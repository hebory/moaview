using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using MoaView.Models;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
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

        var contentDocText = File.ReadAllText(Path.Combine(repoRoot, "docs", "content-preview.md"));
        var readmeText = File.ReadAllText(Path.Combine(repoRoot, "README.md"));
        Assert.Contains($"MoaView-{version}-Setup-x64.exe", readmeText, StringComparison.Ordinal);
        Assert.Contains("docs/QUIC-8-clean-environment-verification.md", readmeText, StringComparison.Ordinal);

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

    private static void AssertIsolatedProcess(
        string exePath,
        string workingDirectory,
        string isolatedProfile,
        string isolatedTemp,
        string arguments,
        string expectedState,
        string? expectedBadge = null)
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
