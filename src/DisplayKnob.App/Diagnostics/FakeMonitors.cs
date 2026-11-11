#if DEBUG
using System.Diagnostics.CodeAnalysis;
using DisplayKnob.Core.Ddc;
using DisplayKnob.Core.Displays;
using Microsoft.Extensions.DependencyInjection;

namespace DisplayKnob.App.Diagnostics;

/// <summary>
/// Debug builds only: simulated DDC monitors next to the real ones, to try features that need several monitors
/// (the volume group) on a machine that has one. They keep their values in memory and change nothing on the hardware.
/// </summary>
internal static class FakeMonitors
{
    public static void Register(IServiceCollection services, int count)
    {
        var real = services.Last(d => d.ServiceType == typeof(IMonitorEnumerator));
        var realType = real.ImplementationType ?? throw new InvalidOperationException("The real monitor enumerator is registered by type.");
        services.Remove(real);

        // Shared across re-enumerations, so a fake monitor keeps its volume when the registry rebuilds its workers.
        var states = Enumerable.Range(1, count).Select(i => new FakeState($"Fake monitor {i}")).ToList();
        services.AddSingleton<IMonitorEnumerator>(sp =>
            new FakeEnumerator((IMonitorEnumerator)ActivatorUtilities.CreateInstance(sp, realType), states));
    }

    private sealed class FakeState
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<byte, VcpValue> _values = new()
        {
            [VcpCodes.Brightness] = new VcpValue(50, 100),
            [VcpCodes.AudioVolume] = new VcpValue(40, 100),
            [VcpCodes.AudioMute] = new VcpValue(2, 2),
        };

        public FakeState(string name) => Name = name;

        public string Name { get; }

        public bool TryGet(byte code, out VcpValue value)
        {
            lock (_lock)
            {
                return _values.TryGetValue(code, out value);
            }
        }

        public bool TrySet(byte code, uint value)
        {
            lock (_lock)
            {
                if (!_values.TryGetValue(code, out var existing))
                {
                    return false;
                }

                _values[code] = new VcpValue(value, existing.Maximum);
                return true;
            }
        }
    }

    private sealed class FakeController(FakeState state) : IMonitorController
    {
        public MonitorIdentity Identity { get; } = MonitorIdentity.Create(null, null, state.Name);

        public string DisplayName => state.Name;

        public string? GdiDeviceName => null; // No display of its own: the OSD appears on the primary display.

        public bool TryGetVcp(byte code, out VcpValue value) => state.TryGet(code, out value);

        public bool TrySetVcp(byte code, uint value) => state.TrySet(code, value);

        public bool TryGetCapabilities([NotNullWhen(true)] out string? capabilities)
        {
            capabilities = "(prot(monitor)vcp(10 12 62 8D))";
            return true;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeEnumerator(IMonitorEnumerator real, List<FakeState> states) : IMonitorEnumerator
    {
        public IReadOnlyList<IMonitorController> Enumerate() =>
            [.. real.Enumerate(), .. states.Select(s => new FakeController(s))];
    }
}
#endif
