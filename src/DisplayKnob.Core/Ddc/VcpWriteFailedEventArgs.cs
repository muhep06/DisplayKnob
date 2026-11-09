namespace DisplayKnob.Core.Ddc;

/// <summary>Data for <see cref="DdcWorker.WriteFailed"/>.</summary>
public sealed class VcpWriteFailedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public VcpWriteFailedEventArgs(byte code, uint value)
    {
        Code = code;
        Value = value;
    }

    /// <summary>The VCP code that could not be written.</summary>
    public byte Code { get; }

    /// <summary>The value that could not be written.</summary>
    public uint Value { get; }
}
