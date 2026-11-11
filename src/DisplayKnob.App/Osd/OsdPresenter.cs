using DisplayKnob.App.ViewModels;
using DisplayKnob.Core.Audio;
using DisplayKnob.Core.Displays;
using DisplayKnob.Core.Settings;
using DisplayKnob.Core.Volume;
using Microsoft.UI.Dispatching;

namespace DisplayKnob.App.Osd;

/// <summary>
/// Shows the OSD on the monitor whose volume changed, then fades it out. While the volume group is active, every
/// monitor of the group gets its own indicator (or only the audio monitor does, see <see cref="GroupOsdMode"/>).
/// </summary>
/// <remarks>Must be created on the UI thread. One window per display is created on first use and reused.</remarks>
internal sealed class OsdPresenter : IDisposable
{
    // Roughly the size of the Windows 11 volume flyout.
    private const double WidthDip = 192;
    private const double HeightDip = 46;
    private const double BottomMarginDip = 24;
    private const byte FadeStep = 32; // 255 → 0 in 8 ticks of ~16 ms ≈ 130 ms.

    private static readonly TimeSpan s_visibleFor = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan s_fadeTick = TimeSpan.FromMilliseconds(16);

    private readonly VolumeController _volume;
    private readonly AudioMonitorMapper _mapper;
    private readonly MonitorRegistry _registry;
    private readonly ISettingsStore _settings;
    private readonly DispatcherQueue _dispatcher;
    private readonly Dictionary<string, Slot> _slots = []; // By GDI device name; "" when the display is unknown.
    private readonly bool _animate;

    public OsdPresenter(VolumeController volume, AudioMonitorMapper mapper, MonitorRegistry registry, ISettingsStore settings)
    {
        _volume = volume;
        _mapper = mapper;
        _registry = registry;
        _settings = settings;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("OsdPresenter must be created on the UI thread.");
        _animate = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

        _volume.VolumeChanged += OnVolumeChanged;
        _volume.VolumeGroupToggled += OnVolumeGroupToggled;
    }

    public void Dispose()
    {
        _volume.VolumeChanged -= OnVolumeChanged;
        _volume.VolumeGroupToggled -= OnVolumeGroupToggled;
        foreach (var slot in _slots.Values)
        {
            slot.HideTimer.Stop();
            slot.FadeTimer.Stop();
            slot.Window.Close();
        }
    }

    // Raised on the volume controller's thread.
    private void OnVolumeChanged(object? sender, VolumeChangedEventArgs e)
    {
        // Quiet re-reads (after a KVM switch or resume) only update the tray icon.
        if (!e.UserInitiated || !_settings.Current.ShowOsd)
        {
            return;
        }

        // With the group set to one indicator, only the monitor the audio plays through shows it.
        var settings = _settings.Current;
        if (settings is { VolumeGroupEnabled: true, VolumeGroupOsd: GroupOsdMode.TargetOnly }
            && e.MonitorKey is not null && _mapper.Current is { } target && e.MonitorKey != target.Monitor.Identity.Key)
        {
            return;
        }

        _dispatcher.TryEnqueue(() => Show(e));
    }

    private void OnVolumeGroupToggled(object? sender, VolumeGroupToggledEventArgs e)
    {
        if (_settings.Current.ShowOsd)
        {
            _dispatcher.TryEnqueue(() => ShowMessage(e.Enabled ? "Volume group on" : "Volume group off", e.GdiDeviceName));
        }
    }

    /// <summary>
    /// Shows the OSD with sample values on the primary display and keeps it up until the next volume change
    /// (development aid for checking the look).
    /// </summary>
    public void Preview(int level, bool muted)
    {
        Show(new VolumeChangedEventArgs("Preview monitor", null, level, muted));
        GetSlot(null).HideTimer.Stop();
    }

    private void Show(VolumeChangedEventArgs e)
    {
        var slot = GetSlot(e.GdiDeviceName);
        slot.ViewModel.Message = string.Empty;
        slot.ViewModel.Level = e.Level;
        slot.ViewModel.Muted = e.Muted;
        slot.ViewModel.MonitorName = e.MonitorName;
        slot.ViewModel.ShowMonitorName = _registry.Monitors.Count > 1;
        Present(slot, e.GdiDeviceName);
    }

    private void ShowMessage(string message, string? gdiDeviceName)
    {
        var slot = GetSlot(gdiDeviceName);
        slot.ViewModel.Message = message;
        Present(slot, gdiDeviceName);
    }

    private static void Present(Slot slot, string? gdiDeviceName)
    {
        if (DisplayLocator.TryGetWorkArea(gdiDeviceName, out var area))
        {
            var width = (int)Math.Round(WidthDip * area.Scale);
            var height = (int)Math.Round(HeightDip * area.Scale);
            var x = area.Left + ((area.Width - width) / 2);
            var y = area.Top + area.Height - height - (int)Math.Round(BottomMarginDip * area.Scale);
            slot.Window.ShowAt(x, y, width, height);
        }

        slot.FadeTimer.Stop();
        SetAlpha(slot, 255);
        slot.HideTimer.Stop();
        slot.HideTimer.Start();
    }

    private Slot GetSlot(string? gdiDeviceName)
    {
        var key = gdiDeviceName ?? string.Empty;
        if (_slots.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var viewModel = new OsdViewModel();
        var slot = new Slot(viewModel, new OsdWindow(viewModel), _dispatcher.CreateTimer(), _dispatcher.CreateTimer());
        slot.HideTimer.Interval = s_visibleFor;
        slot.HideTimer.IsRepeating = false;
        slot.HideTimer.Tick += (_, _) => StartFadeOut(slot);
        slot.FadeTimer.Interval = s_fadeTick;
        slot.FadeTimer.IsRepeating = true;
        slot.FadeTimer.Tick += (_, _) => FadeTick(slot);
        _slots[key] = slot;
        return slot;
    }

    private void StartFadeOut(Slot slot)
    {
        if (_animate)
        {
            slot.FadeTimer.Start();
        }
        else
        {
            SetAlpha(slot, 0);
            slot.Window.Hide();
        }
    }

    private static void FadeTick(Slot slot)
    {
        if (slot.Alpha <= FadeStep)
        {
            slot.FadeTimer.Stop();
            SetAlpha(slot, 0);
            slot.Window.Hide();
            return;
        }

        SetAlpha(slot, (byte)(slot.Alpha - FadeStep));
    }

    private static void SetAlpha(Slot slot, byte alpha)
    {
        slot.Alpha = alpha;
        slot.Window.SetAlpha(alpha);
    }

    // The window, state and timers of the indicator on one display.
    private sealed class Slot(OsdViewModel viewModel, OsdWindow window, DispatcherQueueTimer hideTimer, DispatcherQueueTimer fadeTimer)
    {
        public OsdViewModel ViewModel { get; } = viewModel;

        public OsdWindow Window { get; } = window;

        public DispatcherQueueTimer HideTimer { get; } = hideTimer;

        public DispatcherQueueTimer FadeTimer { get; } = fadeTimer;

        public byte Alpha { get; set; }
    }
}
