using DisplayKnob.Core.Input;

namespace DisplayKnob.Core.Volume;

/// <summary>
/// <see cref="IVolumeSource"/> that intercepts the media volume keys with a low-level keyboard hook.
/// </summary>
/// <remarks>
/// Known limitation: while an elevated (admin) window has focus, UIPI may keep the keys from reaching the hook.
/// </remarks>
internal sealed class HookVolumeSource : IVolumeSource
{
    private readonly KeyboardHookService _hook;

    public HookVolumeSource(KeyboardHookService hook)
    {
        _hook = hook;
    }

    public void Start(IVolumeCommandSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        // Runs inside the hook callback: TryPost is non-blocking and decides swallow vs. pass-through.
        _hook.Start(press => sink.TryPost(new VolumeCommand(ToAction(press.Key), press.Fine)));
    }

    public void Dispose() => _hook.Dispose();

    private static VolumeAction ToAction(VolumeKey key) => key switch
    {
        VolumeKey.Up => VolumeAction.Up,
        VolumeKey.Down => VolumeAction.Down,
        _ => VolumeAction.ToggleMute,
    };
}
