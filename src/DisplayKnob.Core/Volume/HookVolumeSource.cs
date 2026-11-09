using DisplayKnob.Core.Input;
using DisplayKnob.Core.Platform;

namespace DisplayKnob.Core.Volume;

/// <summary>
/// <see cref="IVolumeSource"/> that intercepts the media volume keys with a low-level keyboard hook.
/// </summary>
/// <remarks>
/// <para>Windows can drop a low-level hook without notice, and there is no API to ask whether it is still
/// installed. The hook is therefore replaced after resume (the usual moment for a slow callback) and every
/// <see cref="ReinstallInterval"/>; replacing a working hook is cheap and invisible.</para>
/// <para>Known limitation: while an elevated (admin) window has focus, UIPI may keep the keys from reaching the hook.</para>
/// </remarks>
internal sealed class HookVolumeSource : IVolumeSource
{
    public static readonly TimeSpan ReinstallInterval = TimeSpan.FromMinutes(10);

    private readonly KeyboardHookService _hook;
    private readonly ISystemEventSource _events;
    private readonly ITimer _reinstallTimer;

    public HookVolumeSource(KeyboardHookService hook, ISystemEventSource events, TimeProvider timeProvider)
    {
        _hook = hook;
        _events = events;
        _reinstallTimer = timeProvider.CreateTimer(_ => _hook.Reinstall(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Start(IVolumeCommandSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        // Runs inside the hook callback: TryPost is non-blocking and decides swallow vs. pass-through.
        _hook.Start(press => sink.TryPost(new VolumeCommand(ToAction(press.Key), press.Fine)));

        _events.SystemEventOccurred += OnSystemEvent;
        _reinstallTimer.Change(ReinstallInterval, ReinstallInterval);
    }

    public void Dispose()
    {
        _events.SystemEventOccurred -= OnSystemEvent;
        _reinstallTimer.Dispose();
        _hook.Dispose();
    }

    private void OnSystemEvent(object? sender, SystemEventArgs e)
    {
        if (e.Kind == SystemEventKind.Resumed)
        {
            _hook.Reinstall();
        }
    }

    private static VolumeAction ToAction(VolumeKey key) => key switch
    {
        VolumeKey.Up => VolumeAction.Up,
        VolumeKey.Down => VolumeAction.Down,
        _ => VolumeAction.ToggleMute,
    };
}
