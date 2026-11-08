using System.IO;
using System.Security;
using Microsoft.Win32;

namespace MoaView.Services;

/// <summary>Controls the current user's optional MoaView login startup entry.</summary>
public sealed class WindowsStartupRegistration
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "MoaView";
    public const string BackgroundSwitch = "--background";

    private readonly string _executablePath;
    private readonly string _registrySubKey;

    public WindowsStartupRegistration(string? executablePath = null, string registrySubKey = RunKeyPath)
    {
        var path = executablePath ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("실행 파일 경로를 확인할 수 없습니다.");
        _executablePath = Path.GetFullPath(path);
        _registrySubKey = registrySubKey;
    }

    public bool IsEnabled => TryGetEnabled(out var enabled, out _) && enabled;

    public bool TryGetEnabled(out bool enabled, out string? error)
    {
        enabled = false;
        error = null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registrySubKey, writable: false);
            if (key?.GetValue(ValueName) is string command)
            {
                enabled = string.Equals(command, Command, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            error = $"로그인 시 자동 실행 상태를 읽지 못했습니다. {ex.Message}";
            return false;
        }
    }

    public bool TrySetEnabled(bool enabled, out string? error)
    {
        error = null;

        try
        {
            if (enabled)
            {
                using var key = Registry.CurrentUser.CreateSubKey(_registrySubKey, writable: true);
                if (key is null)
                {
                    error = "로그인 시 자동 실행을 등록할 레지스트리 위치를 열지 못했습니다.";
                    return false;
                }

                key.SetValue(ValueName, Command, RegistryValueKind.String);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(_registrySubKey, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            error = enabled
                ? $"로그인 시 자동 실행을 등록하지 못했습니다. {ex.Message}"
                : $"로그인 시 자동 실행을 해제하지 못했습니다. {ex.Message}";
            return false;
        }
    }

    private string Command => $"\"{_executablePath}\" {BackgroundSwitch}";

    private static bool IsRegistryFailure(Exception ex) =>
        ex is UnauthorizedAccessException or IOException or SecurityException or ArgumentException;
}
