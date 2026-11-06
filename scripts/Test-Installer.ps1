param([string]$Installer)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Installer) { $Installer = Join-Path $repo 'artifacts/installer/MoaView-0.1.6-Setup-x64.exe' }
$Installer = (Resolve-Path -LiteralPath $Installer).Path
$registryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{23C80132-6E95-43B2-AD9B-A283239A5947}_is1'
$menuKey = 'HKCU:\Software\Classes\*\shell\MoaView.Preview'
$identityName = 'MoaView.ExplorerIntegration'
if ((Test-Path -LiteralPath $registryPath) -or (Test-Path -LiteralPath $menuKey) -or (Get-AppxPackage -Name $identityName)) { throw 'MoaView is already installed for this user; run this check with a separate Windows test account.' }
$checkRoot = Join-Path $repo ('artifacts/install-check/' + [Guid]::NewGuid().ToString('N'))
$target = [IO.Path]::GetFullPath((Join-Path $checkRoot 'MoaView Test'))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/install-check')) + [IO.Path]::DirectorySeparatorChar
if (!$target.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid test installation directory.' }
New-Item -ItemType Directory -Path $checkRoot -Force | Out-Null
$results = [Collections.Generic.List[object]]::new()
$installLog = Join-Path $checkRoot 'install.log'
$uninstallLog = Join-Path $checkRoot 'uninstall.log'

function Assert-Startup([string]$Name, [string]$File, [string]$State) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = Join-Path $target 'MoaView.exe'
    $start.Arguments = '--verify-startup' + $(if ($File) { ' "' + $File + '"' } else { '' })
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.EnvironmentVariables['DOTNET_ROOT'] = Join-Path $checkRoot 'no-global-runtime'
    $start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (!$process.WaitForExit(25000)) { $process.Kill(); throw "Startup timeout: $Name" }
        $report = $process.StandardOutput.ReadToEnd()
        $errorText = $process.StandardError.ReadToEnd()
        if ($process.ExitCode -ne 0 -or !$report.Contains("MOAVIEW_STARTUP_VERIFIED=True|STATE=$State|")) {
            throw "Startup failure: $Name $report $errorText"
        }
        if ($Name -eq 'Rive' -and !$report.Contains('|RIVE=ready|')) { throw 'Rive runtime did not render.' }
        if ($State -eq 'SpecializedLoaded' -and !$report.Contains('|SPECIALIZED=ready|')) { throw "$Name did not render." }
        $results.Add([pscustomobject]@{Case=$Name;Passed=$true;Report=$report.Trim()})
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

try {
    $setup = Start-Process -FilePath $Installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/TASKS=',"/DIR=`"$target`"","/LOG=`"$installLog`"") -WindowStyle Hidden -Wait -PassThru
    if ($setup.ExitCode -ne 0) { throw "Installer failed: $($setup.ExitCode)" }
    if (!(Test-Path -LiteralPath $registryPath)) { throw 'Uninstall registration missing.' }
    $registered = (Get-ItemProperty -LiteralPath $registryPath).InstallLocation
    if ([IO.Path]::GetFullPath($registered).TrimEnd('\') -ne $target) { throw 'Unexpected registered installation location.' }
    foreach ($name in @('MoaView.exe','coreclr.dll','hostfxr.dll','PresentationFramework.dll','Microsoft.Windows.SDK.NET.dll','WinRT.Runtime.dll','SharpCompress.dll','Assets/Rive/vendor/rive.wasm','Assets/Rive/vendor/rive_fallback.wasm','Assets/Licenses/Markdig.txt','Assets/Licenses/WebView2-LICENSE.txt','Assets/Licenses/SharpCompress.txt')) {
        if (!(Test-Path -LiteralPath (Join-Path $target $name))) { throw "Installed payload missing: $name" }
    }
    Assert-Startup 'Empty' '' 'Empty'
    $markdown = Join-Path $checkRoot 'preview sample & notes.md'
    '# Installer verification' + "`r`n`r`n**Markdown** preview" | Set-Content -LiteralPath $markdown -Encoding UTF8
    Assert-Startup 'Markdown' $markdown 'TextLoaded'
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
    $results.Add([pscustomobject]@{Case='Install registration and payload';Passed=$true})
} finally {
    $uninstaller = Join-Path $target 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        $uninstall = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$uninstallLog`"") -WindowStyle Hidden -Wait -PassThru
        if ($uninstall.ExitCode -ne 0) { throw "Uninstall failed: $($uninstall.ExitCode)" }
        if ((Test-Path -LiteralPath (Join-Path $target 'MoaView.exe')) -or (Test-Path -LiteralPath $registryPath) -or (Test-Path -LiteralPath $menuKey)) { throw 'Uninstall left application files or registration.' }
        if (Get-AppxPackage -Name $identityName) { throw 'Uninstall left the Explorer identity package registered.' }
        $results.Add([pscustomobject]@{Case='Uninstall';Passed=$true})
    }
    $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $checkRoot 'results.json') -Encoding UTF8
}
$results | Format-Table Case, Passed
Write-Output "Logs: $checkRoot"
