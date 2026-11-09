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

    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly MMDeviceEnumerator _enumerator;
    private readonly MMDeviceNotificationClient _notifications;
    private AudioEndpoint? _default;
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

    public AudioEndpoint? DefaultRenderEndpoint => Volatile.Read(ref _default);

    public bool TryPinVolumeToMax(string endpointId)
    {
        try
        {
            using var device = _enumerator.GetDevice(endpointId);
            var volume = device.AudioEndpointVolume;
            if (volume.MasterVolumeLevelScalar < 1f)
            {
                volume.MasterVolumeLevelScalar = 1f;
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

            endpoint = ReadDefault();
            if (endpoint == _default)
            {
                return;
            }

            Volatile.Write(ref _default, endpoint);
        }

        _logger.Information("Default audio output: {Endpoint}", endpoint?.FriendlyName ?? "(none)");
        DefaultRenderEndpointChanged?.Invoke(this, EventArgs.Empty);
    }

    private AudioEndpoint? ReadDefault()
    {
        try
        {
            if (!_enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, WatchedRole, out var device))
            {
                return null;
            }

            using (device)
            {
                return new AudioEndpoint(device.ID, device.FriendlyName);
            }
        }
        catch (COMException ex)
        {
            _logger.Warning(ex, "Could not read the default audio output");
            return null;
        }
    }
}
