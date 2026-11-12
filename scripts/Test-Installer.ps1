param([string]$Installer)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Installer) { $Installer = Join-Path $repo 'artifacts/installer/MoaView-0.1.12-Setup-x64.exe' }
$Installer = (Resolve-Path -LiteralPath $Installer).Path
$registryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{23C80132-6E95-43B2-AD9B-A283239A5947}_is1'
$menuKey = 'HKCU:\Software\Classes\*\shell\MoaView.Preview'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$identityName = 'MoaView.ExplorerIntegration'
$existingRunValue = if (Test-Path -LiteralPath $runKey) { (Get-Item -LiteralPath $runKey).GetValue('MoaView', $null) } else { $null }
if ((Test-Path -LiteralPath $registryPath) -or (Test-Path -LiteralPath $menuKey) -or (Get-AppxPackage -Name $identityName) -or ($null -ne $existingRunValue) -or @(Get-Process -Name 'MoaView' -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'MoaView is already installed, registered for startup, or running for this user; run this check with a separate Windows test account.'
}
$checkRoot = Join-Path $repo ('artifacts/install-check/' + [Guid]::NewGuid().ToString('N'))
$target = [IO.Path]::GetFullPath((Join-Path $checkRoot 'MoaView Test'))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/install-check')) + [IO.Path]::DirectorySeparatorChar
if (!$target.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid test installation directory.' }
New-Item -ItemType Directory -Path $checkRoot -Force | Out-Null
$results = [Collections.Generic.List[object]]::new()
$installLog = Join-Path $checkRoot 'install.log'
$uninstallLog = Join-Path $checkRoot 'uninstall.log'

function Test-WebView2Installed {
    foreach ($clientKey in @(
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
        'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
        'HKCU:\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
    )) {
        if (Test-Path -LiteralPath $clientKey) {
            $pv = (Get-Item -LiteralPath $clientKey).GetValue('pv', $null)
            if ($pv -and $pv -ne '0.0.0.0') { return $true }
        }
    }
    return $false
}

function Write-SampleImage([string]$Path, [string]$Format) {
    $pixels = [byte[]]@(
        32, 96, 224, 255, 32, 96, 224, 255,
        224, 96, 32, 255, 224, 96, 32, 255
    )
    $bitmap = [System.Windows.Media.Imaging.BitmapSource]::Create(
        2, 2, 96.0, 96.0, [System.Windows.Media.PixelFormats]::Bgra32, $null, $pixels, 8)
    switch ($Format) {
        'JPEG' { $encoder = [System.Windows.Media.Imaging.JpegBitmapEncoder]::new(); $encoder.QualityLevel = 95 }
        'PNG'  { $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new() }
        'BMP'  { $encoder = [System.Windows.Media.Imaging.BmpBitmapEncoder]::new() }
        'GIF'  { $encoder = [System.Windows.Media.Imaging.GifBitmapEncoder]::new() }
        'TIFF' { $encoder = [System.Windows.Media.Imaging.TiffBitmapEncoder]::new() }
        default { throw "Unsupported sample image format: $Format" }
    }
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $encoder.Save($stream) }
    finally { $stream.Dispose() }
}

function New-ZipSample([string]$Path, [string]$TextPath, [string]$ImagePath) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($sample in @(
            [pscustomobject]@{ Name = 'readme.txt'; Path = $TextPath },
            [pscustomobject]@{ Name = 'image.png'; Path = $ImagePath }
        )) {
            $entry = $archive.CreateEntry($sample.Name)
            $entryStream = $entry.Open()
            $fileStream = [IO.File]::OpenRead($sample.Path)
            try { $fileStream.CopyTo($entryStream) }
            finally { $fileStream.Dispose(); $entryStream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}

$script:webView2Installed = Test-WebView2Installed

function Assert-Startup([string]$Name, [string]$File, [string]$State, [string]$ExpectedBadge = '') {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = Join-Path $target 'MoaView.exe'
    $start.Arguments = '--verify-startup' + $(if ($File) { ' "' + $File + '"' } else { '' })
    $start.WorkingDirectory = $target
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.Encoding]::UTF8
    $start.StandardErrorEncoding = [Text.Encoding]::UTF8
    $emptyDotnetRoot = Join-Path $checkRoot 'no-global-runtime'
    $start.EnvironmentVariables['DOTNET_ROOT'] = $emptyDotnetRoot
    $start.EnvironmentVariables['DOTNET_ROOT_X64'] = $emptyDotnetRoot
    $start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $start.EnvironmentVariables['DOTNET_ROLL_FORWARD'] = 'Disable'
    $start.EnvironmentVariables['DOTNET_SHARED_STORE'] = $emptyDotnetRoot
    [void]$start.EnvironmentVariables.Remove('DOTNET_ADDITIONAL_DEPS')
    [void]$start.EnvironmentVariables.Remove('DOTNET_STARTUP_HOOKS')
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (!$process.WaitForExit(25000)) { $process.Kill(); throw "Startup timeout: $Name" }
        $report = $process.StandardOutput.ReadToEnd()
        $errorText = $process.StandardError.ReadToEnd()
        if ($process.ExitCode -ne 0 -or !$report.Contains("MOAVIEW_STARTUP_VERIFIED=True|STATE=$State|")) {
            throw "Startup failure: $Name $report $errorText"
        }
        if ($ExpectedBadge -and !$report.Contains("|BADGE=$ExpectedBadge|")) {
            throw "Unexpected badge for ${Name}: $report"
        }
        if ($Name -eq 'Rive') {
            $expectedRive = if ($script:webView2Installed) { '|RIVE=ready|' } else { '|RIVE=error|' }
            if (!$report.Contains($expectedRive)) { throw "Rive runtime check failed (WebView2=$($script:webView2Installed)): $report" }
        }
        if ($State -eq 'SpecializedLoaded') {
            $expectedSpecialized = if ($script:webView2Installed) { '|SPECIALIZED=ready|' } else { '|SPECIALIZED=error|' }
            if (!$report.Contains($expectedSpecialized)) { throw "$Name check failed (WebView2=$($script:webView2Installed)): $report" }
        }
        $results.Add([pscustomobject]@{Case=$Name;Passed=$true;WebView2Installed=$script:webView2Installed;Report=$report.Trim()})
    } finally { $process.Dispose() }
}

function Assert-ExplorerMenu([string]$File) {
    $verb = Get-ItemProperty -LiteralPath $menuKey
    $command = (Get-Item -LiteralPath "$menuKey\command").GetValue('')
    $expectedCommand = '"' + (Join-Path $target 'MoaView.exe') + '" --preview "%1"'
    if (!$verb.MUIVerb -or $verb.MultiSelectModel -ne 'Single' -or $command -ne $expectedCommand) {
        throw 'Explorer preview menu is not registered correctly.'
    }

    $start = [Diagnostics.ProcessStartInfo]::new($File)
    $start.UseShellExecute = $true
    $start.Verb = 'MoaView.Preview'
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Minimized
    $process = [Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw 'Explorer preview command did not start a process.' }
    try {
        $title = [IO.Path]::GetFileName($File) + ' - '
        $deadline = [DateTime]::UtcNow.AddSeconds(12)
        do {
            $process.Refresh()
            if ($process.MainWindowTitle.StartsWith($title)) { break }
            Start-Sleep -Milliseconds 100
        } while (!$process.HasExited -and [DateTime]::UtcNow -lt $deadline)
        if ($process.HasExited -or !$process.MainWindowTitle.StartsWith($title)) { throw 'Explorer preview command did not open the selected file.' }
        if (!$process.CloseMainWindow() -or !$process.WaitForExit(5000)) { throw 'Explorer preview window did not close.' }
        $results.Add([pscustomobject]@{Case='Explorer menu opens selected file';Passed=$true})
    } finally {
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}

function Get-FileFingerprint([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

$userDataRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'MoaView'
$userDataExistedBefore = Test-Path -LiteralPath $userDataRoot -PathType Container
$userSettings = @(
    [pscustomobject]@{ Name = 'app-theme.json'; Path = (Join-Path $userDataRoot 'app-theme.json'); DefaultJson = '{"theme":"Light"}'; ExistedBefore = $false; ExpectedHash = $null },
    [pscustomobject]@{ Name = 'markdown-theme.json'; Path = (Join-Path $userDataRoot 'markdown-theme.json'); DefaultJson = '{"theme":"Sepia"}'; ExistedBefore = $false; ExpectedHash = $null }
)
foreach ($setting in $userSettings) {
    $setting.ExistedBefore = Test-Path -LiteralPath $setting.Path -PathType Leaf
    if (!$setting.ExistedBefore) {
        New-Item -ItemType Directory -Path $userDataRoot -Force | Out-Null
        [IO.File]::WriteAllText($setting.Path, $setting.DefaultJson, [Text.UTF8Encoding]::new($false))
    }
    $setting.ExpectedHash = Get-FileFingerprint $setting.Path
}

try {
    $setup = Start-Process -FilePath $Installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/TASKS=',"/DIR=`"$target`"","/LOG=`"$installLog`"") -WindowStyle Hidden -Wait -PassThru
    if ($setup.ExitCode -ne 0) { throw "Installer failed: $($setup.ExitCode)" }
    if (!(Test-Path -LiteralPath $registryPath)) { throw 'Uninstall registration missing.' }
    $registered = (Get-ItemProperty -LiteralPath $registryPath).InstallLocation
    if ([IO.Path]::GetFullPath($registered).TrimEnd('\') -ne $target) { throw 'Unexpected registered installation location.' }
    foreach ($name in @('MoaView.exe','MoaView.dll','MoaView.runtimeconfig.json','coreclr.dll','hostfxr.dll','PresentationFramework.dll','Microsoft.Windows.SDK.NET.dll','WinRT.Runtime.dll','SharpCompress.dll','content-preview.md','third-party-notices.md','install.md','Assets/Rive/vendor/rive.wasm','Assets/Rive/vendor/rive_fallback.wasm','Assets/Licenses/Markdig.txt','Assets/Licenses/WebView2-LICENSE.txt','Assets/Licenses/SharpCompress.txt')) {
        if (!(Test-Path -LiteralPath (Join-Path $target $name))) { throw "Installed payload missing: $name" }
    }
    foreach ($name in @('Assets/ThreeD/index.html','Assets/ThreeD/viewer.js','Assets/ThreeD/vendor/build/three.module.js','Assets/ThreeD/vendor/build/three.core.js','Assets/ThreeD/vendor/examples/jsm/loaders/FBXLoader.js','Assets/ThreeD/LICENSE','Assets/ThreeD/vendor/examples/jsm/libs/LICENSE-fflate.txt')) {
        if (!(Test-Path -LiteralPath (Join-Path $target $name))) { throw "Installed 3D payload missing: $name" }
    }
    Add-Type -AssemblyName PresentationCore
    Add-Type -AssemblyName WindowsBase
    Assert-Startup 'Empty' '' 'Empty'
    $markdown = Join-Path $checkRoot 'preview sample & notes.md'
    '# Installer verification' + "`r`n`r`n**Markdown** preview" | Set-Content -LiteralPath $markdown -Encoding UTF8
    Assert-Startup 'Markdown' $markdown 'TextLoaded'
    $images = [ordered]@{ JPEG = 'sample.jpg'; PNG = 'sample.png'; BMP = 'sample.bmp'; GIF = 'sample.gif'; TIFF = 'sample.tiff' }
    foreach ($format in $images.Keys) {
        $imagePath = Join-Path $checkRoot $images[$format]
        Write-SampleImage $imagePath $format
        Assert-Startup "Image $format" $imagePath 'ImageLoaded'
    }
    $zipSample = Join-Path $checkRoot 'sample-archive.zip'
    New-ZipSample $zipSample $markdown (Join-Path $checkRoot $images.PNG)
    Assert-Startup 'ZIP archive' $zipSample 'ArchiveLoaded'
    $damaged = Join-Path $checkRoot 'damaged.jpg'
    [IO.File]::WriteAllBytes($damaged, [byte[]](0xFF, 0xD8, 0x00, 0x22))
    $openFailedBadge = [string]::Concat([char]0xC5F4, [char]0xAE30, ' ', [char]0xC2E4, [char]0xD328)
    Assert-Startup 'Damaged image notice' $damaged 'Error' $openFailedBadge
    $binary = Join-Path $checkRoot 'unknown.bin'
    [IO.File]::WriteAllBytes($binary, [byte[]](0, 1, 255))
    Assert-Startup 'Binary file details' $binary 'TextLoaded'
    $ppm = Join-Path $checkRoot 'sample.ppm'
    [IO.File]::WriteAllText($ppm, "P3`n1 1`n255`n255 0 0`n", [Text.Encoding]::ASCII)
    Assert-Startup 'PPM image' $ppm 'ImageLoaded'
    foreach ($name in @('Assets/Licenses/Magick.NET-Notices.txt','Assets/Licenses/libbpg-0.9.8.tar.gz','Assets/Bpg/bpgdec.js','Assets/Bpg/bpgdec8a.js')) {
        if (!(Test-Path -LiteralPath (Join-Path $target $name))) { throw "Installed image payload missing: $name" }
    }
    if (Get-AppxPackage -Name $identityName) {
        if (Test-Path -LiteralPath $menuKey) { throw 'A legacy menu was left alongside the modern menu.' }
        $results.Add([pscustomobject]@{Case='Windows 11 identity registration';Passed=$true})
    } else {
        Assert-ExplorerMenu $markdown
    }
    if (Test-Path -LiteralPath (Join-Path $target 'MoaView.ExplorerCommand.dll')) {
        & (Join-Path $PSScriptRoot 'Test-ExplorerCommand.ps1') -ApplicationDirectory $target -SampleFile $markdown
        $results.Add([pscustomobject]@{Case='Windows 11 native command opens selected file';Passed=$true})
    }
    $rive = Join-Path $repo 'tests/MoaView.Tests/Fixtures/data_bind_runtime_test.riv'
    Assert-Startup 'Rive' $rive 'RiveLoaded'
    $svg = Join-Path $checkRoot 'sample.svg'
    '<svg xmlns="http://www.w3.org/2000/svg" width="40" height="30"><rect width="40" height="30" fill="blue"/></svg>' |
        Set-Content -LiteralPath $svg -Encoding UTF8
    Assert-Startup 'SVG' $svg 'SpecializedLoaded'
    foreach ($setting in $userSettings) {
        if ((Get-FileFingerprint $setting.Path) -ne $setting.ExpectedHash) {
            throw "Startup verification modified or removed user setting: $($setting.Name)"
        }
    }
    $results.Add([pscustomobject]@{Case='Install registration and payload';Passed=$true})
} finally {
    $uninstaller = Join-Path $target 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        $uninstall = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$uninstallLog`"") -WindowStyle Hidden -Wait -PassThru
        if ($uninstall.ExitCode -ne 0) { throw "Uninstall failed: $($uninstall.ExitCode)" }
        $runRemaining = if (Test-Path -LiteralPath $runKey) { (Get-Item -LiteralPath $runKey).GetValue('MoaView', $null) } else { $null }
        if ((Test-Path -LiteralPath (Join-Path $target 'MoaView.exe')) -or (Test-Path -LiteralPath $registryPath) -or (Test-Path -LiteralPath $menuKey) -or ($null -ne $runRemaining)) { throw 'Uninstall left application files or registration.' }
        if (Get-AppxPackage -Name $identityName) { throw 'Uninstall left the Explorer identity package registered.' }
        foreach ($setting in $userSettings) {
            if ((Get-FileFingerprint $setting.Path) -ne $setting.ExpectedHash) {
                throw "Uninstall removed or modified user setting: $($setting.Name)"
            }
        }
        $results.Add([pscustomobject]@{Case='User settings preserved across install and uninstall';Passed=$true})
        $results.Add([pscustomobject]@{Case='Uninstall';Passed=$true})
    }
    foreach ($setting in $userSettings) {
        if (!$setting.ExistedBefore -and (Test-Path -LiteralPath $setting.Path -PathType Leaf)) {
            Remove-Item -LiteralPath $setting.Path -Force -ErrorAction SilentlyContinue
        }
    }
    if (!$userDataExistedBefore -and (Test-Path -LiteralPath $userDataRoot -PathType Container)) {
        if (@(Get-ChildItem -LiteralPath $userDataRoot -Force -ErrorAction SilentlyContinue).Count -eq 0) {
            Remove-Item -LiteralPath $userDataRoot -Force -ErrorAction SilentlyContinue
        }
    }
    $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $checkRoot 'results.json') -Encoding UTF8
}
$results | Format-Table Case, Passed
Write-Output "Logs: $checkRoot"
