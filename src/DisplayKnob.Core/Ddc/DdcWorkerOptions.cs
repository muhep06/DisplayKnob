namespace DisplayKnob.Core.Ddc;

/// <summary>
/// Retry policy for <see cref="DdcWorker"/>.
/// </summary>
/// <param name="MaxAttempts">Total attempts per operation (first try included).</param>
/// <param name="RetryDelay">Base delay; attempt <c>n</c> waits <c>n × RetryDelay</c> before retrying.</param>
public sealed record DdcWorkerOptions(int MaxAttempts, TimeSpan RetryDelay)
{
    /// <summary>3 attempts, 50 ms then 100 ms between them.</summary>
    public static DdcWorkerOptions Default { get; } = new(3, TimeSpan.FromMilliseconds(50));
}
