param([switch]$Remove)
$ErrorActionPreference = 'Stop'
$packageName = 'MoaView.ExplorerIntegration'

try {
    if ($Remove) {
        Get-AppxPackage -Name $packageName | ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName }
    } else {
        $package = Join-Path $PSScriptRoot 'MoaView.ExplorerIdentity.msix'
        Add-AppxPackage -Path $package -ExternalLocation $PSScriptRoot
        if (!(Get-AppxPackage -Name $packageName)) { throw 'Explorer identity package is missing after registration.' }
    }
    exit 0
} catch {
    [Console]::Error.WriteLine($_.ToString())
    exit 1
}
