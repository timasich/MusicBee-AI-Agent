param(
    [string]$MusicBeePluginsPath = "C:\Program Files (x86)\MusicBee\Plugins"
)

$ErrorActionPreference = "Stop"

$source = Join-Path $PSScriptRoot "bin\x86\Release\MB_AI_Agent.dll"
if (-not (Test-Path $source)) {
    throw "Build output not found: $source"
}

if (-not (Test-Path $MusicBeePluginsPath)) {
    throw "MusicBee Plugins folder not found: $MusicBeePluginsPath"
}

Copy-Item -LiteralPath $source -Destination (Join-Path $MusicBeePluginsPath "MB_AI_Agent.dll") -Force
Write-Host "Installed MB_AI_Agent.dll to $MusicBeePluginsPath"
