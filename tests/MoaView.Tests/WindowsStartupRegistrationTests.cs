using System.IO;
using Microsoft.Win32;
using MoaView.Services;
using Xunit;

namespace MoaView.Tests;

public sealed class WindowsStartupRegistrationTests
{
    [Fact]
    public void TrySetEnabled_UsesOnlyMoaViewValueAndCanRemoveIt()
    {
        var testRoot = $@"Software\MoaView.Tests\{Guid.NewGuid():N}";
        var subKey = $@"{testRoot}\Run";
        var executablePath = Path.Combine(Path.GetTempPath(), "MoaView Test.exe");
        var registration = new WindowsStartupRegistration(executablePath, subKey);

        using (var key = Registry.CurrentUser.CreateSubKey(subKey))
        {
            key.SetValue("OtherTool", "\"C:\\Tools\\Other.exe\"", RegistryValueKind.String);
        }

        try
        {
            Assert.True(registration.TryGetEnabled(out var initiallyEnabled, out var readError), readError);
            Assert.False(initiallyEnabled);

            Assert.True(registration.TrySetEnabled(true, out var enableError), enableError);
            Assert.True(registration.IsEnabled);

            using (var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false))
            {
                Assert.Equal($"\"{Path.GetFullPath(executablePath)}\" --background", key?.GetValue("MoaView"));
                Assert.Equal(new[] { "MoaView", "OtherTool" }, key?.GetValueNames()?.OrderBy(name => name).ToArray());
                Assert.Equal("\"C:\\Tools\\Other.exe\"", key?.GetValue("OtherTool"));
            }

            Assert.True(registration.TrySetEnabled(false, out var disableError), disableError);
            Assert.False(registration.IsEnabled);
            using (var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false))
            {
                Assert.Equal(new[] { "OtherTool" }, key?.GetValueNames());
                Assert.Equal("\"C:\\Tools\\Other.exe\"", key?.GetValue("OtherTool"));
            }
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(testRoot, throwOnMissingSubKey: false);
        }
    }
}
