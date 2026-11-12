using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Win32;
using MoaView.Models;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
using MoaView.ViewModels;
using Xunit;

namespace MoaView.Tests;

public sealed class DeploymentAndGuideVerificationTests
{
    [Fact]
    public void InstallerScriptsAndGuides_StayConsistentWithProjectVersionAndRegistrationContracts()
    {
        var repoRoot = FindRepositoryRoot();
        var csprojPath = Path.Combine(repoRoot, "src", "MoaView", "MoaView.csproj");
        var csproj = XDocument.Load(csprojPath);
        var version = csproj.Root?
            .Elements("PropertyGroup")
            .Elements("Version")
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => !string.IsNullOrEmpty(value));

        Assert.False(string.IsNullOrWhiteSpace(version));

        var appDirectory = FindBuiltApplicationDirectory(repoRoot);
        var exePath = Path.Combine(appDirectory, "MoaView.exe");
        Assert.True(File.Exists(exePath), $"Built MoaView.exe not found at {exePath}");

        var productVersion = FileVersionInfo.GetVersionInfo(exePath).ProductVersion?.Split('+')[0];
        Assert.Equal(version, productVersion);

        var issText = File.ReadAllText(Path.Combine(repoRoot, "installer", "MoaView.iss"));
        Assert.Contains($"#define AppVersion \"{version}\"", issText, StringComparison.Ordinal);
        Assert.Contains("AppId={{23C80132-6E95-43B2-AD9B-A283239A5947}", issText, StringComparison.Ordinal);
        Assert.Contains("DefaultDirName={localappdata}\\Programs\\MoaView", issText, StringComparison.Ordinal);
        Assert.Contains("PrivilegesRequired=lowest", issText, StringComparison.Ordinal);
        Assert.Contains("MinVersion=10.0.17763", issText, StringComparison.Ordinal);
        Assert.Contains("Subkey: \"Software\\Classes\\*\\shell\\MoaView.Preview\"", issText, StringComparison.Ordinal);
        Assert.Contains("ValueData: \"\"\"{app}\\MoaView.exe\"\" --preview \"\"%1\"\"\"", issText, StringComparison.Ordinal);
        Assert.Contains("Subkey: \"Software\\Microsoft\\Windows\\CurrentVersion\\Run\"; ValueType: string; ValueName: \"MoaView\"; ValueData: \"\"\"{app}\\MoaView.exe\"\" --background\"; Flags: uninsdeletevalue", issText, StringComparison.Ordinal);
        Assert.Contains("{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}", issText, StringComparison.Ordinal);

        var buildInstallerText = File.ReadAllText(Path.Combine(repoRoot, "scripts", "Build-Installer.ps1"));
        Assert.Contains($"[string]$Version = '{version}'", buildInstallerText, StringComparison.Ordinal);
        Assert.Contains("[string]$RuntimeVersion = '8.0.33'", buildInstallerText, StringComparison.Ordinal);
        Assert.Contains("docs/content-preview.md", buildInstallerText, StringComparison.Ordinal);
        Assert.Contains("docs/third-party-notices.md", buildInstallerText, StringComparison.Ordinal);
        Assert.Contains("docs/install.md", buildInstallerText, StringComparison.Ordinal);

        var testInstallerText = File.ReadAllText(Path.Combine(repoRoot, "scripts", "Test-Installer.ps1"));
        Assert.Contains($"MoaView-{version}-Setup-x64.exe", testInstallerText, StringComparison.Ordinal);
        Assert.Contains("{23C80132-6E95-43B2-AD9B-A283239A5947}_is1", testInstallerText, StringComparison.Ordinal);
        Assert.Contains("HKCU:\\Software\\Classes\\*\\shell\\MoaView.Preview", testInstallerText, StringComparison.Ordinal);
        Assert.Contains("HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run", testInstallerText, StringComparison.Ordinal);
        Assert.Contains("Get-Process -Name 'MoaView'", testInstallerText, StringComparison.Ordinal);
        Assert.Contains("Test-WebView2Installed", testInstallerText, StringComparison.Ordinal);
        Assert.Contains("content-preview.md", testInstallerText, StringComparison.Ordinal);
        Assert.Contains("third-party-notices.md", testInstallerText, StringComparison.Ordinal);
        Assert.Contains("install.md", testInstallerText, StringComparison.Ordinal);
        Assert.Contains("app-theme.json", testInstallerText, StringComparison.Ordinal);
        Assert.Contains("markdown-theme.json", testInstallerText, StringComparison.Ordinal);
        Assert.Contains("User settings preserved across install and uninstall", testInstallerText, StringComparison.Ordinal);

        var testPortableText = File.ReadAllText(Path.Combine(repoRoot, "scripts", "Test-Portable.ps1"));
        Assert.Contains("[string]$RuntimeVersion = '8.0.33'", testPortableText, StringComparison.Ordinal);
        Assert.Contains("DOTNET_MULTILEVEL_LOOKUP", testPortableText, StringComparison.Ordinal);
        Assert.Contains("DOTNET_ROLL_FORWARD", testPortableText, StringComparison.Ordinal);

        var installDocText = File.ReadAllText(Path.Combine(repoRoot, "docs", "install.md"));
        Assert.Contains($"MoaView-{version}-Setup-x64.exe", installDocText, StringComparison.Ordinal);
        Assert.Contains("%LOCALAPPDATA%\\Programs\\MoaView", installDocText, StringComparison.Ordinal);
        Assert.Contains("%LOCALAPPDATA%\\MoaView", installDocText, StringComparison.Ordinal);
        Assert.Contains("MoaView 종료", installDocText, StringComparison.Ordinal);
        Assert.Contains("scripts/Test-Portable.ps1", installDocText, StringComparison.Ordinal);
        Assert.Contains("scripts/Test-Installer.ps1", installDocText, StringComparison.Ordinal);
        Assert.Contains("QUIC-8-clean-environment-verification.md", installDocText, StringComparison.Ordinal);
        Assert.Contains("QUIC-9-isolated-environment-verification.md", installDocText, StringComparison.Ordinal);

        var quic9DocPath = Path.Combine(repoRoot, "docs", "QUIC-9-isolated-environment-verification.md");
        Assert.True(File.Exists(quic9DocPath));
        var quic9DocText = File.ReadAllText(quic9DocPath);
        Assert.Contains("Test-Installer.ps1", quic9DocText, StringComparison.Ordinal);
        Assert.Contains("app-theme.json", quic9DocText, StringComparison.Ordinal);
        Assert.Contains("markdown-theme.json", quic9DocText, StringComparison.Ordinal);

        var contentDocText = File.ReadAllText(Path.Combine(repoRoot, "docs", "content-preview.md"));
        var readmeText = File.ReadAllText(Path.Combine(repoRoot, "README.md"));
        Assert.Contains($"MoaView-{version}-Setup-x64.exe", readmeText, StringComparison.Ordinal);
        Assert.Contains("docs/QUIC-8-clean-environment-verification.md", readmeText, StringComparison.Ordinal);
        Assert.Contains("docs/QUIC-9-isolated-environment-verification.md", readmeText, StringComparison.Ordinal);

        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff" })
        {
            Assert.Contains(ext, SupportedFileFormats.SupportedImageExtensions, StringComparer.OrdinalIgnoreCase);
        }

        Assert.Equal(
            new[] { ImageFormatKind.Png, ImageFormatKind.Jpeg, ImageFormatKind.Bmp },
            SupportedFileFormats.ConvertibleImageFormats);

        foreach (var formatToken in new[] { "JPEG", "PNG", "BMP", "GIF", "TIFF", "ZIP" })
        {
            Assert.Contains(formatToken, installDocText, StringComparison.Ordinal);
            Assert.Contains(formatToken, contentDocText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void IsolatedApplicationCopy_VerifiesStartupAndShellPreviewWithoutTouchingUserSettings()
    {
        var repoRoot = FindRepositoryRoot();
        var sourceAppDir = FindBuiltApplicationDirectory(repoRoot);
        using var temp = new TempTestDirectory();

        var isolatedAppDir = temp.GetPath("isolated-app");
        var isolatedProfile = temp.GetPath("isolated-profile");
        var isolatedTemp = temp.GetPath("isolated-temp");
        var samplesDir = temp.GetPath("samples");

        Directory.CreateDirectory(isolatedAppDir);
        Directory.CreateDirectory(Path.Combine(isolatedProfile, "Local"));
        Directory.CreateDirectory(Path.Combine(isolatedProfile, "Roaming"));
        Directory.CreateDirectory(isolatedTemp);
        Directory.CreateDirectory(samplesDir);

        CopyDirectoryRecursive(sourceAppDir, isolatedAppDir);

        var copiedExe = Path.Combine(isolatedAppDir, "MoaView.exe");
        Assert.True(File.Exists(copiedExe));
        Assert.True(File.Exists(Path.Combine(isolatedAppDir, "MoaView.dll")));
        Assert.True(File.Exists(Path.Combine(isolatedAppDir, "MoaView.runtimeconfig.json")));

        var hostLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var hostAppThemePath = Path.Combine(hostLocalAppData, "MoaView", "app-theme.json");
        var hostMarkdownThemePath = Path.Combine(hostLocalAppData, "MoaView", "markdown-theme.json");
        var appThemeBefore = ComputeFileHashOrNull(hostAppThemePath);
        var markdownThemeBefore = ComputeFileHashOrNull(hostMarkdownThemePath);

        var jpgSample = SampleImageFactory.WriteValidImage(Path.Combine(samplesDir, "sample.jpg"), ImageFormatKind.Jpeg, 24, 16);
        var pngSample = SampleImageFactory.WriteValidImage(Path.Combine(samplesDir, "sample.png"), ImageFormatKind.Png, 24, 16);
        var bmpSample = SampleImageFactory.WriteValidImage(Path.Combine(samplesDir, "sample.bmp"), ImageFormatKind.Bmp, 24, 16);
        var gifSample = SampleImageFactory.WriteValidImage(Path.Combine(samplesDir, "sample.gif"), ImageFormatKind.Gif, 24, 16);
        var tiffSample = SampleImageFactory.WriteValidImage(Path.Combine(samplesDir, "sample.tiff"), ImageFormatKind.Tiff, 24, 16);

        var mdSample = Path.Combine(samplesDir, "guide.md");
        File.WriteAllText(mdSample, "# MoaView C9\r\n\r\nIsolated portable verification.", Encoding.UTF8);

        var zipSample = ContentPreviewTests.WriteZip(
            Path.Combine(samplesDir, "archive.zip"),
            new Dictionary<string, byte[]>
            {
                ["readme.txt"] = Encoding.UTF8.GetBytes("Archive text preview"),
                ["sample.png"] = File.ReadAllBytes(pngSample),
            });

        var damagedJpg = Path.Combine(samplesDir, "damaged.jpg");
        File.WriteAllBytes(damagedJpg, [0xFF, 0xD8, 0x00, 0x22]);

        var unsupportedBin = Path.Combine(samplesDir, "unknown.bin");
        File.WriteAllBytes(unsupportedBin, [0x00, 0x01, 0xFF]);

        AssertIsolatedProcess(copiedExe, isolatedAppDir, isolatedProfile, isolatedTemp, "--verify-startup", "Empty");
        AssertIsolatedProcess(copiedExe, isolatedAppDir, isolatedProfile, isolatedTemp, $"--verify-startup \"{jpgSample}\"", "ImageLoaded");
        AssertIsolatedProcess(copiedExe, isolatedAppDir, isolatedProfile, isolatedTemp, $"--verify-startup \"{pngSample}\"", "ImageLoaded");
        AssertIsolatedProcess(copiedExe, isolatedAppDir, isolatedProfile, isolatedTemp, $"--verify-startup \"{bmpSample}\"", "ImageLoaded");
        AssertIsolatedProcess(copiedExe, isolatedAppDir, isolatedProfile, isolatedTemp, $"--verify-startup \"{gifSample}\"", "ImageLoaded");
        AssertIsolatedProcess(copiedExe, isolatedAppDir, isolatedProfile, isolatedTemp, $"--verify-startup \"{tiffSample}\"", "ImageLoaded");
        AssertIsolatedProcess(copiedExe, isolatedAppDir, isolatedProfile, isolatedTemp, $"--verify-startup \"{zipSample}\"", "ArchiveLoaded");
        AssertIsolatedProcess(copiedExe, isolatedAppDir, isolatedProfile, isolatedTemp, $"--verify-startup \"{mdSample}\"", "TextLoaded");
        AssertIsolatedProcess(copiedExe, isolatedAppDir, isolatedProfile, isolatedTemp, $"--verify-startup \"{damagedJpg}\"", "Error", expectedBadge: "열기 실패");
        AssertIsolatedProcess(copiedExe, isolatedAppDir, isolatedProfile, isolatedTemp, $"--verify-startup \"{unsupportedBin}\"", "TextLoaded");
        AssertIsolatedProcess(copiedExe, isolatedAppDir, isolatedProfile, isolatedTemp, $"--verify-startup --preview \"{pngSample}\"", "ImageLoaded");

        Assert.Equal(appThemeBefore, ComputeFileHashOrNull(hostAppThemePath));
        Assert.Equal(markdownThemeBefore, ComputeFileHashOrNull(hostMarkdownThemePath));
    }

    [Fact]
    public void IsolatedInstallRunSpacePreviewAndUninstall_PreservesUserSettingsAndCleansUpRegistrations()
    {
        var repoRoot = FindRepositoryRoot();
        var sourceAppDir = FindBuiltApplicationDirectory(repoRoot);
        using var temp = new TempTestDirectory();

        var isolatedProfile = temp.GetPath("isolated-user");
        var isolatedLocalAppData = Path.Combine(isolatedProfile, "Local");
        var isolatedRoamingAppData = Path.Combine(isolatedProfile, "Roaming");
        var isolatedTemp = temp.GetPath("isolated-temp");
        var isolatedInstallDir = Path.Combine(isolatedLocalAppData, "Programs", "MoaView");
        var isolatedUserDataDir = Path.Combine(isolatedLocalAppData, "MoaView");
        var isolatedAppThemePath = Path.Combine(isolatedUserDataDir, "app-theme.json");
        var isolatedMarkdownThemePath = Path.Combine(isolatedUserDataDir, "markdown-theme.json");
        var samplesDir = temp.GetPath("samples");

        Directory.CreateDirectory(isolatedLocalAppData);
        Directory.CreateDirectory(isolatedRoamingAppData);
        Directory.CreateDirectory(isolatedTemp);
        Directory.CreateDirectory(samplesDir);

        var testRegistryRoot = $@"Software\MoaView.Tests\IsolatedInstall_{Guid.NewGuid():N}";
        var uninstallSubKey = $@"{testRegistryRoot}\Uninstall\{{23C80132-6E95-43B2-AD9B-A283239A5947}}_is1";
        var menuSubKey = $@"{testRegistryRoot}\Classes\*\shell\MoaView.Preview";
        var menuCommandSubKey = $@"{menuSubKey}\command";
        var runSubKey = $@"{testRegistryRoot}\Run";

        var hostLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var hostAppThemePath = Path.Combine(hostLocalAppData, "MoaView", "app-theme.json");
        var hostMarkdownThemePath = Path.Combine(hostLocalAppData, "MoaView", "markdown-theme.json");
        var hostAppThemeBefore = ComputeFileHashOrNull(hostAppThemePath);
        var hostMarkdownThemeBefore = ComputeFileHashOrNull(hostMarkdownThemePath);

        try
        {
            // 1. Pre-install clean state check in isolated environment
            Assert.False(Directory.Exists(isolatedInstallDir));
            Assert.False(Directory.Exists(isolatedUserDataDir));
            Assert.Null(Registry.CurrentUser.OpenSubKey(uninstallSubKey, writable: false));
            Assert.Null(Registry.CurrentUser.OpenSubKey(menuSubKey, writable: false));
            Assert.Null(Registry.CurrentUser.OpenSubKey(runSubKey, writable: false));

            // 2. Isolated installation: copy payload + guide docs and register Uninstall, Explorer menu, and Run keys
            CopyDirectoryRecursive(sourceAppDir, isolatedInstallDir);
            foreach (var docFile in new[] { "install.md", "content-preview.md", "third-party-notices.md" })
            {
                File.Copy(Path.Combine(repoRoot, "docs", docFile), Path.Combine(isolatedInstallDir, docFile), overwrite: true);
            }

            var installedExe = Path.Combine(isolatedInstallDir, "MoaView.exe");
            foreach (var requiredRelativePath in new[]
            {
                "MoaView.exe",
                "MoaView.dll",
                "MoaView.runtimeconfig.json",
                "install.md",
                "content-preview.md",
                "third-party-notices.md",
                "Assets/Rive/vendor/rive.wasm",
                "Assets/ThreeD/index.html",
                "Assets/Bpg/bpgdec.js",
                "Assets/Licenses/Markdig.txt",
                "Assets/Licenses/SharpCompress.txt",
                "Assets/Licenses/Magick.NET-Notices.txt",
            })
            {
                Assert.True(
                    File.Exists(Path.Combine(isolatedInstallDir, requiredRelativePath)),
                    $"Missing installed payload file: {requiredRelativePath}");
            }

            using (var uninstallKey = Registry.CurrentUser.CreateSubKey(uninstallSubKey, writable: true))
            {
                Assert.NotNull(uninstallKey);
                uninstallKey!.SetValue("DisplayName", "MoaView", RegistryValueKind.String);
                uninstallKey.SetValue("InstallLocation", isolatedInstallDir, RegistryValueKind.String);
            }

            using (var menuKey = Registry.CurrentUser.CreateSubKey(menuSubKey, writable: true))
            {
                Assert.NotNull(menuKey);
                menuKey!.SetValue("MUIVerb", "MoaView로 미리보기", RegistryValueKind.String);
                menuKey.SetValue("Icon", $"\"{installedExe}\",0", RegistryValueKind.String);
                menuKey.SetValue("MultiSelectModel", "Single", RegistryValueKind.String);
            }

            using (var commandKey = Registry.CurrentUser.CreateSubKey(menuCommandSubKey, writable: true))
            {
                Assert.NotNull(commandKey);
                commandKey!.SetValue(string.Empty, $"\"{installedExe}\" --preview \"%1\"", RegistryValueKind.String);
            }

            using (var runKey = Registry.CurrentUser.CreateSubKey(runSubKey, writable: true))
            {
                Assert.NotNull(runKey);
                runKey!.SetValue("ExistingUserApp", "\"C:\\Tools\\ExistingUserApp.exe\"", RegistryValueKind.String);
            }

            var startupRegistration = new WindowsStartupRegistration(installedExe, runSubKey);
            Assert.False(startupRegistration.IsEnabled);
            Assert.True(startupRegistration.TrySetEnabled(true, out var enableError), enableError);
            Assert.True(startupRegistration.IsEnabled);

            // 3. Save user settings in isolated LOCALAPPDATA and verify installed MoaView.exe startup loads them
            var appThemeSettings = new FileAppThemeSettings(isolatedAppThemePath);
            var markdownThemeSettings = new MarkdownThemeSettings(isolatedMarkdownThemePath);
            Assert.True(appThemeSettings.TrySave(AppTheme.Light));
            Assert.True(markdownThemeSettings.TrySave(MarkdownTheme.Sepia));

            var expectedIsolatedAppThemeHash = ComputeFileHashOrNull(isolatedAppThemePath);
            var expectedIsolatedMarkdownThemeHash = ComputeFileHashOrNull(isolatedMarkdownThemePath);
            Assert.False(string.IsNullOrEmpty(expectedIsolatedAppThemeHash));
            Assert.False(string.IsNullOrEmpty(expectedIsolatedMarkdownThemeHash));

            var jpgSample = SampleImageFactory.WriteValidImage(Path.Combine(samplesDir, "sample.jpg"), ImageFormatKind.Jpeg, 24, 16);
            var pngSample = SampleImageFactory.WriteValidImage(Path.Combine(samplesDir, "sample.png"), ImageFormatKind.Png, 24, 16);
            var bmpSample = SampleImageFactory.WriteValidImage(Path.Combine(samplesDir, "sample.bmp"), ImageFormatKind.Bmp, 24, 16);
            var gifSample = SampleImageFactory.WriteValidImage(Path.Combine(samplesDir, "sample.gif"), ImageFormatKind.Gif, 24, 16);
            var tiffSample = SampleImageFactory.WriteValidImage(Path.Combine(samplesDir, "sample.tiff"), ImageFormatKind.Tiff, 24, 16);

            var mdSample = Path.Combine(samplesDir, "guide.md");
            File.WriteAllText(mdSample, "# MoaView QUIC-9\r\n\r\nIsolated install and Space preview verification.", Encoding.UTF8);

            var zipSample = ContentPreviewTests.WriteZip(
                Path.Combine(samplesDir, "archive.zip"),
                new Dictionary<string, byte[]>
                {
                    ["readme.txt"] = Encoding.UTF8.GetBytes("Archive text preview"),
                    ["sample.png"] = File.ReadAllBytes(pngSample),
                });

            var damagedJpg = Path.Combine(samplesDir, "damaged.jpg");
            File.WriteAllBytes(damagedJpg, [0xFF, 0xD8, 0x00, 0x22]);

            var unsupportedBin = Path.Combine(samplesDir, "unknown.bin");
            File.WriteAllBytes(unsupportedBin, [0x00, 0x01, 0xFF]);

            AssertIsolatedProcess(installedExe, isolatedInstallDir, isolatedProfile, isolatedTemp, "--verify-startup", "Empty", expectedAppTheme: "Light", expectedMarkdownTheme: "Sepia");
            AssertIsolatedProcess(installedExe, isolatedInstallDir, isolatedProfile, isolatedTemp, $"--verify-startup \"{jpgSample}\"", "ImageLoaded", expectedAppTheme: "Light", expectedMarkdownTheme: "Sepia");
            AssertIsolatedProcess(installedExe, isolatedInstallDir, isolatedProfile, isolatedTemp, $"--verify-startup \"{pngSample}\"", "ImageLoaded", expectedAppTheme: "Light", expectedMarkdownTheme: "Sepia");
            AssertIsolatedProcess(installedExe, isolatedInstallDir, isolatedProfile, isolatedTemp, $"--verify-startup \"{bmpSample}\"", "ImageLoaded", expectedAppTheme: "Light", expectedMarkdownTheme: "Sepia");
            AssertIsolatedProcess(installedExe, isolatedInstallDir, isolatedProfile, isolatedTemp, $"--verify-startup \"{gifSample}\"", "ImageLoaded", expectedAppTheme: "Light", expectedMarkdownTheme: "Sepia");
            AssertIsolatedProcess(installedExe, isolatedInstallDir, isolatedProfile, isolatedTemp, $"--verify-startup \"{tiffSample}\"", "ImageLoaded", expectedAppTheme: "Light", expectedMarkdownTheme: "Sepia");
            AssertIsolatedProcess(installedExe, isolatedInstallDir, isolatedProfile, isolatedTemp, $"--verify-startup \"{zipSample}\"", "ArchiveLoaded", expectedAppTheme: "Light", expectedMarkdownTheme: "Sepia");
            AssertIsolatedProcess(installedExe, isolatedInstallDir, isolatedProfile, isolatedTemp, $"--verify-startup \"{mdSample}\"", "TextLoaded", expectedAppTheme: "Light", expectedMarkdownTheme: "Sepia");
            AssertIsolatedProcess(installedExe, isolatedInstallDir, isolatedProfile, isolatedTemp, $"--verify-startup \"{damagedJpg}\"", "Error", expectedBadge: "열기 실패", expectedAppTheme: "Light", expectedMarkdownTheme: "Sepia");
            AssertIsolatedProcess(installedExe, isolatedInstallDir, isolatedProfile, isolatedTemp, $"--verify-startup \"{unsupportedBin}\"", "TextLoaded", expectedAppTheme: "Light", expectedMarkdownTheme: "Sepia");
            AssertIsolatedProcess(installedExe, isolatedInstallDir, isolatedProfile, isolatedTemp, $"--verify-startup --preview \"{pngSample}\"", "ImageLoaded", expectedAppTheme: "Light", expectedMarkdownTheme: "Sepia");

            // 4. Verify Explorer Space preview, selection switching, image manipulation/conversion, and ZIP internal preview
            var convertedOutputPath = Path.Combine(samplesDir, "converted-from-space.png");
            StaTestRunner.Run(() =>
            {
                var isolatedAppearance = new AppAppearance(new FileAppThemeSettings(isolatedAppThemePath));
                var presenter = new ExplorerPreviewPresenter(
                    () =>
                    {
                        var dialogs = new Win32FileDialogService();
                        return new MainWindowViewModel(
                            new FilePreviewService(),
                            dialogs,
                            dialogs,
                            markdownThemeSettings: new MarkdownThemeSettings(isolatedMarkdownThemePath));
                    },
                    isolatedAppearance);

                var explorerOwner = new MainWindow { ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    explorerOwner.Show();
                    var explorerContext = new ExplorerContext(new WindowInteropHelper(explorerOwner).Handle, 1);
                    var selection = new IsolatedExplorerSelectionStub { SelectedPath = jpgSample };
                    using var coordinator = new ExplorerPreviewCoordinator(selection, presenter, () => explorerContext);

                    // Space opens preview for selected JPEG
                    CompleteSynchronously(coordinator.ToggleAsync(explorerContext));
                    Assert.True(presenter.IsOpen);
                    var peekWindow = presenter.Window!;
                    RenderWindow(peekWindow);
                    Assert.True(peekWindow.IsExplorerPreview);
                    Assert.Equal(AppTheme.Light, peekWindow.CurrentAppTheme);
                    Assert.Equal(explorerContext.WindowHandle, new WindowInteropHelper(peekWindow).Owner);
                    Assert.True(peekWindow.VerifyRenderedState());
                    Assert.Equal(24, peekWindow.ViewModel.DisplayPixelWidth);
                    Assert.Equal(16, peekWindow.ViewModel.DisplayPixelHeight);

                    // Image manipulation (zoom, fit, 90-degree rotation) and conversion (JPEG -> PNG)
                    Assert.True(peekWindow.HandleKeyInput(Key.OemPlus));
                    Assert.True(peekWindow.HandleKeyInput(Key.F));
                    Assert.True(peekWindow.HandleKeyInput(Key.R));
                    RenderWindow(peekWindow);
                    Assert.Equal(90, peekWindow.ViewModel.RotationDegrees);
                    Assert.Equal(16, peekWindow.ViewModel.DisplayPixelWidth);
                    Assert.Equal(24, peekWindow.ViewModel.DisplayPixelHeight);
                    Assert.True(peekWindow.VerifyRenderedState());

                    var conversionResult = peekWindow.ViewModel.ConvertCurrentImageTo(convertedOutputPath);
                    Assert.True(conversionResult.IsSuccess, conversionResult.Message);
                    Assert.True(File.Exists(convertedOutputPath));
                    var reloadedConverted = new FilePreviewService().Load(convertedOutputPath);
                    Assert.Equal(PreviewStateKind.ImageLoaded, reloadedConverted.State);
                    Assert.Equal(24, reloadedConverted.PixelWidth);
                    Assert.Equal(16, reloadedConverted.PixelHeight);

                    // Switch Explorer selection to ZIP archive and preview internal PNG and text entries
                    selection.SelectedPath = zipSample;
                    CompleteSynchronously(coordinator.RefreshAsync());
                    RenderWindow(peekWindow);
                    Assert.Equal(PreviewStateKind.ArchiveLoaded, peekWindow.ViewModel.State);
                    Assert.True(peekWindow.VerifyRenderedState());

                    peekWindow.ViewModel.SelectedArchiveEntry = peekWindow.ViewModel.ArchiveEntries.Single(entry => entry.Name == "sample.png");
                    RenderWindow(peekWindow);
                    Assert.Equal(PreviewStateKind.ImageLoaded, peekWindow.ViewModel.State);
                    Assert.True(peekWindow.VerifyRenderedState());

                    peekWindow.ViewModel.SelectedArchiveEntry = peekWindow.ViewModel.ArchiveEntries.Single(entry => entry.Name == "readme.txt");
                    RenderWindow(peekWindow);
                    Assert.Equal(PreviewStateKind.TextLoaded, peekWindow.ViewModel.State);
                    Assert.Contains("Archive text preview", peekWindow.ViewModel.TextContent, StringComparison.Ordinal);
                    Assert.True(peekWindow.VerifyRenderedState());

                    // Switch Explorer selection to Markdown file and verify Sepia theme preference
                    selection.SelectedPath = mdSample;
                    CompleteSynchronously(coordinator.RefreshAsync());
                    RenderWindow(peekWindow);
                    Assert.Equal(PreviewStateKind.TextLoaded, peekWindow.ViewModel.State);
                    Assert.True(peekWindow.ViewModel.IsMarkdownRendered);
                    Assert.Equal(MarkdownTheme.Sepia, peekWindow.ViewModel.SelectedMarkdownTheme.Theme);
                    Assert.True(peekWindow.VerifyRenderedState());

                    // Switch Explorer selection to damaged image and unsupported binary
                    selection.SelectedPath = damagedJpg;
                    CompleteSynchronously(coordinator.RefreshAsync());
                    RenderWindow(peekWindow);
                    Assert.Equal(PreviewStateKind.Error, peekWindow.ViewModel.State);
                    Assert.True(peekWindow.VerifyRenderedState());

                    selection.SelectedPath = unsupportedBin;
                    CompleteSynchronously(coordinator.RefreshAsync());
                    RenderWindow(peekWindow);
                    Assert.Equal(PreviewStateKind.TextLoaded, peekWindow.ViewModel.State);
                    Assert.True(peekWindow.VerifyRenderedState());

                    // Space closes Explorer preview window
                    CompleteSynchronously(coordinator.ToggleAsync(explorerContext));
                    Assert.False(presenter.IsOpen);
                    Assert.Null(presenter.Window);
                }
                finally
                {
                    presenter.Close();
                    explorerOwner.Close();
                }
            });

            // 5. Uninstall: remove startup registration, shell menu key, uninstall key, and installed payload directory
            Assert.True(startupRegistration.TrySetEnabled(false, out var disableError), disableError);
            Registry.CurrentUser.DeleteSubKeyTree(menuSubKey, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(uninstallSubKey, throwOnMissingSubKey: false);
            Directory.Delete(isolatedInstallDir, recursive: true);

            Assert.False(File.Exists(installedExe));
            Assert.False(Directory.Exists(isolatedInstallDir));
            Assert.Null(Registry.CurrentUser.OpenSubKey(uninstallSubKey, writable: false));
            Assert.Null(Registry.CurrentUser.OpenSubKey(menuSubKey, writable: false));
            Assert.False(startupRegistration.IsEnabled);

            using (var runKeyAfterUninstall = Registry.CurrentUser.OpenSubKey(runSubKey, writable: false))
            {
                Assert.NotNull(runKeyAfterUninstall);
                Assert.Null(runKeyAfterUninstall!.GetValue("MoaView"));
                Assert.Equal("\"C:\\Tools\\ExistingUserApp.exe\"", runKeyAfterUninstall.GetValue("ExistingUserApp"));
            }

            // Verify user settings in %LOCALAPPDATA%\MoaView are preserved across install, run, and uninstall
            Assert.True(File.Exists(isolatedAppThemePath));
            Assert.True(File.Exists(isolatedMarkdownThemePath));
            Assert.Equal(expectedIsolatedAppThemeHash, ComputeFileHashOrNull(isolatedAppThemePath));
            Assert.Equal(expectedIsolatedMarkdownThemeHash, ComputeFileHashOrNull(isolatedMarkdownThemePath));
            Assert.Equal(AppTheme.Light, appThemeSettings.Load());
            Assert.Equal(MarkdownTheme.Sepia, markdownThemeSettings.Load());

            // Verify host user settings were never modified
            Assert.Equal(hostAppThemeBefore, ComputeFileHashOrNull(hostAppThemePath));
            Assert.Equal(hostMarkdownThemeBefore, ComputeFileHashOrNull(hostMarkdownThemePath));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(testRegistryRoot, throwOnMissingSubKey: false);
        }
    }

    private static void AssertIsolatedProcess(
        string exePath,
        string workingDirectory,
        string isolatedProfile,
        string isolatedTemp,
        string arguments,
        string expectedState,
        string? expectedBadge = null,
        string? expectedAppTheme = null,
        string? expectedMarkdownTheme = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        startInfo.Environment["LOCALAPPDATA"] = Path.Combine(isolatedProfile, "Local");
        startInfo.Environment["APPDATA"] = Path.Combine(isolatedProfile, "Roaming");
        startInfo.Environment["USERPROFILE"] = isolatedProfile;
        startInfo.Environment["TEMP"] = isolatedTemp;
        startInfo.Environment["TMP"] = isolatedTemp;

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(20000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            Assert.Fail($"Isolated MoaView.exe timed out for arguments: {arguments}");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult().Trim();
        var stderr = stderrTask.GetAwaiter().GetResult().Trim();

        Assert.True(
            process.ExitCode == 0,
            $"Isolated MoaView.exe exited with code {process.ExitCode} for '{arguments}'. Stdout: {stdout} Stderr: {stderr}");
        Assert.Contains($"MOAVIEW_STARTUP_VERIFIED=True|STATE={expectedState}|VISIBLE=True|", stdout, StringComparison.Ordinal);

        if (!string.IsNullOrEmpty(expectedBadge))
        {
            Assert.Contains($"|BADGE={expectedBadge}|", stdout, StringComparison.Ordinal);
        }

        if (!string.IsNullOrEmpty(expectedAppTheme))
        {
            Assert.Contains($"|THEME={expectedAppTheme}|", stdout, StringComparison.Ordinal);
        }

        if (!string.IsNullOrEmpty(expectedMarkdownTheme))
        {
            Assert.Contains($"|MDTHEME={expectedMarkdownTheme}", stdout, StringComparison.Ordinal);
        }
    }

    private static void RenderWindow(Window window)
    {
        window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static void CompleteSynchronously(Task task) => task.GetAwaiter().GetResult();

    private sealed class IsolatedExplorerSelectionStub : IExplorerSelectionService
    {
        public string? SelectedPath { get; set; }

        public Task<string?> GetSelectedPathAsync(ExplorerContext context) => Task.FromResult(SelectedPath);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MoaView.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate MoaView.sln from AppContext.BaseDirectory.");
    }

    private static string FindBuiltApplicationDirectory(string repoRoot)
    {
        var testOutputDir = new DirectoryInfo(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var tfm = testOutputDir.Name;
        var configuration = testOutputDir.Parent?.Name ?? "Debug";
        var candidate = Path.Combine(repoRoot, "src", "MoaView", "bin", configuration, tfm);
        if (File.Exists(Path.Combine(candidate, "MoaView.exe")) &&
            File.Exists(Path.Combine(candidate, "MoaView.runtimeconfig.json")))
        {
            return candidate;
        }

        return AppContext.BaseDirectory;
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var targetFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            var targetSubDir = Path.Combine(destinationDir, Path.GetFileName(directory));
            CopyDirectoryRecursive(directory, targetSubDir);
        }
    }

    private static string? ComputeFileHashOrNull(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
