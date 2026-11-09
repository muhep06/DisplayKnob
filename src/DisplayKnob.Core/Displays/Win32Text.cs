namespace DisplayKnob.Core.Displays;

internal static class Win32Text
{
    /// <summary>Converts a fixed, null-terminated UTF-16 buffer to a string.</summary>
    public static string FromBuffer(ReadOnlySpan<char> buffer)
    {
        var end = buffer.IndexOf('\0');
        return new string(end < 0 ? buffer : buffer[..end]);
    }
}
