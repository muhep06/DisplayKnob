using DisplayKnob.Core.Platform;
using Velopack;

namespace DisplayKnob.App;

/// <summary>
/// Entry point. Velopack must run first: during install, update and uninstall it launches the exe
/// with special arguments, handles them and exits before any UI is created.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // On uninstall, remove the "start with Windows" entries so none is left pointing at a deleted exe.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => StartupEntries.Remove())
            .Run();

        // The XAML compiler's own Main is disabled (DISABLE_XAML_GENERATED_MAIN); this is its body.
        XamlGeneratedProgram.XamlGeneratedMain();
    }
}
