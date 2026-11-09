using DisplayKnob.App.ViewModels;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace DisplayKnob.App.Views;

/// <summary>The settings window. Created on demand and closed (not hidden) when the user closes it.</summary>
internal sealed partial class SettingsWindow : Window
{
    private const int WidthDip = 820;
    private const int HeightDip = 720;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        ViewModel.Load();
        InitializeComponent();

        // AppWindow sizes are in physical pixels.
        var hwnd = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var scale = PInvoke.GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(WidthDip * scale), (int)(HeightDip * scale)));
    }

    public SettingsViewModel ViewModel { get; }
}
