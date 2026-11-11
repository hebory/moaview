using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace MoaView.Services;

public enum SpaceKeyAction { PassThrough, Consume, Toggle }

/// <summary>A held Space toggles at most once, including when focus changes during the press.</summary>
public sealed class SpaceKeyGesture
{
    private bool _pressed;
    private bool _consumed;

    public bool IsPressed => _pressed;

    // A key-up can be lost when Windows removes a hook or switches input desktops.
    // Do not reset during typematic repeats: a consumed key need not appear down to GetAsyncKeyState.
    public bool RecoverReleasedKey(bool physicallyDown, TimeSpan quietFor)
    {
        if (!_pressed || physicallyDown || quietFor < TimeSpan.FromMilliseconds(1500)) return false;
        _pressed = _consumed = false;
        return true;
    }

    public SpaceKeyAction Handle(bool isDown, bool eligible)
    {
        if (!isDown)
        {
            var action = _consumed ? SpaceKeyAction.Consume : SpaceKeyAction.PassThrough;
            _pressed = _consumed = false;
            return action;
        }

        if (_pressed)
        {
            return _consumed ? SpaceKeyAction.Consume : SpaceKeyAction.PassThrough;
        }

        _pressed = true;
        _consumed = eligible;
        return eligible ? SpaceKeyAction.Toggle : SpaceKeyAction.PassThrough;
    }
}

/// <summary>Only inspects Space and modifier state. No Shell/COM or file I/O runs in the hook.</summary>
public sealed class ExplorerSpaceKeyHook : IDisposable
{
    private readonly Action<ExplorerContext> _onSpace;
    private readonly HookProc _callback;
    private readonly SpaceKeyGesture _gesture = new();
    private readonly Thread _thread;
    private readonly TimeSpan _renewalInterval;
    private readonly Action<string>? _diagnostic;
    private Dispatcher? _dispatcher;
    private nint _hook;
    private long _lastRegistration;
    private long _lastSpaceEvent;
    private int _registrationCount;
    private int _recoveryCount;
    private int _lastRegistrationError;
    private int _disposed;

    public ExplorerSpaceKeyHook(Action<ExplorerContext> onSpace, Action<string>? diagnostic = null)
        : this(onSpace, TimeSpan.FromSeconds(5), diagnostic)
    {
    }

    internal ExplorerSpaceKeyHook(Action<ExplorerContext> onSpace, TimeSpan renewalInterval, Action<string>? diagnostic = null)
    {
        _onSpace = onSpace;
        _renewalInterval = renewalInterval;
        _diagnostic = diagnostic;
        _callback = OnKeyboard;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            try
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                RenewRegistration();
                // Windows can silently remove WH_KEYBOARD_LL after a timeout; a nonzero
                // saved handle is not a health check. Renew on this same dedicated thread.
                var maintenance = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
                {
                    Interval = renewalInterval < TimeSpan.FromSeconds(1) ? renewalInterval : TimeSpan.FromSeconds(1),
                };
                maintenance.Tick += OnMaintenance;
                maintenance.Start();

                ready.TrySetResult();
                try { Dispatcher.Run(); }
                finally { maintenance.Stop(); }
            }
            catch (Exception ex)
            {
                ready.TrySetException(ex);
            }
            finally
            {
                if (_hook != 0)
                {
                    UnhookWindowsHookEx(_hook);
                    _hook = 0;
                }
            }
        }) { IsBackground = true, Name = "MoaView Explorer Space" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Task.GetAwaiter().GetResult();
    }

    private nint OnKeyboard(int code, nint message, nint data)
    {
        try
        {
            if (code >= 0 && Volatile.Read(ref _disposed) == 0 && Marshal.ReadInt32(data) == 0x20)
            {
                var isDown = message == 0x100 || message == 0x104;
                var isUp = message == 0x101 || message == 0x105;
                if (isDown || isUp)
                {
                    _lastSpaceEvent = Stopwatch.GetTimestamp();
                    // A held key's repeats must neither re-query Explorer nor toggle again.
                    var context = isDown && !_gesture.IsPressed && !HasModifier() ? ExplorerWindowContext.Capture() : null;
                    var action = _gesture.Handle(isDown, context is not null);
                    if (action == SpaceKeyAction.Toggle && context is { } source)
                    {
                        _onSpace(source); // The subscriber only posts to the UI dispatcher.
                    }

                    if (action != SpaceKeyAction.PassThrough)
                    {
                        return 1;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Never let a managed exception escape a native keyboard callback.
            Report($"hook-callback-error type={ex.GetType().Name}");
        }

        return CallNextHookEx(_hook, code, message, data);
    }

    internal nint RegistrationHandle => Interlocked.CompareExchange(ref _hook, 0, 0);
    internal int RegistrationCount => Volatile.Read(ref _registrationCount);
    internal int RecoveryCount => Volatile.Read(ref _recoveryCount);

    private void OnMaintenance(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (_gesture.RecoverReleasedKey(IsDown(0x20), Stopwatch.GetElapsedTime(_lastSpaceEvent)))
            Report("space-release-recovered");
        if (Stopwatch.GetElapsedTime(_lastRegistration) < _renewalInterval) return;
        try
        {
            RenewRegistration();
            _lastRegistrationError = 0;
        }
        catch (Win32Exception ex)
        {
            // Keep any old registration and retry on the next tick, including after unlock/resume.
            if (_lastRegistrationError != ex.NativeErrorCode)
                Report($"hook-renewal-retry win32={ex.NativeErrorCode}");
            _lastRegistrationError = ex.NativeErrorCode;
        }
    }

    private void RenewRegistration()
    {
        // Install first, so a failed renewal does not disable a healthy listener.
        var replacement = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
        if (replacement == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var previous = Interlocked.Exchange(ref _hook, replacement);
        var registration = Interlocked.Increment(ref _registrationCount);
        _lastRegistration = Stopwatch.GetTimestamp();
        if (previous != 0 && !UnhookWindowsHookEx(previous))
        {
            var error = Marshal.GetLastWin32Error();
            Interlocked.Increment(ref _recoveryCount);
            Report($"hook-restored registration={registration} previous-unhook-error={error}");
        }
        else if (registration <= 2)
        {
            // Record startup and the first successful unattended renewal, not every key/renewal.
            Report($"hook-{(previous == 0 ? "installed" : "renewed")} registration={registration}");
        }
    }

    private void Report(string message)
    {
        if (_diagnostic is not { } diagnostic) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { diagnostic(message); }
            catch { /* Diagnostics must not interrupt keyboard processing. */ }
        });
    }

    private static bool HasModifier() =>
        IsDown(0x10) || IsDown(0x11) || IsDown(0x12) || IsDown(0x5B) || IsDown(0x5C);

    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private delegate nint HookProc(int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hookId, HookProc callback, nint module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
