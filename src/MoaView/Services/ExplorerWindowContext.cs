using System.Runtime.InteropServices;
using System.Text;

namespace MoaView.Services;

public static class ExplorerWindowContext
{
    public static ExplorerContext? Capture()
    {
        var foreground = GetForegroundWindow();
        if (foreground == 0 || GetClass(foreground) is not ("CabinetWClass" or "ExploreWClass"))
        {
            return null;
        }

        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(0, ref info) || info.Focus == 0 || info.Caret != 0 || (info.Flags & 0x1E) != 0)
        {
            return null;
        }

        // Allow only a file list with no edit control, menu or rename caret.
        if (GetClass(info.Focus) is not ("DirectUIHWND" or "SysListView32" or "SHELLDLL_DefView"))
        {
            return null;
        }

        var view = info.Focus;
        for (var depth = 0; view != 0 && view != foreground && depth < 16; depth++, view = GetParent(view))
        {
            if (GetClass(view) == "SHELLDLL_DefView" && GetAncestor(view, 2) == foreground)
            {
                return new ExplorerContext(foreground, view);
            }
        }

        return null;
    }

    public static bool IsWindow(nint window) => window != 0 && IsWindowNative(window);

    private static string GetClass(nint window)
    {
        var buffer = new StringBuilder(256);
        return GetClassName(window, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowNative(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int maxCount);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);
    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);
}
