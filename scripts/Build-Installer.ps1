param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.1.8',
    [ValidatePattern('^8\.0\.\d+$')][string]$RuntimeVersion = '8.0.33',
    [string]$ExplorerIdentityDirectory,
    [switch]$SkipPublish
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolsRoot = Join-Path $repo '.tools'
$publish = Join-Path $repo 'artifacts/publish/win-x64'
$output = Join-Path $repo 'artifacts/installer'
New-Item -ItemType Directory -Path $toolsRoot, $output -Force | Out-Null
$publishArguments = @()
$installerArguments = @()
if ($ExplorerIdentityDirectory) {
    if ($SkipPublish) { throw 'Publish the executable with the matching Explorer identity manifest.' }
    $identityDirectory = (Resolve-Path -LiteralPath $ExplorerIdentityDirectory).Path
    foreach ($name in @('MoaView.manifest', 'MoaView.ExplorerCommand.dll', 'MoaView.ExplorerIdentity.msix')) {
        if (!(Test-Path -LiteralPath (Join-Path $identityDirectory $name) -PathType Leaf)) { throw "Explorer identity file missing: $name" }
    }
    $publishArguments += '-p:ApplicationManifest=' + (Join-Path $identityDirectory 'MoaView.manifest')
    $installerArguments += "/DExplorerIdentityDir=$identityDirectory"
}

function Assert-SignedDownload([string]$Path, [string]$Url, [string]$Publisher) {
    if (!(Test-Path -LiteralPath $Path)) { Invoke-WebRequest -Uri $Url -OutFile $Path }
    $signature = Get-AuthenticodeSignature -FilePath $Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike "*O=$Publisher,*") {
        throw "Download signature validation failed: $Path"
    }
}

$compilerSetup = Join-Path $toolsRoot 'innosetup-7.1.1-x64.exe'
$compiler = Join-Path $toolsRoot 'InnoSetup/ISCC.exe'
Assert-SignedDownload $compilerSetup 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe' 'Pyrsys B.V.'
if (!(Test-Path -LiteralPath $compiler)) {
    $compilerDir = Join-Path $toolsRoot 'InnoSetup'
    $process = Start-Process -FilePath $compilerSetup -ArgumentList @('/PORTABLE=1','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/NOICONS',"/DIR=`"$compilerDir`"") -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Inno Setup portable preparation failed: $($process.ExitCode)" }
}
$bootstrapper = Join-Path $toolsRoot 'MicrosoftEdgeWebview2Setup.exe'
Assert-SignedDownload $bootstrapper 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' 'Microsoft Corporation'

if (!$SkipPublish) {
    $artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    $resolvedPublish = [IO.Path]::GetFullPath($publish)
    if (!$resolvedPublish.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish path is outside artifacts.' }
    if (Test-Path -LiteralPath $resolvedPublish) { Remove-Item -LiteralPath $resolvedPublish -Recurse -Force }
    & dotnet publish (Join-Path $repo 'src/MoaView/MoaView.csproj') -c Release -r win-x64 --self-contained true "-p:Version=$Version" "-p:RuntimeFrameworkVersion=$RuntimeVersion" -p:PublishSingleFile=false -p:PublishTrimmed=false -p:RestoreSources=https://api.nuget.org/v3/index.json -o $publish -m:1 -v:minimal @publishArguments
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
}
$config = Get-Content -LiteralPath (Join-Path $publish 'MoaView.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach ($framework in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
    if (!($config.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq $framework -and $_.version -eq $RuntimeVersion })) {
        throw "Published app does not contain $framework $RuntimeVersion."
    }
}
if ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publish 'MoaView.exe')).ProductVersion.Split('+')[0] -ne $Version) {
    throw 'Published application version does not match installer version.'
}

$packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
$licenseDir = Join-Path $publish 'Assets/Licenses'
New-Item -ItemType Directory -Path $licenseDir -Force | Out-Null
$bpgSource = Join-Path $toolsRoot 'libbpg-0.9.8.tar.gz'
if (!(Test-Path -LiteralPath $bpgSource)) {
    Invoke-WebRequest -Uri 'https://bellard.org/bpg/libbpg-0.9.8.tar.gz' -OutFile $bpgSource
}
if ((Get-FileHash -LiteralPath $bpgSource -Algorithm SHA256).Hash -ne 'E2E5518FB3AE0883B79E857C640AA15BFC3A4DAAE50DF1096A2CDF3E5A17FE1F') {
    throw 'libbpg source archive checksum mismatch.'
}
Copy-Item -LiteralPath $bpgSource -Destination $licenseDir
foreach ($package in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
    $packagePath = Join-Path $packageRoot "$package/$RuntimeVersion"
    foreach ($name in @('LICENSE.TXT', 'LICENSE', 'THIRD-PARTY-NOTICES.TXT')) {
        $source = Join-Path $packagePath $name
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $licenseDir "$package-$name") }
    }
}
foreach ($name in @('LICENSE.txt', 'NOTICE.txt')) {
    Copy-Item -LiteralPath (Join-Path $packageRoot "microsoft.web.webview2/1.0.4314.35/$name") -Destination (Join-Path $licenseDir "WebView2-$name")
}
Copy-Item -LiteralPath (Join-Path $repo 'docs/content-preview.md'),(Join-Path $repo 'docs/third-party-notices.md'),(Join-Path $repo 'docs/install.md') -Destination $publish
& $compiler "/DAppVersion=$Version" "/DPublishDir=$publish" "/DOutputDir=$output" "/DWebViewBootstrapper=$bootstrapper" @installerArguments (Join-Path $repo 'installer/MoaView.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$installer = Join-Path $output "MoaView-$Version-Setup-x64.exe"
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
"$hash  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath "$installer.sha256" -Encoding ascii
[pscustomobject]@{Installer=$installer;Bytes=(Get-Item -LiteralPath $installer).Length;SHA256=$hash;Runtime=$RuntimeVersion} | Format-List
