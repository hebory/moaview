param(
    [Parameter(Mandatory)][string]$ApplicationDirectory,
    [Parameter(Mandatory)][string]$SampleFile
)
$ErrorActionPreference = 'Stop'
$application = (Resolve-Path -LiteralPath (Join-Path $ApplicationDirectory 'MoaView.exe')).Path
$dll = (Resolve-Path -LiteralPath (Join-Path $ApplicationDirectory 'MoaView.ExplorerCommand.dll')).Path
$sample = (Resolve-Path -LiteralPath $SampleFile).Path

Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class ExplorerCommandSmoke
{
    [ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IClassFactory
    {
        void CreateInstance(IntPtr outer, ref Guid iid, out IExplorerCommand result);
        void LockServer([MarshalAs(UnmanagedType.Bool)] bool locked);
    }

    [ComImport, Guid("A08CE4D0-FA25-44AB-B57C-C7B1C323E0B9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IExplorerCommand
    {
        void GetTitle(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] out string title);
        void GetIcon(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] out string icon);
        void GetToolTip(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] out string tip);
        void GetCanonicalName(out Guid name);
        void GetState(IntPtr items, [MarshalAs(UnmanagedType.Bool)] bool slow, out uint state);
        void Invoke(IntPtr items, IntPtr bindContext);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetClassObject(ref Guid clsid, ref Guid iid, out IntPtr factory);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", ExactSpelling=true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);
    [DllImport("shell32.dll", CharSet=CharSet.Unicode, ExactSpelling=true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, out IntPtr item);
    [DllImport("shell32.dll", ExactSpelling=true)]
    private static extern int SHCreateShellItemArrayFromShellItem(IntPtr item, ref Guid iid, out IntPtr items);

    public static void Invoke(string dll, string app, string sample)
    {
        var module = LoadLibraryEx(dll, IntPtr.Zero, 0x900); // DLL directory and System32.
        if (module == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr factoryPointer = IntPtr.Zero, item = IntPtr.Zero, items = IntPtr.Zero;
        IClassFactory factory = null;
        IExplorerCommand command = null;
        try
        {
            var export = GetProcAddress(module, "DllGetClassObject");
            var getFactory = (GetClassObject)Marshal.GetDelegateForFunctionPointer(export, typeof(GetClassObject));
            var clsid = new Guid("becbf110-b45c-4c72-a5ca-67e0bafbd299");
            var factoryId = typeof(IClassFactory).GUID;
            Marshal.ThrowExceptionForHR(getFactory(ref clsid, ref factoryId, out factoryPointer));
            factory = (IClassFactory)Marshal.GetObjectForIUnknown(factoryPointer);
            var commandId = typeof(IExplorerCommand).GUID;
            factory.CreateInstance(IntPtr.Zero, ref commandId, out command);

            var itemId = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(sample, IntPtr.Zero, ref itemId, out item));
            var arrayId = new Guid("b63ea76d-1f85-456f-a19c-48159efa858b");
            Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromShellItem(item, ref arrayId, out items));

            string title, icon;
            uint state;
            command.GetTitle(items, out title);
            command.GetIcon(items, out icon);
            command.GetState(items, false, out state);
            if (String.IsNullOrEmpty(title) || icon != app + ",0" || state != 0)
                throw new InvalidOperationException("Explorer command metadata is invalid.");
            command.GetState(IntPtr.Zero, false, out state);
            if (state != 2) throw new InvalidOperationException("The command must be hidden without a file selection.");
            command.Invoke(items, IntPtr.Zero);
        }
        finally
        {
            if (command != null) Marshal.ReleaseComObject(command);
            if (factory != null) Marshal.ReleaseComObject(factory);
            if (factoryPointer != IntPtr.Zero) Marshal.Release(factoryPointer);
            if (items != IntPtr.Zero) Marshal.Release(items);
            if (item != IntPtr.Zero) Marshal.Release(item);
            FreeLibrary(module);
        }
    }
}
'@

$titlePrefix = [IO.Path]::GetFileName($sample) + ' - '
$existing = @(Get-Process -Name MoaView -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
[ExplorerCommandSmoke]::Invoke($dll, $application, $sample)
$process = $null
$deadline = [DateTime]::UtcNow.AddSeconds(12)
try {
    do {
        $process = Get-Process -Name MoaView -ErrorAction SilentlyContinue | Where-Object {
            $_.Id -notin $existing -and $_.Path -eq $application
        } | Select-Object -First 1
        if ($process -and $process.MainWindowTitle.StartsWith($titlePrefix)) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$process -or !$process.MainWindowTitle.StartsWith($titlePrefix)) { throw 'Native command did not open the sample file.' }
    if (!$process.CloseMainWindow() -or !$process.WaitForExit(5000)) { throw 'Native preview did not close.' }
    Write-Output 'Native Explorer command opened the selected file.'
} finally {
    if ($process) {
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}
