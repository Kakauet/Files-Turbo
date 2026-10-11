param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '10.0.401')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$downloadRoot = Join-Path $PSScriptRoot 'downloads'
New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
$metadataPath = Join-Path $downloadRoot 'dotnet-10-releases.json'
Invoke-WebRequest 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json' -OutFile $metadataPath
$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$sdk = @($metadata.releases | ForEach-Object { $_.sdks } | Where-Object { $_.version -eq $Version })[0]
if (-not $sdk) { throw "SDK $Version not found in official metadata." }
$package = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
if ([Uri]$package.url -and ([Uri]$package.url).Host -ne 'builds.dotnet.microsoft.com') { throw 'Unexpected SDK download host.' }
$archive = Join-Path $downloadRoot "dotnet-sdk-$Version-win-x64.zip"
Invoke-WebRequest $package.url -OutFile $archive
$actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash
if ($actualHash -ne $package.hash) { throw 'SDK SHA512 verification failed.' }
$package | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $downloadRoot "sdk-$Version-verification.json")
$sdkRoot = Join-Path $PSScriptRoot "dotnet-$Version"
New-Item -ItemType Directory -Path $sdkRoot -Force | Out-Null
tar -xf $archive -C $sdkRoot
if ($LASTEXITCODE -ne 0) { throw 'SDK extraction failed.' }
& (Join-Path $sdkRoot 'dotnet.exe') --info
if ($LASTEXITCODE -ne 0) { throw 'SDK validation failed.' }
