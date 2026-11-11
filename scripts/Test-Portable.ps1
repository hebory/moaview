[CmdletBinding()]
param(
    [string]$PublishDirectory,
    [string]$Version,
    [string]$RuntimeVersion = '8.0.33',
    [string]$OutputRoot,
    [ValidateRange(5, 180)][int]$StartupTimeoutSeconds = 40
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$report = [ordered]@{
    schemaVersion = 1
    status = 'failed'
    purpose = '게시 폴더 복사본을 전역 .NET 설치 경로를 제한한 별도 프로세스로 기동하는 무설치 smoke 검사입니다. Windows 설치·제거 또는 깨끗한 PC 검증이 아닙니다.'
    sourcePublishDirectory = $null
    executableDirectory = $null
    resultFile = $null
    applicationVersion = [ordered]@{ expected = $null; actual = $null }
    runtime = [ordered]@{ expected = $RuntimeVersion; includedFrameworks = @(); processDotnetRoot = $null; multilevelLookup = '0' }
    embeddedAssets = 'MoaView.ico/png are WPF Resource items, not loose publish files; successful startup renders MainWindow.xaml, which references both resources.'
    userData = [ordered]@{
        webView2CasesSkipped = $true
        settingsBefore = @{}
        settingsAfter = @{}
        settingsUnchanged = $null
        note = 'WebView2를 생성하는 SVG/WebP/Rive/3D 표본은 실행하지 않습니다. 앱 테마 파일은 시작 시 읽힐 수 있어 검사 전후 해시를 비교하고, 테스트별 로컬 프로필·임시 경로는 별도 디렉터리를 가리킵니다.'
    }
    cases = [Collections.Generic.List[object]]::new()
    limitations = @(
        '이 검사는 Windows 사용자 레지스트리, 앱 설치·제거, 실제 Explorer 입력을 확인하지 않습니다.',
        'ZIP 표본은 아카이브 화면이 열리는지 확인합니다. ZIP 내부 텍스트·이미지 항목 선택은 별도의 WPF 기능 테스트 대상입니다.',
        'WebView2 프로필을 사용하는 SVG·WebP·AVIF·Rive·3D 표본은 사용자 프로필 격리를 보장할 수 없어 생략합니다.',
        '깨끗한 Windows 계정·VM, WebView2가 없는 환경 및 Windows 버전별 호환성을 증명하지 않습니다.',
        'GIF 검사는 정적 첫 프레임의 이미지 기동만 확인하며 애니메이션 재생을 확인하지 않습니다.'
    )
    error = $null
}
$runRoot = $null

function Get-FileFingerprint([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Test-PathWithin([string]$Candidate, [string]$Parent) {
    $parentPrefix = $Parent.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return $Candidate.StartsWith($parentPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        $Candidate.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar).Equals(
            $Parent.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar),
            [StringComparison]::OrdinalIgnoreCase)
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
        default { throw "지원하지 않는 검사 이미지 형식입니다: $Format" }
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

function Invoke-StartupCase([string]$Name, [string]$SamplePath, [string]$ExpectedState, [string]$ExpectedBadge) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = Join-Path $script:copyDirectory 'MoaView.exe'
    $start.Arguments = '--verify-startup' + $(if ($SamplePath) { ' "' + $SamplePath + '"' } else { '' })
    $start.WorkingDirectory = $script:copyDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true

    # These values apply to this child process only. MoaView's WebView2 controls
    # use explicit profile paths, so those preview types are intentionally skipped.
    $start.EnvironmentVariables['DOTNET_ROOT'] = $script:emptyDotnetRoot
    $start.EnvironmentVariables['DOTNET_ROOT_X64'] = $script:emptyDotnetRoot
    $start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $start.EnvironmentVariables['DOTNET_ROLL_FORWARD'] = 'Disable'
    $start.EnvironmentVariables['DOTNET_SHARED_STORE'] = $script:emptyDotnetRoot
    $start.EnvironmentVariables['LOCALAPPDATA'] = Join-Path $script:isolatedProfile 'Local'
    $start.EnvironmentVariables['APPDATA'] = Join-Path $script:isolatedProfile 'Roaming'
    $start.EnvironmentVariables['USERPROFILE'] = $script:isolatedProfile
    $start.EnvironmentVariables['TEMP'] = $script:isolatedTemp
    $start.EnvironmentVariables['TMP'] = $script:isolatedTemp
    [void]$start.EnvironmentVariables.Remove('DOTNET_ADDITIONAL_DEPS')
    [void]$start.EnvironmentVariables.Remove('DOTNET_STARTUP_HOOKS')

    $process = [Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw "앱 프로세스를 시작하지 못했습니다: $Name" }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $timedOut = $false
    try {
        if (!$process.WaitForExit($script:timeoutMilliseconds)) {
            $timedOut = $true
            $process.Kill()
            [void]$process.WaitForExit(5000)
        }
        $stdoutTask.Wait()
        $stderrTask.Wait()
        $stdout = $stdoutTask.Result.Trim()
        $stderr = $stderrTask.Result.Trim()
        $marker = [regex]::Match($stdout, 'MOAVIEW_STARTUP_VERIFIED=(True|False)\|STATE=([^|\r\n]+)\|VISIBLE=(True|False)\|[^\r\n]*')
        $passed = !$timedOut -and $marker.Success -and $process.ExitCode -eq 0 -and
            $marker.Groups[1].Value -eq 'True' -and $marker.Groups[2].Value -eq $ExpectedState -and
            $marker.Groups[3].Value -eq 'True'
        if ($ExpectedBadge) { $passed = $passed -and $stdout.Contains("|BADGE=$ExpectedBadge|") }
        $script:report.cases.Add([pscustomobject][ordered]@{
            name = $Name
            passed = [bool]$passed
            expectedState = $ExpectedState
            actualState = if ($marker.Success) { $marker.Groups[2].Value } else { $null }
            exitCode = if ($timedOut) { $null } else { $process.ExitCode }
            timedOut = $timedOut
            startupReport = if ($stdout.Length -gt 2000) { $stdout.Substring(0, 2000) } else { $stdout }
            errorOutput = if ($stderr.Length -gt 2000) { $stderr.Substring(0, 2000) } else { $stderr }
        })
    }
    finally { $process.Dispose() }
}

try {
    if (!$PublishDirectory) { $PublishDirectory = Join-Path $repoRoot 'artifacts/publish/win-x64' }
    $source = (Resolve-Path -LiteralPath $PublishDirectory).Path
    if (!(Test-Path -LiteralPath $source -PathType Container)) { throw "게시 폴더를 찾을 수 없습니다: $PublishDirectory" }
    $source = [IO.Path]::GetFullPath($source)
    $report.sourcePublishDirectory = $source

    $projectXml = [xml](Get-Content -LiteralPath (Join-Path $repoRoot 'src/MoaView/MoaView.csproj') -Raw)
    $projectVersion = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
    if (!$Version) { $Version = [string]$projectVersion }
    if (!$Version -or $Version -notmatch '^\d+\.\d+\.\d+$') { throw '기대 앱 버전을 MoaView.csproj에서 확인하지 못했습니다. -Version으로 지정하세요.' }
    if ($Version -ne [string]$projectVersion) { throw "요청 버전 $Version 이 현재 프로젝트 버전 $projectVersion 과 다릅니다." }
    $report.applicationVersion.expected = $Version

    $runtimeConfigPath = Join-Path $source 'MoaView.runtimeconfig.json'
    $runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
    $frameworks = @($runtimeConfig.runtimeOptions.includedFrameworks)
    if (!$frameworks -or $frameworks.Count -lt 2) { throw '런타임 config에 자체 포함 .NET 및 Windows Desktop 프레임워크가 없습니다.' }
    foreach ($frameworkName in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
        $framework = @($frameworks | Where-Object { $_.name -eq $frameworkName }) | Select-Object -First 1
        if (!$framework -or $framework.version -ne $RuntimeVersion) {
            throw "$frameworkName 자체 포함 버전이 기대값 $RuntimeVersion 과 다릅니다."
        }
    }
    $report.runtime.includedFrameworks = @($frameworks | ForEach-Object { [pscustomobject]@{ name = $_.name; version = $_.version } })

    $payloadFiles = @(
        'MoaView.exe', 'MoaView.dll', 'MoaView.runtimeconfig.json',
        'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll',
        'PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll',
        'Microsoft.Windows.SDK.NET.dll', 'WinRT.Runtime.dll', 'SharpCompress.dll'
    )
    foreach ($relativePath in $payloadFiles) {
        if (!(Test-Path -LiteralPath (Join-Path $source $relativePath) -PathType Leaf)) {
            throw "게시 폴더 필수 파일이 없습니다: $relativePath"
        }
    }
    $actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $source 'MoaView.exe')).ProductVersion
    if ($actualVersion) { $actualVersion = $actualVersion.Split('+')[0] }
    $report.applicationVersion.actual = $actualVersion
    if ($actualVersion -ne $Version) { throw "게시 앱 버전 $actualVersion 이 기대값 $Version 과 다릅니다." }

    $reparseItems = @(Get-ChildItem -LiteralPath $source -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
    if ($reparseItems.Count -gt 0) { throw "게시 폴더 안에 재분석 지점이 있어 안전하게 복사할 수 없습니다: $($reparseItems[0].FullName)" }

    if (!$OutputRoot) { $OutputRoot = Join-Path ([IO.Path]::GetTempPath()) 'MoaView-PortableCheck' }
    if (![IO.Path]::IsPathRooted($OutputRoot)) { $OutputRoot = Join-Path $repoRoot $OutputRoot }
    $outputBase = [IO.Path]::GetFullPath($OutputRoot)
    if (Test-PathWithin $outputBase $source) { throw '검사 결과 폴더를 게시 폴더 안에 둘 수 없습니다.' }
    if (!(Test-Path -LiteralPath $outputBase -PathType Container)) { New-Item -ItemType Directory -Path $outputBase -ErrorAction Stop | Out-Null }
    if ((Get-Item -LiteralPath $outputBase -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '검사 결과 폴더는 재분석 지점(심볼릭 링크 등)일 수 없습니다.' }
    do { $runId = [Guid]::NewGuid().ToString('N'); $runRoot = Join-Path $outputBase $runId } while (Test-Path -LiteralPath $runRoot)
    if (Test-PathWithin ([IO.Path]::GetFullPath($runRoot)) $source) { throw '검사 실행 폴더가 게시 폴더 안으로 들어갑니다.' }
    New-Item -ItemType Directory -Path $runRoot -ErrorAction Stop | Out-Null
    $runRoot = [IO.Path]::GetFullPath($runRoot)
    $report.resultFile = Join-Path $runRoot 'result.json'
    $script:copyDirectory = Join-Path $runRoot 'app'
    $samplesDirectory = Join-Path $runRoot 'samples'
    $script:emptyDotnetRoot = Join-Path $runRoot 'global-dotnet-unavailable'
    $script:isolatedProfile = Join-Path $runRoot 'isolated-profile'
    $script:isolatedTemp = Join-Path $runRoot 'isolated-temp'
    $script:timeoutMilliseconds = $StartupTimeoutSeconds * 1000
    foreach ($directory in @($script:copyDirectory, $samplesDirectory,
            (Join-Path $script:isolatedProfile 'Local'), (Join-Path $script:isolatedProfile 'Roaming'), $script:isolatedTemp)) {
        New-Item -ItemType Directory -Path $directory -ErrorAction Stop | Out-Null
    }
    if (Test-Path -LiteralPath $script:emptyDotnetRoot) { throw '전역 .NET 우회 경로가 존재하므로 검사를 중단합니다.' }
    $report.executableDirectory = $script:copyDirectory
    $report.runtime.processDotnetRoot = $script:emptyDotnetRoot

    $copyItems = @(Get-ChildItem -LiteralPath $source -Force)
    if ($copyItems.Count -eq 0) { throw '게시 폴더가 비어 있습니다.' }
    foreach ($item in $copyItems) { Copy-Item -LiteralPath $item.FullName -Destination $script:copyDirectory -Recurse -Force -ErrorAction Stop }
    foreach ($relativePath in $payloadFiles) {
        if (!(Test-Path -LiteralPath (Join-Path $script:copyDirectory $relativePath) -PathType Leaf)) {
            throw "복사본 필수 파일이 없습니다: $relativePath"
        }
    }

    Add-Type -AssemblyName PresentationCore
    Add-Type -AssemblyName WindowsBase
    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $settingPaths = @('MoaView/app-theme.json', 'MoaView/markdown-theme.json')
    foreach ($settingName in $settingPaths) {
        $report.userData.settingsBefore[$settingName] = Get-FileFingerprint (Join-Path $localAppData $settingName)
    }

    $images = [ordered]@{ JPEG = 'sample jpeg with spaces.jpg'; PNG = 'sample png.png'; BMP = 'sample bmp.bmp'; GIF = 'sample gif.gif'; TIFF = 'sample tiff.tiff' }
    foreach ($format in $images.Keys) { Write-SampleImage (Join-Path $samplesDirectory $images[$format]) $format }
    $textSample = Join-Path $samplesDirectory 'readme.txt'
    [IO.File]::WriteAllText($textSample, "MoaView portable preview sample.", [Text.UTF8Encoding]::new($false))
    $zipSample = Join-Path $samplesDirectory 'text-and-image.zip'
    New-ZipSample $zipSample $textSample (Join-Path $samplesDirectory $images.PNG)
    $damagedSample = Join-Path $samplesDirectory 'damaged.jpg'
    [IO.File]::WriteAllBytes($damagedSample, [byte[]]@(0xFF, 0xD8, 0x00, 0x22))
    $unsupportedSample = Join-Path $samplesDirectory 'unknown.bin'
    [IO.File]::WriteAllBytes($unsupportedSample, [byte[]]@(0, 1, 255))

    Invoke-StartupCase '빈 앱 시작' $null 'Empty' $null
    foreach ($format in $images.Keys) {
        $caseName = if ($format -eq 'GIF') { '이미지 GIF 정적 첫 프레임' } else { "이미지 $format" }
        Invoke-StartupCase $caseName (Join-Path $samplesDirectory $images[$format]) 'ImageLoaded' $null
    }
    Invoke-StartupCase 'ZIP 텍스트·이미지 아카이브 열기' $zipSample 'ArchiveLoaded' $null
    Invoke-StartupCase '손상 이미지 오류 안내' $damagedSample 'Error' '열기 실패'
    Invoke-StartupCase '미지원 바이너리 안내' $unsupportedSample 'TextLoaded' $null

    foreach ($settingName in $settingPaths) {
        $report.userData.settingsAfter[$settingName] = Get-FileFingerprint (Join-Path $localAppData $settingName)
    }
    $settingsUnchanged = $true
    foreach ($settingName in $settingPaths) {
        if ($report.userData.settingsBefore[$settingName] -ne $report.userData.settingsAfter[$settingName]) { $settingsUnchanged = $false }
    }
    $report.userData.settingsUnchanged = $settingsUnchanged
    if (!$settingsUnchanged) { throw '검사 도중 사용자 설정 파일의 전후 해시가 달라졌습니다. 원본을 자동 복원하지 않았습니다.' }
    $report.status = if (@($report.cases | Where-Object { !$_.passed }).Count -eq 0) { 'passed' } else { 'failed' }
}
catch {
    $report.status = 'failed'
    $report.error = $_.Exception.Message
}

$json = $report | ConvertTo-Json -Depth 8
if ($report.resultFile) {
    try { [IO.File]::WriteAllText($report.resultFile, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false)) }
    catch { $report.error = "결과 JSON 저장 실패: $($_.Exception.Message)"; $report.status = 'failed'; $json = $report | ConvertTo-Json -Depth 8 }
}
Write-Output $json
if ($report.status -ne 'passed') { exit 1 }
