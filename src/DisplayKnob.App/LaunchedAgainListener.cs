using Microsoft.UI.Dispatching;

namespace DisplayKnob.App;

/// <summary>
/// Runs an action on the UI thread whenever another copy of the app was launched (see <see cref="SingleInstance"/>).
/// </summary>
internal sealed class LaunchedAgainListener : IDisposable
{
    private readonly EventWaitHandle _event = new(initialState: false, EventResetMode.AutoReset, SingleInstance.LaunchedAgainEventName);
    private readonly RegisteredWaitHandle _registration;

    public LaunchedAgainListener(DispatcherQueue dispatcher, Action onLaunchedAgain)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(onLaunchedAgain);

        // The callback runs on a pool thread; the settings window is UI-thread only. A launch that happened while this
        // copy was still starting is not lost: the event stays set until the first wait.
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _event,
            (_, _) => dispatcher.TryEnqueue(() => onLaunchedAgain()),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration.Unregister(null);
        _event.Dispose();
    }
}
