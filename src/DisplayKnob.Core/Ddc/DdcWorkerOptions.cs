namespace DisplayKnob.Core.Ddc;

/// <summary>
/// Retry, pacing and write-verification policy for <see cref="DdcWorker"/>.
/// </summary>
/// <param name="MaxAttempts">Total attempts per operation (first try included).</param>
/// <param name="RetryDelay">Base delay; attempt <c>n</c> waits <c>n × RetryDelay</c> before retrying.</param>
/// <param name="CommandSpacing">
/// Minimum time between the end of one DDC command and the start of the next (zero: no pause). A monitor that is still
/// busy with a command silently ignores the next one: the call reports success and nothing happens. Measured on a
/// Samsung S34C65xU, the last write of a quick series was lost whenever it followed the previous one by less than about
/// 100 ms (call included), so a dragged brightness slider stopped at an intermediate value instead of the one the
/// user chose. This is where each monitor starts; see <paramref name="VerifiedCodes"/> for how it adapts.
/// </param>
/// <param name="VerifyDelay">
/// How long after the last write the worker reads a verified value back (zero: no verification). Long enough not to
/// get in the way of a key held down or a slider being dragged.
/// </param>
/// <param name="MaxCommandSpacing">The longest spacing the worker will learn (zero: it never grows).</param>
/// <param name="VerifiedCodes">
/// VCP codes whose writes are read back once the writes stop. A monitor can't tell us how long it needs between
/// commands, so the worker finds out by checking whether the last value stuck: if the monitor still reports the
/// value before it, the write was dropped; the worker sends it again and widens the spacing for that monitor.
/// </param>
public sealed record DdcWorkerOptions(
    int MaxAttempts,
    TimeSpan RetryDelay,
    TimeSpan CommandSpacing = default,
    TimeSpan VerifyDelay = default,
    TimeSpan MaxCommandSpacing = default,
    IReadOnlySet<byte>? VerifiedCodes = null)
{
    /// <summary>
    /// 3 attempts (50 ms, then 100 ms between them), 80 ms between commands growing to at most 400 ms, and brightness
    /// and volume read back 150 ms after their last write.
    /// </summary>
    public static DdcWorkerOptions Default { get; } = new(
        3,
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(80),
        TimeSpan.FromMilliseconds(150),
        TimeSpan.FromMilliseconds(400),
        new HashSet<byte> { VcpCodes.Brightness, VcpCodes.AudioVolume });
}
