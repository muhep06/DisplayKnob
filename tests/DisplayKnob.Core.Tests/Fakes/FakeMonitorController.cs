using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using DisplayKnob.Core.Ddc;
using DisplayKnob.Core.Displays;

namespace DisplayKnob.Core.Tests.Fakes;

/// <summary>
/// In-memory monitor. Thread-safe enough for one DdcWorker loop plus test assertions.
/// </summary>
internal sealed class FakeMonitorController : IMonitorController
{
    private readonly ConcurrentDictionary<byte, VcpValue> _values = new();
    private readonly ConcurrentQueue<(byte Code, uint Value)> _writes = new();

    public FakeMonitorController(string name = "Fake Monitor")
    {
        DisplayName = name;
        Identity = MonitorIdentity.Create(null, null, name);
    }

    public MonitorIdentity Identity { get; }

    public string DisplayName { get; }

    public string? GdiDeviceName { get; set; }

    public string? Capabilities { get; set; } = "(prot(monitor)vcp(10 12 62 8D))";

    /// <summary>Number of upcoming calls (of any kind) that fail before calls start succeeding.</summary>
    public int FailNextCalls { get; set; }

    /// <summary>When set, every call waits on this gate first (lets tests hold the worker busy).</summary>
    public ManualResetEventSlim? Gate { get; set; }

    /// <summary>Signaled when any call starts (before waiting on <see cref="Gate"/>).</summary>
    public ManualResetEventSlim CallStarted { get; } = new(false);

    public int CallCount { get; private set; }

    public bool IsDisposed { get; private set; }

    public IReadOnlyList<(byte Code, uint Value)> Writes => [.. _writes];

    /// <summary>Codes whose writes are acknowledged but ignored (some monitors do this).</summary>
    public HashSet<byte> IgnoredWriteCodes { get; } = [];

    /// <summary>Maps a written value to what the monitor then reports (e.g. Samsung reports 0 after unmute = 2).</summary>
    public Func<byte, uint, uint>? ReportedValueAfterWrite { get; set; }

    public uint? GetCurrent(byte code) => _values.TryGetValue(code, out var v) ? v.Current : null;

    public void SetValue(byte code, uint current, uint maximum = 100) => _values[code] = new VcpValue(current, maximum);

    public bool TryGetVcp(byte code, out VcpValue value)
    {
        value = default;
        return BeginCall() && _values.TryGetValue(code, out value);
    }

    public bool TrySetVcp(byte code, uint value)
    {
        if (!BeginCall())
        {
            return false;
        }

        _writes.Enqueue((code, value));
        if (IgnoredWriteCodes.Contains(code))
        {
            return true;
        }

        var max = _values.TryGetValue(code, out var existing) ? existing.Maximum : 100;
        _values[code] = new VcpValue(ReportedValueAfterWrite?.Invoke(code, value) ?? value, max);
        return true;
    }

    public bool TryGetCapabilities([NotNullWhen(true)] out string? capabilities)
    {
        capabilities = BeginCall() ? Capabilities : null;
        return capabilities is not null;
    }

    public void Dispose()
    {
        IsDisposed = true;
        CallStarted.Dispose();
    }

    private bool BeginCall()
    {
        CallStarted.Set();
        Gate?.Wait(TimeSpan.FromSeconds(10));
        CallCount++;
        if (FailNextCalls > 0)
        {
            FailNextCalls--;
            return false;
        }

        return true;
    }
}
