using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.CSharp.RuntimeBinder;

namespace MoaView.Services;

/// <summary>Reads Explorer's selected filesystem item on a separate STA, without using the clipboard.</summary>
public sealed class WindowsExplorerSelectionService : IExplorerSelectionService, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private bool _disposed;

    public WindowsExplorerSelectionService()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }) { IsBackground = true, Name = "MoaView Explorer selection" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        _dispatcher = ready.Task.GetAwaiter().GetResult();
    }

    public Task<string?> GetSelectedPathAsync(ExplorerContext context) =>
        _disposed ? Task.FromResult<string?>(null) : _dispatcher.InvokeAsync(() => ReadSelection(context)).Task;

    private static string? ReadSelection(ExplorerContext context)
    {
        object? shell = null;
        object? windows = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return null;
            }

            shell = Activator.CreateInstance(shellType);
            windows = ((dynamic)shell!).Windows();
            var count = (int)((dynamic)windows).Count;
            for (var index = 0; index < count; index++)
            {
                object? window = null;
                try
                {
                    window = ((dynamic)windows).Item(index);
                    if (window is not null && (nint)(long)((dynamic)window).HWND == context.WindowHandle &&
                        IsMatchingView(window, context.ViewHandle))
                    {
                        return ReadSelectedPath(window);
                    }
                }
                catch (Exception ex) when (IsShellUnavailable(ex))
                {
                    // An Explorer window/tab can disappear while its collection is enumerated.
                }
                finally
                {
                    Release(window);
                }
            }
        }
        catch (Exception ex) when (IsShellUnavailable(ex))
        {
            // Explorer restart, unavailable COM server or a non-filesystem shell location.
        }
        finally
        {
            Release(windows);
            Release(shell);
        }

        return null;
    }

    private static bool IsMatchingView(object window, nint expectedView)
    {
        object? browser = null;
        IShellView? view = null;
        try
        {
            var service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"); // SID_STopLevelBrowser
            var iid = typeof(IShellBrowser).GUID;
            var provider = (IComServiceProvider)window;
            if (provider.QueryService(ref service, ref iid, out browser) < 0 || browser is null)
            {
                return false;
            }

            ((IShellBrowser)browser).QueryActiveShellView(out view);
            if (view is null)
            {
                return false;
            }
            // Explorer's out-of-process view exposes these inherited methods on IShellView.
            // Its COM proxy need not accept a separate QueryInterface for IOleWindow.
            view.GetWindow(out var viewHandle);
            // HWND alone is shared by tabs. Match the focused Shell view as well.
            return viewHandle == expectedView;
        }
        finally
        {
            Release(view);
            Release(browser);
        }
    }

    private static string? ReadSelectedPath(object window)
    {
        object? document = null;
        object? selected = null;
        object? item = null;
        try
        {
            document = ((dynamic)window).Document;
            selected = ((dynamic)document!).SelectedItems();
            // Multiple selection is deliberately left to Explorer; do not choose an arbitrary file.
            if ((int)((dynamic)selected!).Count != 1)
            {
                return null;
            }

            item = ((dynamic)selected).Item(0);
            if (!(bool)((dynamic)item!).IsFileSystem)
            {
                return null;
            }

            var path = (string)((dynamic)item).Path;
            return Path.IsPathFullyQualified(path) ? path : null;
        }
        finally
        {
            Release(item);
            Release(selected);
            Release(document);
        }
    }

    private static bool IsShellUnavailable(Exception ex) => ex is
        COMException or InvalidComObjectException or InvalidCastException or
        RuntimeBinderException or UnauthorizedAccessException or ArgumentException;

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            // Never wait on a hung Explorer COM call during application shutdown.
            _dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        }
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IComServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow(out nint window);
        void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enterMode);
    }

    // The IOleWindow slots and all preceding IShellBrowser slots must retain their native order.
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow(out nint window);
        void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enterMode);
        void InsertMenusSB(nint menu, nint widths);
        void SetMenuSB(nint menu, nint oleMenu, nint activeObject);
        void RemoveMenusSB(nint menu);
        void SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string text);
        void EnableModelessSB([MarshalAs(UnmanagedType.Bool)] bool enable);
        void TranslateAcceleratorSB(nint message, ushort id);
        void BrowseObject(nint pidl, uint flags);
        void GetViewStateStream(uint mode, out nint stream);
        void GetControlWindow(uint id, out nint window);
        void SendControlMsg(uint id, uint message, nint wParam, nint lParam, out nint result);
        void QueryActiveShellView(out IShellView? view);
    }
}
