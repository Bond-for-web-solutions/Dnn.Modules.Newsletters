param(
    [string]$Configuration = "Release",
    [string]$Project = "Dnn.Modules.Newsletters.sln"
)

$ErrorActionPreference = "Stop"

# Find MSBuild via vswhere (covers Community/Pro/Enterprise/BuildTools, any version)
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    Write-Host "vswhere.exe not found. Install Visual Studio or Build Tools." -ForegroundColor Red
    exit 1
}

$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
    -find "MSBuild\**\Bin\MSBuild.exe" 2>$null | Select-Object -First 1

if (-not $msbuild) {
    Write-Host "MSBuild not found via vswhere." -ForegroundColor Red
    exit 1
}

$root = (Get-Item $PSScriptRoot).Parent.FullName
$buildFile = Join-Path $root $Project

Write-Host "`nBuilding $Project ($Configuration)..." -ForegroundColor Cyan
Write-Host "  MSBuild: $msbuild" -ForegroundColor DarkGray
Write-Host "  File:    $buildFile" -ForegroundColor DarkGray
Write-Host ""

# Restore NuGet packages (works for packages.config and PackageReference).
& $msbuild $buildFile -t:Restore -p:RestorePackagesConfig=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $msbuild $buildFile "-p:Configuration=$Configuration"
exit $LASTEXITCODE
