# Creates Desktop shortcuts for the published FMR Vantage demo build.
#   FMR Vantage.lnk       -> normal start (splash, Home)
#   FMR Vantage Demo.lnk  -> opens the workspace and runs the project fleet right away
param([string]$Exe = (Join-Path $PSScriptRoot '..\dist\FMR-Vantage-0.1.0-win-x64\FMRVantage.exe'))

$exe = (Resolve-Path $Exe).Path
$desktop = [Environment]::GetFolderPath('Desktop')
$shell = New-Object -ComObject WScript.Shell

function New-Shortcut([string]$name, [string]$arguments, [string]$description) {
    $lnk = $shell.CreateShortcut((Join-Path $desktop "$name.lnk"))
    $lnk.TargetPath = $exe
    $lnk.Arguments = $arguments
    $lnk.WorkingDirectory = Split-Path $exe
    $lnk.IconLocation = "$exe,0"
    $lnk.Description = $description
    $lnk.Save()
    Write-Host "created  $(Join-Path $desktop "$name.lnk")"
}

New-Shortcut 'FMR Vantage' '' 'FMR Vantage - Fleet Management Robot'
New-Shortcut 'FMR Vantage Demo' '--page monitor --scenario PRJ --run' 'FMR Vantage - live demo of the project fleet'
