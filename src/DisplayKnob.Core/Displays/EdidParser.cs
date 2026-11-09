using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DisplayKnob.Core.Displays;

/// <summary>
/// Parses the identity fields of an EDID 1.x base block (VESA E-EDID).
/// </summary>
public static class EdidParser
{
    private const int BaseBlockLength = 128;
    private const int FirstDescriptorOffset = 54;
    private const int DescriptorLength = 18;
    private const int DescriptorCount = 4;
    private const int DescriptorTextLength = 13;
    private const byte SerialTextTag = 0xFF;
    private const byte NameTag = 0xFC;

    private static ReadOnlySpan<byte> Header => [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    /// <summary>
    /// Parses an EDID base block.
    /// </summary>
    /// <param name="edid">Raw EDID bytes (at least 128).</param>
    /// <param name="info">The parsed identity when successful.</param>
    /// <returns><see langword="true"/> when the data has a valid EDID header.</returns>
    public static bool TryParse(ReadOnlySpan<byte> edid, [NotNullWhen(true)] out EdidInfo? info)
    {
        info = null;
        if (edid.Length < BaseBlockLength || !edid[..Header.Length].SequenceEqual(Header))
        {
            return false;
        }

        var manufacturer = DecodeManufacturerId(BinaryPrimitives.ReadUInt16BigEndian(edid[8..10]));
        var productCode = BinaryPrimitives.ReadUInt16LittleEndian(edid[10..12]);
        var serialNumber = BinaryPrimitives.ReadUInt32LittleEndian(edid[12..16]);

        string? serialText = null;
        string? name = null;
        for (var i = 0; i < DescriptorCount; i++)
        {
            var descriptor = edid.Slice(FirstDescriptorOffset + (i * DescriptorLength), DescriptorLength);

            // Display descriptors start with 00 00 00 <tag>; detailed timing descriptors don't.
            if (descriptor[0] != 0 || descriptor[1] != 0 || descriptor[2] != 0)
            {
                continue;
            }

            switch (descriptor[3])
            {
                case SerialTextTag:
                    serialText = DecodeDescriptorText(descriptor[5..]);
                    break;
                case NameTag:
                    name = DecodeDescriptorText(descriptor[5..]);
                    break;
            }
        }

        info = new EdidInfo(manufacturer, productCode, serialNumber, serialText, name);
        return true;
    }

    // Three 5-bit letters, 'A' = 1, packed big-endian into bits 14..0.
    private static string DecodeManufacturerId(ushort value) =>
        new([
            (char)('A' - 1 + ((value >> 10) & 0x1F)),
            (char)('A' - 1 + ((value >> 5) & 0x1F)),
            (char)('A' - 1 + (value & 0x1F)),
        ]);

    // Text is ASCII, terminated by 0x0A and padded with spaces.
    private static string? DecodeDescriptorText(ReadOnlySpan<byte> text)
    {
        text = text[..DescriptorTextLength];
        var end = text.IndexOf((byte)0x0A);
        if (end >= 0)
        {
            text = text[..end];
        }

        var result = Encoding.ASCII.GetString(text).Trim();
        return result.Length == 0 ? null : result;
    }
}
