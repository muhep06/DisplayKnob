using System.Globalization;
using Serilog;
using Serilog.Core;

namespace DisplayKnob.App;

internal static class LoggingSetup
{
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DisplayKnob",
        "logs");

    public static Logger CreateLogger() =>
        new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(LogDirectory, "displayknob-.log"),
                rollingInterval: RollingInterval.Day,
                formatProvider: CultureInfo.InvariantCulture,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
}
