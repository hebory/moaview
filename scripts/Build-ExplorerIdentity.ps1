[CmdletBinding(DefaultParameterSetName = 'Development')]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.1.3',
    [Parameter(ParameterSetName = 'Development', Mandatory)][switch]$DevelopmentCertificate,
    [Parameter(ParameterSetName = 'Trusted', Mandatory)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$SigningThumbprint
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = Join-Path $repo "artifacts/explorer-identity/$Version"
$layout = Join-Path $output 'package-layout'
$pf = [Environment]::GetFolderPath('ProgramFilesX86')
$vswhere = Join-Path $pf 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'Visual Studio C++ x64 build tools are required.' }
$msbuild = Join-Path $vs 'MSBuild/Current/Bin/MSBuild.exe'
$sdkBin = Join-Path $pf 'Windows Kits/10/bin'
$sdk = Get-ChildItem -LiteralPath $sdkBin -Directory | Where-Object {
    $_.Name -match '^10\.\d+\.\d+\.\d+$' -and (Test-Path (Join-Path $_.FullName 'x64/makeappx.exe')) -and (Test-Path (Join-Path $_.FullName 'x64/signtool.exe'))
} | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$sdk) { throw 'Windows SDK MakeAppx and SignTool are required.' }
$makeappx = Join-Path $sdk.FullName 'x64/makeappx.exe'
$signtool = Join-Path $sdk.FullName 'x64/signtool.exe'

New-Item -ItemType Directory -Path $output, (Join-Path $layout 'Assets') -Force | Out-Null
& $msbuild (Join-Path $repo 'src/MoaView.ExplorerCommand/MoaView.ExplorerCommand.vcxproj') /p:Configuration=Release /p:Platform=x64 "/p:OutDir=$output/" "/p:IntDir=$output/obj/" /m:1 /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Explorer command build failed.' }

Add-Type -AssemblyName System.Drawing
$icon = [Drawing.Image]::FromFile((Join-Path $repo 'src/MoaView/Assets/Brand/MoaView.png'))
try {
    foreach ($logo in @(@{Name='StoreLogo';Size=50}, @{Name='Square150x150Logo';Size=150}, @{Name='Square44x44Logo';Size=44})) {
        $image = [Drawing.Bitmap]::new($icon, $logo.Size, $logo.Size)
        try { $image.Save((Join-Path $layout "Assets/$($logo.Name).png"), [Drawing.Imaging.ImageFormat]::Png) }
        finally { $image.Dispose() }
    }
} finally { $icon.Dispose() }

$certificate = $null
$rsa = $null
$pfxPath = $null
try {
    if ($DevelopmentCertificate) {
        $rsa = [Security.Cryptography.RSA]::Create(3072)
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=MoaView Development', $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $eku = [Security.Cryptography.OidCollection]::new()
        [void]$eku.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($eku, $true))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
            [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $true))
        $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddDays(90))
        $password = [Guid]::NewGuid().ToString('N')
        $pfxPath = Join-Path $output ([Guid]::NewGuid().ToString('N') + '.pfx')
        [IO.File]::WriteAllBytes($pfxPath, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $password))
        $signingArguments = @('/f', $pfxPath, '/p', $password)
    } else {
        $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$SigningThumbprint"
        if (!$certificate.HasPrivateKey) { throw 'The signing certificate has no accessible private key.' }
        $signingArguments = @('/sha1', $SigningThumbprint)
    }

    [xml]$manifest = Get-Content -LiteralPath (Join-Path $repo 'installer/ExplorerIdentity/AppxManifest.xml') -Raw
    $identity = $manifest.DocumentElement.SelectSingleNode("*[local-name()='Identity']")
    $identity.SetAttribute('Publisher', $certificate.Subject)
    $identity.SetAttribute('Version', "$Version.0")
    $manifest.Save((Join-Path $layout 'AppxManifest.xml'))
    $package = Join-Path $output 'MoaView.ExplorerIdentity.msix'
    & $makeappx pack /o /nv /d $layout /p $package
    if ($LASTEXITCODE -ne 0) { throw 'Explorer identity package build failed.' }
    & $signtool sign /fd SHA256 @signingArguments $package
    if ($LASTEXITCODE -ne 0) { throw 'Explorer identity package signing failed.' }

    # Embed the matching identity in the externally installed desktop executable.
    [xml]$desktopManifest = @'
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="MoaView" />
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
    <security><requestedPrivileges><requestedExecutionLevel level="asInvoker" uiAccess="false" /></requestedPrivileges></security>
  </trustInfo>
  <msix xmlns="urn:schemas-microsoft-com:msix.v1" packageName="MoaView.ExplorerIntegration" applicationId="MoaView" />
</assembly>
'@
    $desktopManifest.DocumentElement.SelectSingleNode("*[local-name()='msix']").SetAttribute('publisher', $certificate.Subject)
    $desktopManifest.Save((Join-Path $output 'MoaView.manifest'))
    if ($DevelopmentCertificate) {
        [IO.File]::WriteAllBytes((Join-Path $output 'MoaView-Development.cer'), $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    }
    [pscustomobject]@{ Package=$package; Publisher=$certificate.Subject; Thumbprint=$certificate.Thumbprint; Expires=$certificate.NotAfter } | Format-List
} finally {
    if ($pfxPath -and (Test-Path -LiteralPath $pfxPath)) { Remove-Item -LiteralPath $pfxPath -Force }
    if ($certificate) { $certificate.Dispose() }
    if ($rsa) { $rsa.Dispose() }
}
