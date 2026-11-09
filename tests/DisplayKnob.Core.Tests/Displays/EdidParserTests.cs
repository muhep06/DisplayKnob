using System.Text;
using DisplayKnob.Core.Displays;

namespace DisplayKnob.Core.Tests.Displays;

public sealed class EdidParserTests
{
    [Fact]
    public void TryParse_ValidEdid_ReadsIdentityFields()
    {
        var edid = BuildEdid(manufacturer: 0x10AC, productCode: 0x4123, serial: 0x12345678, name: "DELL U2723QE", serialText: "ABC123");

        var ok = EdidParser.TryParse(edid, out var info);

        Assert.True(ok);
        Assert.Equal("DEL", info!.ManufacturerId);
        Assert.Equal(0x4123, info.ProductCode);
        Assert.Equal(0x12345678u, info.SerialNumber);
        Assert.Equal("DELL U2723QE", info.Name);
        Assert.Equal("ABC123", info.SerialText);
    }

    [Fact]
    public void TryParse_NoTextDescriptors_LeavesTextNull()
    {
        var edid = BuildEdid(manufacturer: 0x10AC, productCode: 1, serial: 0, name: null, serialText: null);

        var ok = EdidParser.TryParse(edid, out var info);

        Assert.True(ok);
        Assert.Null(info!.Name);
        Assert.Null(info.SerialText);
    }

    [Fact]
    public void TryParse_BadHeader_ReturnsFalse()
    {
        var edid = BuildEdid(manufacturer: 0x10AC, productCode: 1, serial: 0, name: null, serialText: null);
        edid[1] = 0x00;

        var ok = EdidParser.TryParse(edid, out var info);

        Assert.False(ok);
        Assert.Null(info);
    }

    [Fact]
    public void TryParse_TooShort_ReturnsFalse()
    {
        var ok = EdidParser.TryParse(new byte[64], out _);

        Assert.False(ok);
    }

    [Fact]
    public void ToKey_PrefersSerialText()
    {
        var info = new EdidInfo("DEL", 0x4123, 0x12345678, "ABC123", null);

        Assert.Equal("DEL4123-ABC123", info.ToKey());
    }

    [Fact]
    public void ToKey_NoSerialText_UsesNumericSerial()
    {
        var info = new EdidInfo("DEL", 0x4123, 0x12345678, null, null);

        Assert.Equal("DEL4123-12345678", info.ToKey());
    }

    internal static byte[] BuildEdid(ushort manufacturer, ushort productCode, uint serial, string? name, string? serialText)
    {
        var edid = new byte[128];
        byte[] header = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];
        header.CopyTo(edid, 0);

        edid[8] = (byte)(manufacturer >> 8);
        edid[9] = (byte)manufacturer;
        BitConverter.TryWriteBytes(edid.AsSpan(10), productCode);
        BitConverter.TryWriteBytes(edid.AsSpan(12), serial);

        // Descriptor 0: a detailed timing descriptor (non-zero pixel clock), must be skipped.
        edid[54] = 0x01;
        edid[55] = 0x02;

        WriteTextDescriptor(edid, 72, 0xFC, name);
        WriteTextDescriptor(edid, 90, 0xFF, serialText);
        return edid;
    }

    private static void WriteTextDescriptor(byte[] edid, int offset, byte tag, string? text)
    {
        if (text is null)
        {
            // Dummy descriptor (tag 0x10).
            edid[offset + 3] = 0x10;
            return;
        }

        edid[offset + 3] = tag;
        var bytes = Encoding.ASCII.GetBytes(text + "\n").AsSpan();
        var field = edid.AsSpan(offset + 5, 13);
        field.Fill(0x20);
        bytes[..Math.Min(bytes.Length, 13)].CopyTo(field);
    }
}
