using DisplayKnob.App.ViewModels;
using DisplayKnob.Core.Displays;
using Serilog;

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
    private readonly ILogger _logger;
    private TrayFlyoutWindow? _window;

    public TrayFlyoutPresenter(TrayFlyoutViewModel viewModel, ILogger logger)
    {
        _viewModel = viewModel;
        _logger = logger.ForContext<TrayFlyoutPresenter>();
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

        // Items are created synchronously, so the size is known right away. Reading the monitors takes longer;
        // a row a monitor turns out not to support is dropped then, and the flyout is resized (see below).
        var loaded = _viewModel.LoadAsync();

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
        var left = area.Left + area.Width - width - margin;
        var bottom = area.Top + area.Height - margin;
        _window.ShowAt(left, bottom - height, width, height);
        _ = ResizeWhenLoadedAsync(loaded, left, bottom, width, area.Scale);
    }

    // The content height can change once the monitors have been read; keep the bottom edge where it is.
    private async Task ResizeWhenLoadedAsync(Task loaded, int left, int bottom, int width, double scale)
    {
        try
        {
            await loaded;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Reading failed (the view model logs the expected cases); the flyout keeps the size it was shown with.
            _logger.Warning(ex, "Reading the monitors for the flyout failed");
            return;
        }

        if (_window is { IsShown: true } window)
        {
            var height = (int)Math.Ceiling(window.MeasureHeight(WidthDip) * scale);
            window.Resize(left, bottom - height, width, height);
        }
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
