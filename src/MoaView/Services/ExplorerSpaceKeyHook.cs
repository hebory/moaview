using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace MoaView.Services;

public enum SpaceKeyAction { PassThrough, Consume, Toggle }

/// <summary>A held Space toggles at most once, including when focus changes during the press.</summary>
public sealed class SpaceKeyGesture
{
    private bool _pressed;
    private bool _consumed;

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
    private Dispatcher? _dispatcher;
    private nint _hook;
    private int _disposed;

    public ExplorerSpaceKeyHook(Action<ExplorerContext> onSpace)
    {
        _onSpace = onSpace;
        _callback = OnKeyboard;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            try
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                _hook = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
                if (_hook == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                ready.TrySetResult();
                Dispatcher.Run();
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
        if (code >= 0 && Volatile.Read(ref _disposed) == 0 && Marshal.ReadInt32(data) == 0x20)
        {
            var isDown = message == 0x100 || message == 0x104;
            var isUp = message == 0x101 || message == 0x105;
            if (isDown || isUp)
            {
                var context = isDown && !HasModifier() ? ExplorerWindowContext.Capture() : null;
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

        return CallNextHookEx(_hook, code, message, data);
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
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
