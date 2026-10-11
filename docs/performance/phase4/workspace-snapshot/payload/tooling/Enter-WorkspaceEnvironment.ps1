$taskRoot = Split-Path $PSScriptRoot -Parent
$env:DOTNET_ROOT = Join-Path $PSScriptRoot 'dotnet-10.0.401'
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot 'nuget-packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $PSScriptRoot 'nuget-http-cache'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $PSScriptRoot 'nuget-plugins-cache'
$env:NUGET_SCRATCH = Join-Path $PSScriptRoot 'nuget-scratch'
$env:TEMP = Join-Path $PSScriptRoot 'temp'
$env:TMP = $env:TEMP
@($env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $env:NUGET_HTTP_CACHE_PATH, $env:NUGET_PLUGINS_CACHE_PATH, $env:NUGET_SCRATCH, $env:TEMP) | ForEach-Object { New-Item -ItemType Directory -Path $_ -Force | Out-Null }
