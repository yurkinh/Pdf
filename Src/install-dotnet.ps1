# Installs the SDK pinned in global.json into .\.dotnet (no system-wide changes).
# Usage: .\install-dotnet.ps1 [-Workloads maui,...]
param([string[]]$Workloads = @())
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$installDir = '.dotnet'
$version = (Get-Content -Path 'global.json' -Raw | ConvertFrom-Json).sdk.version
$installScript = Join-Path $env:TEMP "dotnet-install-$([guid]::NewGuid()).ps1"
try {
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installScript
    & $installScript -Version $version -InstallDir $installDir
    # Libraries and tests also target net10.0, so the local root needs that runtime to run them.
    & $installScript -Runtime dotnet -Channel 10.0 -InstallDir $installDir
}
finally {
    if (Test-Path -LiteralPath $installScript) { Remove-Item -LiteralPath $installScript -Force }
}
if ($Workloads.Count -gt 0) { & "$installDir\dotnet.exe" workload install @Workloads }
Write-Host "Done. SDK: $(& "$installDir\dotnet.exe" --version)"
