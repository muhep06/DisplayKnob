using DisplayKnob.App.ViewModels;
using DisplayKnob.Core.Displays;

namespace DisplayKnob.App.Tray;

/// <summary>Opens and closes the tray flyout. Must be created on the UI thread.</summary>
internal sealed class TrayFlyoutPresenter : IDisposable
{
    private const double WidthDip = 360;
    private const double MarginDip = 12;

    // Clicking the tray icon while the flyout is open first deactivates (hides) it; the click itself must
    // not open it again right away.
    private const long ReopenGuardMs = 300;

    private readonly TrayFlyoutViewModel _viewModel;
    private TrayFlyoutWindow? _window;

    public TrayFlyoutPresenter(TrayFlyoutViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public event EventHandler? SettingsRequested;

    public void Toggle(Guid? iconId)
    {
        _window ??= CreateWindow();
        if (_window.IsShown)
        {
            _window.Hide();
            return;
        }

        if (Environment.TickCount64 - _window.HiddenAt < ReopenGuardMs)
        {
            return;
        }

        _ = _viewModel.LoadAsync(); // Items are created synchronously, so the size is known right away.

        var (x, y) = (0, 0);
        if (iconId is { } id && TrayIconLocator.TryGetRect(id, out var icon))
        {
            (x, y) = ((icon.left + icon.right) / 2, (icon.top + icon.bottom) / 2);
        }

        if (!DisplayLocator.TryGetWorkAreaAt(x, y, out var area))
        {
            return;
        }

        // Bottom-right of the work area, like the Windows 11 flyouts.
        var width = (int)Math.Round(WidthDip * area.Scale);
        var height = (int)Math.Ceiling(_window.MeasureHeight(WidthDip) * area.Scale);
        var margin = (int)Math.Round(MarginDip * area.Scale);
        _window.ShowAt(area.Left + area.Width - width - margin, area.Top + area.Height - height - margin, width, height);
    }

    public void Dispose()
    {
        _viewModel.Dispose();
        _window?.Close();
    }

    private TrayFlyoutWindow CreateWindow()
    {
        var window = new TrayFlyoutWindow(_viewModel);
        window.SettingsRequested += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        return window;
    }
}
