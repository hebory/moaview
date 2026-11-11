using System.Diagnostics;
using System.Runtime.InteropServices;
using MoaView.Services;
using Xunit;

namespace MoaView.Tests;

public sealed class ExplorerSpaceRecoveryTests
{
    [Fact]
    public async Task Hook_ReinstallsAfterNativeRegistrationIsRemoved()
    {
        using var hook = new ExplorerSpaceKeyHook(
            _ => { },
            TimeSpan.FromMilliseconds(200));

        var original = hook.RegistrationHandle;
        Assert.NotEqual(nint.Zero, original);
        Assert.True(UnhookWindowsHookEx(original), $"Could not remove test hook: {Marshal.GetLastWin32Error()}");

        Assert.True(await EventuallyAsync(
            () => hook.RecoveryCount >= 1 && hook.RegistrationCount >= 2,
            TimeSpan.FromSeconds(4)));
        Assert.NotEqual(nint.Zero, hook.RegistrationHandle);
    }

    [Fact]
    public async Task Hook_RenewsHealthyRegistrationAndStopsRenewingAfterDispose()
    {
        using var hook = new ExplorerSpaceKeyHook(
            _ => { },
            TimeSpan.FromMilliseconds(100));

        Assert.True(await EventuallyAsync(() => hook.RegistrationCount >= 3, TimeSpan.FromSeconds(3)));
        Assert.Equal(0, hook.RecoveryCount);
        Assert.NotEqual(nint.Zero, hook.RegistrationHandle);

        hook.Dispose();
        Assert.Equal(0, hook.RegistrationHandle);
        var disposedRegistrationCount = hook.RegistrationCount;
        await Task.Delay(350);

        Assert.Equal(disposedRegistrationCount, hook.RegistrationCount);
        Assert.Equal(0, hook.RegistrationHandle);
    }

    [Fact]
    public void Gesture_RecoversOnlyAfterKeyReleaseAndLongQuietPeriod()
    {
        var gesture = new SpaceKeyGesture();

        Assert.Equal(SpaceKeyAction.Toggle, gesture.Handle(isDown: true, eligible: true));
        Assert.True(gesture.IsPressed);
        Assert.False(gesture.RecoverReleasedKey(physicallyDown: true, TimeSpan.FromSeconds(10)));
        Assert.Equal(SpaceKeyAction.Consume, gesture.Handle(isDown: true, eligible: true)); // Typematic repeat.
        Assert.False(gesture.RecoverReleasedKey(physicallyDown: false, TimeSpan.FromMilliseconds(1499)));
        Assert.True(gesture.RecoverReleasedKey(physicallyDown: false, TimeSpan.FromMilliseconds(1500)));
        Assert.False(gesture.IsPressed);

        Assert.Equal(SpaceKeyAction.Toggle, gesture.Handle(isDown: true, eligible: true));
        Assert.Equal(SpaceKeyAction.Consume, gesture.Handle(isDown: false, eligible: true));
    }

    private static async Task<bool> EventuallyAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            if (predicate()) return true;
            await Task.Delay(20);
        }

        return predicate();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);
}
