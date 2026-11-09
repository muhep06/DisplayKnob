using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using Serilog;

namespace DisplayKnob.Core.Audio;

/// <summary>
/// <see cref="IAudioEndpointWatcher"/> over Core Audio (IMMDeviceEnumerator / IMMNotificationClient) via NAudio.
/// </summary>
internal sealed class NAudioEndpointWatcher : IAudioEndpointWatcher
{
    // The media volume keys act on the multimedia default endpoint.
    private const Role WatchedRole = Role.Multimedia;

    // Passed as the event context of our own volume changes, so their notifications can be told apart.
    private static readonly Guid s_ownContext = new("5e0c64b0-3f6d-4c43-9a3e-6f1d2b7c9a10");

    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly MMDeviceEnumerator _enumerator;
    private readonly MMDeviceNotificationClient _notifications;
    private AudioEndpoint? _default;
    private MMDevice? _defaultDevice; // Guarded by _lock; kept alive for its volume notifications.
    private AudioEndpointVolumeNotificationDelegate? _volumeHandler; // Guarded by _lock.
    private bool _disposed;

    public NAudioEndpointWatcher(ILogger logger)
    {
        _logger = logger.ForContext<NAudioEndpointWatcher>();
        _enumerator = new MMDeviceEnumerator();

        // Callbacks arrive on a Core Audio thread; we hop to the thread pool ourselves.
        _notifications = _enumerator.CreateNotificationClient(useSynchronizationContext: false);
        _notifications.DefaultDeviceChanged += OnDefaultDeviceChanged;

        RefreshDefault();
    }

    public event EventHandler? DefaultRenderEndpointChanged;

    public event EventHandler<EndpointVolumeChangedEventArgs>? DefaultEndpointVolumeChanged;

    public AudioEndpoint? DefaultRenderEndpoint => Volatile.Read(ref _default);

    public IReadOnlyList<AudioEndpoint> GetRenderEndpoints()
    {
        try
        {
            var endpoints = new List<AudioEndpoint>();
            foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    endpoints.Add(new AudioEndpoint(device.ID, device.FriendlyName));
                }
            }

            return endpoints;
        }
        catch (COMException ex)
        {
            _logger.Warning(ex, "Could not list audio outputs");
            return [];
        }
    }

    public bool TrySetVolume(string endpointId, int level)
    {
        var scalar = Math.Clamp(level, 0, 100) / 100f;
        try
        {
            using var device = _enumerator.GetDevice(endpointId);
            var volume = device.AudioEndpointVolume;
            volume.NotificationGuid = s_ownContext;
            if (ToLevel(volume.MasterVolumeLevelScalar) != Math.Clamp(level, 0, 100))
            {
                volume.MasterVolumeLevelScalar = scalar;
            }

            if (volume.Mute)
            {
                volume.Mute = false;
            }

            return true;
        }
        catch (COMException ex)
        {
            // The device may have just been unplugged; not fatal.
            _logger.Warning(ex, "Could not set the Windows volume of endpoint {EndpointId}", endpointId);
            return false;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            ReplaceDefaultDevice(null);
        }

        _notifications.DefaultDeviceChanged -= OnDefaultDeviceChanged;
        _notifications.Dispose();
        _enumerator.Dispose();
    }

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        if (e.Flow != DataFlow.Render || e.Role != WatchedRole)
        {
            return;
        }

        // Calling back into Core Audio from inside the notification can deadlock; defer it.
        ThreadPool.QueueUserWorkItem(_ => RefreshDefault());
    }

    private void RefreshDefault()
    {
        AudioEndpoint? endpoint;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            endpoint = ReadDefault(out var device);
            if (endpoint == _default)
            {
                device?.Dispose();
                return;
            }

            ReplaceDefaultDevice(device);
            Volatile.Write(ref _default, endpoint);
        }

        _logger.Information("Default audio output: {Endpoint}", endpoint?.FriendlyName ?? "(none)");
        DefaultRenderEndpointChanged?.Invoke(this, EventArgs.Empty);
    }

    private AudioEndpoint? ReadDefault(out MMDevice? device)
    {
        device = null;
        try
        {
            if (!_enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, WatchedRole, out var found))
            {
                return null;
            }

            device = found;
            return new AudioEndpoint(found.ID, found.FriendlyName);
        }
        catch (COMException ex)
        {
            device?.Dispose();
            device = null;
            _logger.Warning(ex, "Could not read the default audio output");
            return null;
        }
    }

    // Called under _lock.
    private void ReplaceDefaultDevice(MMDevice? device)
    {
        if (_defaultDevice is not null)
        {
            if (_volumeHandler is not null)
            {
                _defaultDevice.AudioEndpointVolume.OnVolumeNotification -= _volumeHandler;
                _volumeHandler = null;
            }

            _defaultDevice.Dispose();
        }

        _defaultDevice = device;
        if (device is null)
        {
            return;
        }

        try
        {
            var id = device.ID;
            _volumeHandler = data =>
            {
                var args = new EndpointVolumeChangedEventArgs(id, ToLevel(data.MasterVolume), data.Muted, data.EventContext == s_ownContext);

                // Arrives on a Core Audio thread; calling back into Core Audio from here can deadlock.
                ThreadPool.QueueUserWorkItem(_ => DefaultEndpointVolumeChanged?.Invoke(this, args));
            };
            device.AudioEndpointVolume.OnVolumeNotification += _volumeHandler;
        }
        catch (COMException ex)
        {
            _logger.Warning(ex, "Could not watch the Windows volume of {Endpoint}", device.FriendlyName);
        }
    }

    private static int ToLevel(float scalar) => (int)Math.Round(scalar * 100, MidpointRounding.AwayFromZero);
}
