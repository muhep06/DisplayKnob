using Windows.Win32;

namespace DisplayKnob.App;

/// <summary>
/// Keeps DisplayKnob to one running copy per Windows session. Two copies would both install the keyboard hook and
/// handle every volume key twice (a mute would undo itself), and nothing on screen tells a user that the first
/// launch is still starting, so they launch it again.
/// </summary>
/// <remarks>
/// A second launch asks the running copy to open its tray flyout, as a click on the tray icon would (so it answers
/// "yes, it is running"), and exits.
/// </remarks>
internal static class SingleInstance
{
    /// <summary>Name of the event that tells the running copy that the app was launched again.</summary>
    internal const string LaunchedAgainEventName = @"Local\DisplayKnob.LaunchedAgain";

    private const string MutexName = @"Local\DisplayKnob.SingleInstance";

    // After an update the new copy starts while the old one is still shutting down; it waits this long for it to let go.
    private static readonly TimeSpan s_handoverGrace = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Becomes the one running copy.
    /// </summary>
    /// <returns>
    /// The held mutex (keep it until the process exits), or <see langword="null"/> when another copy is running:
    /// it has been asked to open its flyout, and this process should exit.
    /// </returns>
    public static Mutex? TryEnter()
    {
        var mutex = new Mutex(initiallyOwned: false, MutexName);
        if (TryAcquire(mutex, TimeSpan.Zero))
        {
            return mutex;
        }

        // This process was just started by the user, so it may let the running copy come to the front; without it
        // Windows can refuse to activate the flyout of a background process.
        PInvoke.AllowSetForegroundWindow(PInvoke.ASFW_ANY);
        using (var launchedAgain = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, LaunchedAgainEventName))
        {
            launchedAgain.Set();
        }

        if (TryAcquire(mutex, s_handoverGrace))
        {
            return mutex; // The other copy was on its way out (an update restart).
        }

        mutex.Dispose();
        return null;
    }

    // A copy that was killed leaves the mutex "abandoned"; the next waiter still gets ownership.
    private static bool TryAcquire(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }
}
