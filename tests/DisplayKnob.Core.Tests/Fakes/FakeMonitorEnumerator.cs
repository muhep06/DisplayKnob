using DisplayKnob.Core.Ddc;
using DisplayKnob.Core.Displays;

namespace DisplayKnob.Core.Tests.Fakes;

internal sealed class FakeMonitorEnumerator : IMonitorEnumerator
{
    private readonly Func<IReadOnlyList<FakeMonitorController>> _factory;

    public FakeMonitorEnumerator(Func<IReadOnlyList<FakeMonitorController>> factory)
    {
        _factory = factory;
    }

    public List<FakeMonitorController> Created { get; } = [];

    public IReadOnlyList<IMonitorController> Enumerate()
    {
        var monitors = _factory();
        Created.AddRange(monitors);
        return monitors;
    }
}
