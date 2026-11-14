# Installs the .NET 10 SDK for the current user in %USERPROFILE%\.dotnet (no admin)
# and registers DOTNET_ROOT + user PATH. Copies an existing SDK when one is found,
# otherwise downloads the official dotnet-install.ps1 from dot.net.
$ErrorActionPreference = 'Stop'
$target = Join-Path $env:USERPROFILE '.dotnet'
$sources = @(Get-ChildItem "$env:LOCALAPPDATA\Packages\Claude*\LocalCache\Local\Microsoft\dotnet" -Directory -ErrorAction SilentlyContinue)

if (Test-Path (Join-Path $target 'sdk\10.*')) {
    Write-Host "SDK 10 already present in $target"
}
elseif ($sources.Count -gt 0 -and (Test-Path (Join-Path $sources[0].FullName 'sdk'))) {
    Write-Host "Copying SDK from $($sources[0].FullName) to $target ..."
    New-Item -ItemType Directory -Force $target | Out-Null
    robocopy $sources[0].FullName $target /E /NFL /NDL /NJH /NJS /NP | Out-Null
}
else {
    Write-Host "Downloading official installer ..."
    $script = Join-Path $env:TEMP 'dotnet-install.ps1'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $script -UseBasicParsing
    & powershell -NoProfile -ExecutionPolicy Bypass -File $script -Channel 10.0 -InstallDir $target -NoPath
}

# User environment: DOTNET_ROOT and PATH (own entry first, stale entry removed).
$stale = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$parts = @(([Environment]::GetEnvironmentVariable('Path', 'User') -split ';') | Where-Object { $_ -and $_ -ne $target -and $_ -ne $stale })
[Environment]::SetEnvironmentVariable('Path', (@($target) + $parts) -join ';', 'User')
[Environment]::SetEnvironmentVariable('DOTNET_ROOT', $target, 'User')

# Current session too.
$env:DOTNET_ROOT = $target
$env:Path = "$target;$env:Path"

Write-Host ""
& (Join-Path $target 'dotnet.exe') --list-sdks
Write-Host ""
Write-Host "Done. Open a NEW terminal. Use:  & `"$target\dotnet.exe`" build Vantage.slnx"
if ((Get-Command dotnet).Source -notlike "$target*") {
    Write-Host "Note: 'dotnet' alone still resolves to C:\Program Files\dotnet (system PATH comes first)."
    Write-Host "      Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0 to make plain 'dotnet' work everywhere."
}
