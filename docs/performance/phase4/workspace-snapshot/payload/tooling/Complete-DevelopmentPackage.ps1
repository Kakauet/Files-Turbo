#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InputPackage,
    [string]$ServerOutput,
    [string]$Destination,
    [string]$MakeAppx,
    [string[]]$DependencyDirs = @(),
    [ValidateRange(1, 4)][int]$MaxExtractedGiB = 2,
    [ValidateRange(100, 100000)][int]$MaxEntries = 30000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$allowedDestination = Join-Path $workspace 'artifacts\development-layout'
if (!$ServerOutput) { $ServerOutput = Join-Path $workspace 'source\src\Files.App.Server\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64' }
if (!$MakeAppx) { $MakeAppx = Join-Path $workspace 'tooling\nuget-packages\microsoft.windows.sdk.buildtools\10.0.28000.2705\bin\10.0.28000.0\x64\makeappx.exe' }
if (!$Destination) { $Destination = Join-Path $allowedDestination ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)) }

function Assert-ConfinedPath([string]$Path, [string]$Root = $workspace) {
    $full = [IO.Path]::GetFullPath($Path)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    if (!$full.Equals($rootFull, [StringComparison]::OrdinalIgnoreCase) -and !$full.StartsWith($rootFull + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Path escapes its permitted root: $Path" }
    $cursor = $full
    while ($cursor -and ($cursor.Length -ge $workspace.Length)) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse point rejected: $cursor" }
        }
        if ($cursor.Equals($workspace, [StringComparison]::OrdinalIgnoreCase)) { break }
        $cursor = Split-Path -Parent $cursor
    }
    return $full
}

function Get-SafeRelativePath([string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Name) -or $Name.StartsWith('/') -or $Name.StartsWith('\') -or $Name.Contains(':')) { throw "Invalid package path: $Name" }
    $relative = $Name.Replace('/', '\').TrimEnd('\')
    foreach ($part in $relative.Split('\')) {
        if (!$part -or $part -eq '.' -or $part -eq '..' -or $part -match '[<>"|?*\x00-\x1f]' -or $part -match '[. ]$' -or $part -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)') { throw "Unsafe package path: $Name" }
    }
    return $relative
}

function Get-SafeFiles([string]$Root) {
    $queue = [Collections.Generic.Queue[string]]::new()
    $queue.Enqueue((Assert-ConfinedPath $Root))
    while ($queue.Count) {
        $current = $queue.Dequeue()
        foreach ($item in Get-ChildItem -LiteralPath $current -Force) {
            $null = Assert-ConfinedPath $item.FullName $Root
            if ($item.PSIsContainer) { $queue.Enqueue($item.FullName) } else { $item }
        }
    }
}

function Get-StreamHash([IO.Stream]$Stream) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [Convert]::ToHexString($sha.ComputeHash($Stream)) } finally { $sha.Dispose() }
}

function Get-FileHashValue([string]$Path) {
    $null = Assert-ConfinedPath $Path
    $stream = [IO.File]::OpenRead($Path)
    try { return Get-StreamHash $stream } finally { $stream.Dispose() }
}

function Get-PeInfo([string]$Path) {
    $stream = [IO.File]::OpenRead((Assert-ConfinedPath $Path))
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) { throw "Not a PE binary: $Path" }
        $stream.Position = 0x3C
        $pe = $reader.ReadUInt32()
        if ($pe -gt ($stream.Length - 24)) { throw "Invalid PE header: $Path" }
        $stream.Position = $pe
        if ($reader.ReadUInt32() -ne 0x4550) { throw "Invalid PE signature: $Path" }
        $machine = $reader.ReadUInt16()
        $sections = $reader.ReadUInt16()
        $stream.Position = $pe + 20
        $optionalSize = $reader.ReadUInt16()
        $optional = $pe + 24
        $stream.Position = $optional
        $magic = $reader.ReadUInt16()
        $directories = if ($magic -eq 0x20B) { 112 } elseif ($magic -eq 0x10B) { 96 } else { throw "Unknown PE optional header: $Path" }
        $managed = $false
        $anyCpu = $false
        if ($optionalSize -ge ($directories + 15 * 8)) {
            $stream.Position = $optional + $directories + 14 * 8
            $clrRva = $reader.ReadUInt32()
            if ($clrRva) {
                $managed = $true
                for ($i = 0; $i -lt $sections; $i++) {
                    $stream.Position = $optional + $optionalSize + $i * 40 + 8
                    $virtualSize = $reader.ReadUInt32()
                    $virtualAddress = $reader.ReadUInt32()
                    $rawSize = $reader.ReadUInt32()
                    $rawPointer = $reader.ReadUInt32()
                    if ($clrRva -ge $virtualAddress -and $clrRva -lt ($virtualAddress + [Math]::Max($virtualSize, $rawSize))) {
                        $flagOffset = [long]$rawPointer + $clrRva - $virtualAddress + 16
                        if ($flagOffset -gt ($stream.Length - 4)) { throw "Invalid CLR header: $Path" }
                        $stream.Position = $flagOffset
                        $flags = $reader.ReadUInt32()
                        $anyCpu = ($machine -eq 0x14C -and ($flags -band 1) -ne 0 -and ($flags -band 2) -eq 0)
                        break
                    }
                }
            }
        }
        $architecture = switch ($machine) { 0x8664 { 'x64' } 0x14C { if ($anyCpu) { 'AnyCPU' } else { 'x86' } } 0xAA64 { 'arm64' } default { 'unknown' } }
        return [pscustomobject]@{ Architecture = $architecture; Machine = ('0x{0:X4}' -f $machine); Managed = $managed }
    } finally { $reader.Dispose(); $stream.Dispose() }
}

$InputPackage = Assert-ConfinedPath $InputPackage
$ServerOutput = Assert-ConfinedPath $ServerOutput
$MakeAppx = Assert-ConfinedPath $MakeAppx
$Destination = Assert-ConfinedPath $Destination $allowedDestination
if ($Destination.Equals($allowedDestination, [StringComparison]::OrdinalIgnoreCase)) { throw 'Destination must be a new child directory of artifacts/development-layout.' }
if ([IO.Path]::GetExtension($InputPackage) -ne '.msix' -or !(Test-Path -LiteralPath $InputPackage -PathType Leaf)) { throw 'Input must be an existing MSIX in the workspace.' }
if (!(Test-Path -LiteralPath $ServerOutput -PathType Container)) { throw 'Server output directory is missing.' }
if (Test-Path -LiteralPath $Destination) { throw 'Destination already exists. Choose a fresh directory; this script never deletes or reuses a previous delivery.' }
$signature = Get-AuthenticodeSignature -LiteralPath $MakeAppx
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Microsoft Corporation(,|$)') { throw 'MakeAppx must have a valid Microsoft Authenticode signature.' }
$serverFiles = @(Get-SafeFiles $ServerOutput)
if ($serverFiles.Count -gt $MaxEntries) { throw 'Server output exceeds the entry limit.' }
$serverBytes = [long]($serverFiles | Measure-Object -Property Length -Sum).Sum
$dependencySources = [Collections.Generic.List[object]]::new()
$dependencyBytes = 0L
foreach ($dependencyDir in $DependencyDirs) {
    $dependencyRoot = Assert-ConfinedPath $dependencyDir
    if (!(Test-Path -LiteralPath $dependencyRoot -PathType Container)) { throw "Dependency directory is missing: $dependencyRoot" }
    foreach ($file in Get-SafeFiles $dependencyRoot) {
        $relative = Get-SafeRelativePath ([IO.Path]::GetRelativePath($dependencyRoot, $file.FullName))
        $dependencySources.Add([pscustomobject]@{ Source = $file.FullName; Relative = $relative; Length = $file.Length })
        $dependencyBytes += $file.Length
        if ($dependencySources.Count -gt $MaxEntries) { throw 'Dependencies exceed the entry limit.' }
    }
}
$limit = [long]$MaxExtractedGiB * 1GB
if (($serverBytes + $dependencyBytes) -ge $limit) { throw 'Server and dependencies exceed the size limit.' }
foreach ($required in @('Files.App.Server.exe', 'Files.App.Server.dll', 'Files.App.Server.deps.json', 'WinRT.Host.dll', 'WinRT.Host.runtimeconfig.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $ServerOutput $required) -PathType Leaf)) { throw "Missing server output: $required" }
}

$layout = Join-Path $Destination 'layout'
$null = [IO.Directory]::CreateDirectory((Assert-ConfinedPath $layout $allowedDestination))
$reportPath = Join-Path $Destination 'package-validation.json'
$state = [ordered]@{ Status = 'incomplete'; InputPackage = $InputPackage; InputSHA256 = (Get-FileHashValue $InputPackage); ServerOutput = $ServerOutput; Layout = $layout; MakeAppx = $MakeAppx; ManifestUnchanged = $false; Signed = $false; UiTested = $false }
try {
    $archive = [IO.Compression.ZipFile]::OpenRead($InputPackage)
    try {
        if ($archive.Entries.Count -gt $MaxEntries) { throw 'Package exceeds the entry limit.' }
        $total = $serverBytes + $dependencyBytes
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $archive.Entries) {
            $relative = Get-SafeRelativePath $entry.FullName
            if (!$names.Add($relative)) { throw "Duplicate package entry: $relative" }
            if ($relative -ieq 'AppxSignature.p7x') { throw 'Signed input rejected. Supply the original unsigned development MSIX.' }
            if ((($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000 -or ($entry.ExternalAttributes -band 0x400) -ne 0) { throw "Linked package entry rejected: $relative" }
            $total += $entry.Length
            if ($total -gt $limit) { throw 'Package plus server exceeds the extracted size limit.' }
        }
        $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($workspace))
        if ($drive.AvailableFreeSpace -lt (2 * $total + 1GB)) { throw 'Insufficient free space for layout, new package and 1 GiB reserve.' }
        foreach ($entry in $archive.Entries) {
            $relative = Get-SafeRelativePath $entry.FullName
            $target = Assert-ConfinedPath (Join-Path $layout $relative) $layout
            if ($entry.FullName.EndsWith('/') -or $entry.FullName.EndsWith('\')) { $null = [IO.Directory]::CreateDirectory($target); continue }
            $null = [IO.Directory]::CreateDirectory((Assert-ConfinedPath (Split-Path -Parent $target) $layout))
            $inputStream = $entry.Open()
            $outputStream = [IO.File]::Open($target, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try {
                $buffer = [byte[]]::new(65536)
                $copied = 0L
                while (($read = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    $copied += $read
                    if ($copied -gt $entry.Length) { throw "ZIP entry exceeded its declared size: $relative" }
                    $outputStream.Write($buffer, 0, $read)
                }
                if ($copied -ne $entry.Length) { throw "ZIP entry length mismatch: $relative" }
            } finally { $outputStream.Dispose(); $inputStream.Dispose() }
        }
    } finally { $archive.Dispose() }
    $manifestPath = Join-Path $layout 'AppxManifest.xml'
    $manifestHash = Get-FileHashValue $manifestPath
    $xmlSettings = [Xml.XmlReaderSettings]::new()
    $xmlSettings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $xmlSettings.XmlResolver = $null
    $xmlReader = [Xml.XmlReader]::Create($manifestPath, $xmlSettings)
    $manifest = [Xml.XmlDocument]::new()
    $manifest.XmlResolver = $null
    try { $manifest.Load($xmlReader) } finally { $xmlReader.Dispose() }
    $identity = $manifest.SelectSingleNode('/*[local-name()="Package"]/*[local-name()="Identity"]')
    if (!$identity -or $identity.GetAttribute('Name') -ne 'FilesTurbo.Experimental' -or $identity.GetAttribute('ProcessorArchitecture') -ne 'x64') { throw 'Only the FilesTurbo.Experimental x64 package can be completed.' }
    $protocols = @($manifest.SelectNodes('//*[local-name()="Protocol"]') | ForEach-Object { $_.GetAttribute('Name') })
    $aliases = @($manifest.SelectNodes('//*[local-name()="ExecutionAlias"]') | ForEach-Object { $_.GetAttribute('Alias') })
    if ($protocols.Count -ne 1 -or $protocols[0] -ne 'files-turbo' -or $aliases.Count -ne 1 -or $aliases[0] -ne 'files-turbo.exe') { throw 'Unexpected protocol or execution alias.' }
    foreach ($metadata in @('AppxBlockMap.xml', '[Content_Types].xml', 'AppxSignature.p7x')) {
        $metadataPath = Assert-ConfinedPath (Join-Path $layout $metadata) $layout
        if (Test-Path -LiteralPath $metadataPath -PathType Leaf) { Remove-Item -LiteralPath $metadataPath }
    }
    $serverDestination = Join-Path $layout 'Files.App.Server'
    $null = [IO.Directory]::CreateDirectory((Assert-ConfinedPath $serverDestination $layout))
    foreach ($file in $serverFiles) {
        $relative = Get-SafeRelativePath ([IO.Path]::GetRelativePath($ServerOutput, $file.FullName))
        $target = Assert-ConfinedPath (Join-Path $serverDestination $relative) $serverDestination
        $null = [IO.Directory]::CreateDirectory((Assert-ConfinedPath (Split-Path -Parent $target) $serverDestination))
        if (Test-Path -LiteralPath $target) {
            if ((Get-FileHashValue $target) -ne (Get-FileHashValue $file.FullName)) { throw "Existing server file differs; refuse to overwrite: $relative" }
        } else { [IO.File]::Copy((Assert-ConfinedPath $file.FullName $ServerOutput), $target, $false) }
    }
    $declarations = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($node in $manifest.SelectNodes('//*[@Executable or @Path]')) {
        foreach ($attribute in @('Executable', 'Path')) {
            if ($node.HasAttribute($attribute)) { $null = $declarations.Add((Get-SafeRelativePath ($node.GetAttribute($attribute)))) }
        }
    }
    foreach ($node in $manifest.SelectNodes('//*[local-name()="InProcessServer" or local-name()="OutOfProcessServer"]/*[local-name()="Path"]')) { $null = $declarations.Add((Get-SafeRelativePath $node.InnerText)) }
    foreach ($relative in $declarations) {
        if (!(Test-Path -LiteralPath (Assert-ConfinedPath (Join-Path $layout $relative) $layout) -PathType Leaf)) { throw "Missing manifest executable/server path: $relative" }
    }
    $requiredArchitectures = [ordered]@{
        'Files.exe' = 'x64'; 'Files.App.Server\Files.App.Server.exe' = 'x64';
        'Assets\FilesOpenDialog\Files.App.Launcher.exe' = 'x64';
        'Assets\FilesOpenDialog\Files.App.OpenDialog64.dll' = 'x64'; 'Assets\FilesOpenDialog\Files.App.SaveDialog64.dll' = 'x64';
        'Assets\FilesOpenDialog\Files.App.OpenDialog32.dll' = 'x86'; 'Assets\FilesOpenDialog\Files.App.SaveDialog32.dll' = 'x86'
    }
    foreach ($relative in $requiredArchitectures.Keys) {
        $path = Assert-ConfinedPath (Join-Path $layout $relative) $layout
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing native payload: $relative" }
        if ((Get-PeInfo $path).Architecture -ne $requiredArchitectures[$relative]) { throw "Wrong PE architecture: $relative" }
    }
    $inventory = [Collections.Generic.List[object]]::new()
    foreach ($file in Get-SafeFiles $layout) {
        $relative = [IO.Path]::GetRelativePath($layout, $file.FullName).Replace('\', '/')
        $peInfo = if ($file.Extension -in @('.exe', '.dll')) { Get-PeInfo $file.FullName } else { $null }
        if ($relative.StartsWith('Files.App.Server/') -and $peInfo -and $peInfo.Architecture -notin @('x64', 'AnyCPU')) { throw "Incompatible server architecture: $relative" }
        $inventory.Add([pscustomobject]@{ Path = $relative; Length = $file.Length; SHA256 = (Get-FileHashValue $file.FullName); PE = $peInfo })
    }
    $state['Identity'] = $identity.OuterXml
    $state['Dependencies'] = @($manifest.SelectNodes('//*[local-name()="Dependencies"]/*') | ForEach-Object { $_.OuterXml })
    $state['DeclaredPaths'] = @($declarations | ForEach-Object { $_ })
    $state['Inventory'] = $inventory.ToArray()
    $state['ManifestSHA256'] = $manifestHash
    $outputPackage = Assert-ConfinedPath (Join-Path $Destination ('FilesTurbo.Experimental_' + $identity.GetAttribute('Version') + '_x64_Development_unsigned.msix')) $Destination
    $log = Join-Path $Destination 'makeappx-pack.log'
    & $MakeAppx pack /d $layout /p $outputPackage /h SHA256 /l /no *> $log
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed with exit code $LASTEXITCODE. See $log" }
    $packed = [IO.Compression.ZipFile]::OpenRead($outputPackage)
    try {
        foreach ($item in $inventory) {
            $entry = $packed.GetEntry($item.Path)
            if (!$entry -or $entry.Length -ne $item.Length) { throw "Packaged entry missing or changed: $($item.Path)" }
            $stream = $entry.Open()
            try { if ((Get-StreamHash $stream) -ne $item.SHA256) { throw "Packaged entry hash differs: $($item.Path)" } } finally { $stream.Dispose() }
        }
        if (!$packed.GetEntry('AppxBlockMap.xml') -or !$packed.GetEntry('[Content_Types].xml') -or $packed.GetEntry('AppxSignature.p7x')) { throw 'Output must contain regenerated package metadata and remain unsigned.' }
    } finally { $packed.Dispose() }
    $dependencyInventory = [Collections.Generic.List[object]]::new()
    $dependencyDestination = Join-Path $Destination 'Dependencies'
    foreach ($item in $dependencySources) {
        $target = Assert-ConfinedPath (Join-Path $dependencyDestination $item.Relative) $dependencyDestination
        $null = [IO.Directory]::CreateDirectory((Assert-ConfinedPath (Split-Path -Parent $target) $dependencyDestination))
        $sourceHash = Get-FileHashValue $item.Source
        if (Test-Path -LiteralPath $target) {
            if ((Get-FileHashValue $target) -ne $sourceHash) { throw "Conflicting dependency file: $($item.Relative)" }
        } else { [IO.File]::Copy((Assert-ConfinedPath $item.Source), $target, $false) }
        if ((Get-FileHashValue $target) -ne $sourceHash) { throw "Dependency hash mismatch: $($item.Relative)" }
        $dependencyInventory.Add([pscustomobject]@{ Source = $item.Source; Path = $item.Relative.Replace('\', '/'); Length = $item.Length; SHA256 = $sourceHash })
    }
    $state['DependencyInventory'] = $dependencyInventory.ToArray()
    if ((Get-FileHashValue $manifestPath) -ne $manifestHash) { throw 'Manifest changed during completion.' }
    $state['ManifestUnchanged'] = $true
    $state['OutputPackage'] = $outputPackage
    $state['OutputSHA256'] = Get-FileHashValue $outputPackage
    $state['Status'] = 'content-validated-not-installed'
    $state['CompletedUtc'] = [DateTime]::UtcNow.ToString('o')
    Write-Output $outputPackage
    Write-Output $reportPath
} catch {
    $state['Error'] = $_.Exception.Message
    throw
} finally {
    [IO.File]::WriteAllText((Assert-ConfinedPath $reportPath $Destination), ($state | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}
