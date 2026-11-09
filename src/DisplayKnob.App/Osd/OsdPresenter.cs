using DisplayKnob.App.ViewModels;
using DisplayKnob.Core.Displays;
using DisplayKnob.Core.Settings;
using DisplayKnob.Core.Volume;
using Microsoft.UI.Dispatching;

namespace DisplayKnob.App.Osd;

/// <summary>
/// Shows the OSD on the target monitor whenever the volume changes, then fades it out.
/// </summary>
/// <remarks>Must be created on the UI thread. The window is created once and reused.</remarks>
internal sealed class OsdPresenter : IDisposable
{
    private const double WidthDip = 240;
    private const double HeightDip = 52;
    private const double BottomMarginDip = 24;
    private const byte FadeStep = 32; // 255 → 0 in 8 ticks of ~16 ms ≈ 130 ms.

    private static readonly TimeSpan s_visibleFor = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan s_fadeTick = TimeSpan.FromMilliseconds(16);

    private readonly VolumeController _volume;
    private readonly MonitorRegistry _registry;
    private readonly ISettingsStore _settings;
    private readonly DispatcherQueue _dispatcher;
    private readonly OsdViewModel _viewModel = new();
    private readonly OsdWindow _window;
    private readonly DispatcherQueueTimer _hideTimer;
    private readonly DispatcherQueueTimer _fadeTimer;
    private readonly bool _animate;
    private byte _alpha;

    public OsdPresenter(VolumeController volume, MonitorRegistry registry, ISettingsStore settings)
    {
        _volume = volume;
        _registry = registry;
        _settings = settings;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("OsdPresenter must be created on the UI thread.");
        _window = new OsdWindow(_viewModel);
        _animate = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

        _hideTimer = _dispatcher.CreateTimer();
        _hideTimer.Interval = s_visibleFor;
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => StartFadeOut();

        _fadeTimer = _dispatcher.CreateTimer();
        _fadeTimer.Interval = s_fadeTick;
        _fadeTimer.IsRepeating = true;
        _fadeTimer.Tick += (_, _) => FadeTick();

        _volume.VolumeChanged += OnVolumeChanged;
    }

    public void Dispose()
    {
        _volume.VolumeChanged -= OnVolumeChanged;
        _hideTimer.Stop();
        _fadeTimer.Stop();
        _window.Close();
    }

    // Raised on the volume controller's thread.
    private void OnVolumeChanged(object? sender, VolumeChangedEventArgs e)
    {
        if (_settings.Current.ShowOsd)
        {
            _dispatcher.TryEnqueue(() => Show(e));
        }
    }

    private void Show(VolumeChangedEventArgs e)
    {
        _viewModel.Level = e.Level;
        _viewModel.Muted = e.Muted;
        _viewModel.MonitorName = e.MonitorName;
        _viewModel.ShowMonitorName = _registry.Monitors.Count > 1;

        if (DisplayLocator.TryGetWorkArea(e.GdiDeviceName, out var area))
        {
            var width = (int)Math.Round(WidthDip * area.Scale);
            var height = (int)Math.Round(HeightDip * area.Scale);
            var x = area.Left + ((area.Width - width) / 2);
            var y = area.Top + area.Height - height - (int)Math.Round(BottomMarginDip * area.Scale);
            _window.ShowAt(x, y, width, height);
        }

        _fadeTimer.Stop();
        SetAlpha(255);
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void StartFadeOut()
    {
        if (_animate)
        {
            _fadeTimer.Start();
        }
        else
        {
            SetAlpha(0);
            _window.Hide();
        }
    }

    private void FadeTick()
    {
        if (_alpha <= FadeStep)
        {
            _fadeTimer.Stop();
            SetAlpha(0);
            _window.Hide();
            return;
        }

        SetAlpha((byte)(_alpha - FadeStep));
    }

    private void SetAlpha(byte alpha)
    {
        _alpha = alpha;
        _window.SetAlpha(alpha);
    }
}
