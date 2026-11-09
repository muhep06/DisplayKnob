using DisplayKnob.Core.Settings;

namespace DisplayKnob.Core.Tests.Fakes;

internal sealed class InMemorySettingsStore : ISettingsStore
{
    public event EventHandler? Changed;

    public DisplayKnobSettings Current { get; private set; } = DisplayKnobSettings.Default;

    public void Update(Func<DisplayKnobSettings, DisplayKnobSettings> change)
    {
        Current = change(Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
