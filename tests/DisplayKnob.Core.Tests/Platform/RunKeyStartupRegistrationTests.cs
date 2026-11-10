using DisplayKnob.Core.Platform;
using Microsoft.Win32;
using Serilog.Core;

namespace DisplayKnob.Core.Tests.Platform;

// Uses real registry keys, but under a key of its own that is deleted afterwards.
public sealed class RunKeyStartupRegistrationTests : IDisposable
{
    private const string Command = "\"C:\\Apps\\DisplayKnob\\current\\DisplayKnob.exe\"";

    private readonly string _root = $@"Software\DisplayKnobTests\{Guid.NewGuid():N}";

    private string RunKey => _root + @"\Run";

    private string ApprovedKey => _root + @"\StartupApproved";

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);

    private RunKeyStartupRegistration Create(string? command = Command) => new(Logger.None, command, RunKey, ApprovedKey);

    private void SetApproved(params byte[] bytes)
    {
        using var key = Registry.CurrentUser.CreateSubKey(ApprovedKey);
        key.SetValue(StartupEntries.ValueName, bytes, RegistryValueKind.Binary);
    }

    private object? RunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(StartupEntries.ValueName);
    }

    private object? ApprovedValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return key?.GetValue(StartupEntries.ValueName);
    }

    [Fact]
    public void IsEnabled_Nothing_IsFalse() => Assert.False(Create().IsEnabled);

    [Fact]
    public void TrySetEnabled_True_WritesTheRunValueAndIsEnabled()
    {
        var registration = Create();

        var ok = registration.TrySetEnabled(true);

        Assert.True(ok);
        Assert.Equal(Command, RunValue());
        Assert.True(registration.IsEnabled);
    }

    [Fact]
    public void TrySetEnabled_False_RemovesTheRunValue()
    {
        var registration = Create();
        registration.TrySetEnabled(true);

        registration.TrySetEnabled(false);

        Assert.Null(RunValue());
        Assert.False(registration.IsEnabled);
    }

    [Fact]
    public void IsEnabled_RunValuePointsElsewhere_IsFalse()
    {
        Create(@"""C:\Old\DisplayKnob.exe""").TrySetEnabled(true);

        Assert.False(Create().IsEnabled);
    }

    [Fact]
    public void IsEnabled_DisabledInTaskManager_IsFalse()
    {
        var registration = Create();
        registration.TrySetEnabled(true);

        SetApproved(0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        Assert.False(registration.IsEnabled);
    }

    [Fact]
    public void IsEnabled_ApprovedInTaskManager_IsTrue()
    {
        var registration = Create();
        registration.TrySetEnabled(true);

        SetApproved(0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        Assert.True(registration.IsEnabled);
    }

    [Fact]
    public void TrySetEnabled_True_ClearsAFlagSetInTaskManager()
    {
        var registration = Create();
        registration.TrySetEnabled(true);
        SetApproved(0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        registration.TrySetEnabled(true);

        Assert.Null(ApprovedValue());
        Assert.True(registration.IsEnabled);
    }

    [Fact]
    public void TrySetEnabled_NoExecutablePath_ReturnsFalse() => Assert.False(Create(command: null).TrySetEnabled(true));

    [Fact]
    public void Remove_DeletesBothEntries()
    {
        Create().TrySetEnabled(true);
        SetApproved(0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        var removed = StartupEntries.Remove(RunKey, ApprovedKey);

        Assert.True(removed);
        Assert.Null(RunValue());
        Assert.Null(ApprovedValue());
    }

    [Fact]
    public void Remove_NothingThere_StillSucceeds() => Assert.True(StartupEntries.Remove(RunKey, ApprovedKey));
}
