#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Delivery = 'artifacts/development-layout/managed-release-phase3-verified',
    [string]$OutputReport = 'reports/phase3-build-provenance.json'
)
$ErrorActionPreference = 'Stop'
$phase3Root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
function Resolve-Phase3Path([string]$Path) {
    $phase3Path = [IO.Path]::GetFullPath($Path, $phase3Root)
    if (!$phase3Path.StartsWith($phase3Root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Path outside workspace: $Path" }
    $phase3Cursor = $phase3Path
    while ($phase3Cursor.Length -ge $phase3Root.Length) {
        if ((Test-Path -LiteralPath $phase3Cursor) -and ((Get-Item -LiteralPath $phase3Cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked evidence rejected: $Path" }
        if ($phase3Cursor -eq $phase3Root) { break }
        $phase3Cursor = Split-Path $phase3Cursor -Parent
    }
    return $phase3Path
}
$phase3ReportPath = Resolve-Phase3Path $OutputReport
if (Test-Path -LiteralPath $phase3ReportPath) { throw 'Report already exists; use a fresh output path to preserve evidence.' }
$phase3Repository = Resolve-Phase3Path 'source'
$phase3Start = (& git -C $phase3Repository rev-parse files-turbo-phase3-start)
if ($LASTEXITCODE -ne 0 -or $phase3Start -ne 'b18efc91ed3033081691f407a2bb7b5ee3d7d21d') { throw 'Phase III start reference is not the exact Phase II commit.' }
$phase3Tracked = @(& git -C $phase3Repository diff files-turbo-phase3-start --name-only -- src)
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate tracked source changes.' }
$phase3Untracked = @(& git -C $phase3Repository ls-files --others --exclude-standard -- src)
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate new source files.' }
$phase3Changes = @($phase3Tracked + $phase3Untracked | Sort-Object -Unique)
$phase3ChangedCs = @($phase3Changes | Where-Object { $_.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase) })
if (!$phase3ChangedCs.Count) { throw 'No changed production C# documents found.' }
foreach ($phase3Relative in $phase3Changes) {
    if (!(Test-Path -LiteralPath (Resolve-Phase3Path ('source/' + $phase3Relative)) -PathType Leaf)) { throw "Deleted source needs an explicit provenance policy: $phase3Relative" }
}
$phase3Projects = @('Files.App', 'Files.App.CsWin32', 'Files.App.Storage') + @($phase3ChangedCs | ForEach-Object { ($_ -split '/')[1] })
$phase3Documents = [Collections.Generic.List[object]]::new()
$phase3Assemblies = foreach ($phase3Project in @($phase3Projects | Sort-Object -Unique)) {
    $phase3AssemblyName = if ($phase3Project -eq 'Files.App') { 'Files' } else { $phase3Project }
    $phase3Framework = if ($phase3Project -eq 'Files.Shared') { 'net10.0' } else { 'net10.0-windows10.0.26100.0' }
    $phase3Directory = "source/src/$phase3Project/bin/x64/Release/$phase3Framework/win-x64"
    $phase3AssemblyPath = Resolve-Phase3Path "$phase3Directory/$phase3AssemblyName.dll"
    $phase3PdbPath = Resolve-Phase3Path "$phase3Directory/$phase3AssemblyName.pdb"
    $phase3LayoutRelative = if ($phase3Project -eq 'Files.App.Server') { "Files.App.Server/$phase3AssemblyName.dll" } else { "$phase3AssemblyName.dll" }
    $phase3LayoutPath = Resolve-Phase3Path "$Delivery/layout/$phase3LayoutRelative"
    $phase3AssemblyHash = (Get-FileHash -LiteralPath $phase3AssemblyPath).Hash
    if ($phase3AssemblyHash -ne (Get-FileHash -LiteralPath $phase3LayoutPath).Hash) { throw "Packaged assembly differs: $phase3AssemblyName" }
    $phase3PdbStream = [IO.File]::OpenRead($phase3PdbPath)
    $phase3Provider = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($phase3PdbStream)
    $phase3AssemblyStream = [IO.File]::OpenRead($phase3AssemblyPath)
    $phase3PeReader = [Reflection.PortableExecutable.PEReader]::new($phase3AssemblyStream)
    try {
        $phase3Reader = $phase3Provider.GetMetadataReader()
        $phase3DebugEntries = @($phase3PeReader.ReadDebugDirectory() | Where-Object Type -eq CodeView)
        if ($phase3DebugEntries.Count -ne 1) { throw "Missing unique CodeView PDB identity: $phase3AssemblyName" }
        $phase3CodeView = $phase3PeReader.ReadCodeViewDebugDirectoryData($phase3DebugEntries[0])
        $phase3PdbId = [byte[]]@($phase3Reader.DebugMetadataHeader.Id)
        $phase3PdbGuid = [Guid]::new([byte[]]$phase3PdbId[0..15])
        $phase3PdbStamp = [BitConverter]::ToUInt32($phase3PdbId, 16)
        if ($phase3CodeView.Guid -ne $phase3PdbGuid -or $phase3CodeView.Age -ne 1 -or $phase3DebugEntries[0].Stamp -ne $phase3PdbStamp) { throw "PDB does not belong to assembly: $phase3AssemblyName" }
        foreach ($phase3Handle in $phase3Reader.Documents) {
            $phase3Document = $phase3Reader.GetDocument($phase3Handle)
            $phase3DocumentName = $phase3Reader.GetString($phase3Document.Name)
            $phase3Relative = [IO.Path]::GetRelativePath($phase3Repository, $phase3DocumentName).Replace('\', '/')
            if ($phase3Relative -notin $phase3ChangedCs) { continue }
            $phase3SourcePath = Resolve-Phase3Path $phase3DocumentName
            if ($phase3Reader.GetGuid($phase3Document.HashAlgorithm) -ne [Guid]'8829d00f-11b8-4213-878b-770e8597ac16') { throw "Source checksum is not SHA256: $phase3Relative" }
            $phase3BuiltHash = [Convert]::ToHexString($phase3Reader.GetBlobBytes($phase3Document.Hash))
            if ($phase3BuiltHash -ne (Get-FileHash -LiteralPath $phase3SourcePath).Hash) { throw "Compiled source mismatch: $phase3Relative" }
            $phase3Documents.Add([pscustomobject]@{ Path = $phase3Relative; SHA256 = $phase3BuiltHash; Assembly = "$phase3AssemblyName.dll" })
        }
    } finally {
        $phase3PeReader.Dispose(); $phase3AssemblyStream.Dispose()
        $phase3Provider.Dispose(); $phase3PdbStream.Dispose()
    }
    [pscustomobject]@{
        Assembly = "$phase3AssemblyName.dll"
        LayoutPath = $phase3LayoutRelative
        SHA256 = $phase3AssemblyHash
        PdbPath = [IO.Path]::GetRelativePath($phase3Root, $phase3PdbPath)
        PdbSHA256 = (Get-FileHash -LiteralPath $phase3PdbPath).Hash
        PdbIdentityMatchesAssembly = $true
    }
}
$phase3DocumentPaths = @($phase3Documents | Select-Object -ExpandProperty Path -Unique)
$phase3Missing = @($phase3ChangedCs | Where-Object { $_ -notin $phase3DocumentPaths })
if ($phase3Missing.Count) { throw "Changed source missing from build PDB checksums: $($phase3Missing -join ', ')" }
$phase3Inputs = foreach ($phase3Relative in @($phase3Changes | Where-Object { $_.EndsWith('.xaml') -or $_.EndsWith('.appxmanifest') -or ($_ -like 'src/Files.App.CsWin32/*' -and $_.EndsWith('.txt')) })) {
    [pscustomobject]@{
        Path = $phase3Relative
        SHA256 = (Get-FileHash -LiteralPath (Resolve-Phase3Path ('source/' + $phase3Relative))).Hash
        CheckKind = 'source-input-hash-not-a-portable-PDB-document'
    }
}
$phase3SourceManifestPath = Resolve-Phase3Path 'source/src/Files.App/Package.appxmanifest'
$phase3SourceManifest = [xml][IO.File]::ReadAllText($phase3SourceManifestPath)
$phase3LayoutManifestPath = Resolve-Phase3Path ($Delivery + '/layout/AppxManifest.xml')
$phase3LayoutManifest = [xml][IO.File]::ReadAllText($phase3LayoutManifestPath)
foreach ($phase3Attribute in @('Name', 'Publisher', 'Version')) {
    if ($phase3SourceManifest.Package.Identity.GetAttribute($phase3Attribute) -ne $phase3LayoutManifest.Package.Identity.GetAttribute($phase3Attribute)) { throw "Source/package manifest identity differs: $phase3Attribute" }
}
$phase3Result = [ordered]@{
    VerifiedUtc = [DateTime]::UtcNow.ToString('o')
    Status = 'compiled-source-and-packaged-assembly-match'
    Scope = 'SHA256 Portable PDB checksums of every changed/new production C# file under src since Phase III start; PDB CodeView identity; packaged assembly equality. XAML, package manifest and CsWin32 declarations have separate input hashes. No runtime verdict.'
    PhaseIIIStartCommit = $phase3Start
    SourceCommit = (& git -C $phase3Repository rev-parse HEAD)
    SourceWorkingTreeDirty = (@(& git -C $phase3Repository status --porcelain).Count -gt 0)
    Delivery = [IO.Path]::GetRelativePath($phase3Root, (Resolve-Phase3Path $Delivery))
    ChangedCSharpCount = $phase3ChangedCs.Count
    Assemblies = @($phase3Assemblies)
    Documents = @($phase3Documents)
    SourceInputs = @($phase3Inputs)
    Manifest = @{ Path = 'src/Files.App/Package.appxmanifest'; SourceSHA256 = (Get-FileHash -LiteralPath $phase3SourceManifestPath).Hash; LayoutSHA256 = (Get-FileHash -LiteralPath $phase3LayoutManifestPath).Hash; Identity = $phase3LayoutManifest.Package.Identity.Name; Publisher = $phase3LayoutManifest.Package.Identity.Publisher; Version = $phase3LayoutManifest.Package.Identity.Version }
}
$phase3Json = $phase3Result | ConvertTo-Json -Depth 8
$phase3Output = [IO.FileStream]::new($phase3ReportPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
try {
    $phase3Bytes = [Text.UTF8Encoding]::new($false).GetBytes($phase3Json)
    $phase3Output.Write($phase3Bytes, 0, $phase3Bytes.Length)
} finally { $phase3Output.Dispose() }
Write-Output $phase3Result.Status
