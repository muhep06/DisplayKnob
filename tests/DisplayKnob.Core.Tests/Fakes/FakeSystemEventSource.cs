using DisplayKnob.Core.Platform;

namespace DisplayKnob.Core.Tests.Fakes;

internal sealed class FakeSystemEventSource : ISystemEventSource
{
    public event EventHandler<SystemEventArgs>? SystemEventOccurred;

    public void Raise(SystemEventKind kind) => SystemEventOccurred?.Invoke(this, new SystemEventArgs(kind));
}
