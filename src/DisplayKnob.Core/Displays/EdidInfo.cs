using System.Globalization;

namespace DisplayKnob.Core.Displays;

/// <summary>
/// Identity fields parsed from a monitor's EDID base block.
/// </summary>
/// <param name="ManufacturerId">Three-letter PNP manufacturer ID, e.g. <c>DEL</c>.</param>
/// <param name="ProductCode">Manufacturer product code.</param>
/// <param name="SerialNumber">Numeric serial number (0 when not set).</param>
/// <param name="SerialText">Serial number descriptor text (0xFF), if present.</param>
/// <param name="Name">Monitor name descriptor text (0xFC), if present.</param>
public sealed record EdidInfo(
    string ManufacturerId,
    ushort ProductCode,
    uint SerialNumber,
    string? SerialText,
    string? Name)
{
    /// <summary>
    /// A compact key that identifies this monitor model and unit, e.g. <c>DEL4123-ABC123</c>.
    /// </summary>
    public string ToKey()
    {
        var serial = !string.IsNullOrEmpty(SerialText)
            ? SerialText
            : SerialNumber.ToString("X8", CultureInfo.InvariantCulture);

        return string.Create(CultureInfo.InvariantCulture, $"{ManufacturerId}{ProductCode:X4}-{serial}");
    }
}
