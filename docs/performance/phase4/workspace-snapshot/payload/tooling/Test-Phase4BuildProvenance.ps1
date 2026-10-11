#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Delivery = 'artifacts/development-layout/managed-release-phase4-final07',
    [string]$OutputReport = 'reports/phase4-build-provenance-firstvisit.json'
)
$ErrorActionPreference = 'Stop'
$phase4Root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
function Resolve-Phase4Path([string]$Path) {
    $phase4Path = [IO.Path]::GetFullPath($Path, $phase4Root)
    if (!$phase4Path.StartsWith($phase4Root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Path outside workspace: $Path" }
    $phase4Cursor = $phase4Path
    while ($phase4Cursor.Length -ge $phase4Root.Length) {
        if ((Test-Path -LiteralPath $phase4Cursor) -and ((Get-Item -LiteralPath $phase4Cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked evidence rejected: $Path" }
        if ($phase4Cursor -eq $phase4Root) { break }
        $phase4Cursor = Split-Path $phase4Cursor -Parent
    }
    return $phase4Path
}
$phase4ReportPath = Resolve-Phase4Path $OutputReport
if (Test-Path -LiteralPath $phase4ReportPath) { throw 'Report already exists; use a fresh output path to preserve evidence.' }
$phase4Repository = Resolve-Phase4Path 'source'
$phase4Start = (& git -C $phase4Repository rev-parse 'files-turbo-phase4-start^{commit}')
if ($LASTEXITCODE -ne 0 -or $phase4Start -ne '1b66432ad3baf122aa4e608b67ea47a5401a78ed') { throw 'Phase IV start reference is not the exact Phase III commit.' }
$phase4Tracked = @(& git -C $phase4Repository diff files-turbo-phase4-start --name-only -- src)
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate tracked source changes.' }
$phase4Untracked = @(& git -C $phase4Repository ls-files --others --exclude-standard -- src)
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate new source files.' }
$phase4Changes = @($phase4Tracked + $phase4Untracked | Sort-Object -Unique)
$phase4ChangedCs = @($phase4Changes | Where-Object { $_.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase) })
if (!$phase4ChangedCs.Count) { throw 'No changed production C# documents found.' }
foreach ($phase4Relative in $phase4Changes) {
    if (!(Test-Path -LiteralPath (Resolve-Phase4Path ('source/' + $phase4Relative)) -PathType Leaf)) { throw "Deleted source needs an explicit provenance policy: $phase4Relative" }
}
$phase4Projects = @('Files.App', 'Files.App.CsWin32', 'Files.App.Storage') + @($phase4ChangedCs | ForEach-Object { ($_ -split '/')[1] })
$phase4Documents = [Collections.Generic.List[object]]::new()
$phase4Assemblies = foreach ($phase4Project in @($phase4Projects | Sort-Object -Unique)) {
    $phase4AssemblyName = if ($phase4Project -eq 'Files.App') { 'Files' } else { $phase4Project }
    $phase4Framework = if ($phase4Project -eq 'Files.Shared') { 'net10.0' } else { 'net10.0-windows10.0.26100.0' }
    $phase4Directory = "source/src/$phase4Project/bin/x64/Release/$phase4Framework/win-x64"
    $phase4AssemblyPath = Resolve-Phase4Path "$phase4Directory/$phase4AssemblyName.dll"
    $phase4PdbPath = Resolve-Phase4Path "$phase4Directory/$phase4AssemblyName.pdb"
    $phase4LayoutRelative = if ($phase4Project -eq 'Files.App.Server') { "Files.App.Server/$phase4AssemblyName.dll" } else { "$phase4AssemblyName.dll" }
    $phase4LayoutPath = Resolve-Phase4Path "$Delivery/layout/$phase4LayoutRelative"
    $phase4AssemblyHash = (Get-FileHash -LiteralPath $phase4AssemblyPath).Hash
    if ($phase4AssemblyHash -ne (Get-FileHash -LiteralPath $phase4LayoutPath).Hash) { throw "Packaged assembly differs: $phase4AssemblyName" }
    $phase4PdbStream = [IO.File]::OpenRead($phase4PdbPath)
    $phase4Provider = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($phase4PdbStream)
    $phase4AssemblyStream = [IO.File]::OpenRead($phase4AssemblyPath)
    $phase4PeReader = [Reflection.PortableExecutable.PEReader]::new($phase4AssemblyStream)
    try {
        $phase4Reader = $phase4Provider.GetMetadataReader()
        $phase4DebugEntries = @($phase4PeReader.ReadDebugDirectory() | Where-Object Type -eq CodeView)
        if ($phase4DebugEntries.Count -ne 1) { throw "Missing unique CodeView PDB identity: $phase4AssemblyName" }
        $phase4CodeView = $phase4PeReader.ReadCodeViewDebugDirectoryData($phase4DebugEntries[0])
        $phase4PdbId = [byte[]]@($phase4Reader.DebugMetadataHeader.Id)
        $phase4PdbGuid = [Guid]::new([byte[]]$phase4PdbId[0..15])
        $phase4PdbStamp = [BitConverter]::ToUInt32($phase4PdbId, 16)
        if ($phase4CodeView.Guid -ne $phase4PdbGuid -or $phase4CodeView.Age -ne 1 -or $phase4DebugEntries[0].Stamp -ne $phase4PdbStamp) { throw "PDB does not belong to assembly: $phase4AssemblyName" }
        foreach ($phase4Handle in $phase4Reader.Documents) {
            $phase4Document = $phase4Reader.GetDocument($phase4Handle)
            $phase4DocumentName = $phase4Reader.GetString($phase4Document.Name)
            $phase4Relative = [IO.Path]::GetRelativePath($phase4Repository, $phase4DocumentName).Replace('\', '/')
            if ($phase4Relative -notin $phase4ChangedCs) { continue }
            $phase4SourcePath = Resolve-Phase4Path $phase4DocumentName
            if ($phase4Reader.GetGuid($phase4Document.HashAlgorithm) -ne [Guid]'8829d00f-11b8-4213-878b-770e8597ac16') { throw "Source checksum is not SHA256: $phase4Relative" }
            $phase4BuiltHash = [Convert]::ToHexString($phase4Reader.GetBlobBytes($phase4Document.Hash))
            if ($phase4BuiltHash -ne (Get-FileHash -LiteralPath $phase4SourcePath).Hash) { throw "Compiled source mismatch: $phase4Relative" }
            $phase4Documents.Add([pscustomobject]@{ Path = $phase4Relative; SHA256 = $phase4BuiltHash; Assembly = "$phase4AssemblyName.dll" })
        }
    } finally {
        $phase4PeReader.Dispose(); $phase4AssemblyStream.Dispose()
        $phase4Provider.Dispose(); $phase4PdbStream.Dispose()
    }
    [pscustomobject]@{
        Assembly = "$phase4AssemblyName.dll"
        LayoutPath = $phase4LayoutRelative
        SHA256 = $phase4AssemblyHash
        PdbPath = [IO.Path]::GetRelativePath($phase4Root, $phase4PdbPath)
        PdbSHA256 = (Get-FileHash -LiteralPath $phase4PdbPath).Hash
        PdbIdentityMatchesAssembly = $true
    }
}
$phase4DocumentPaths = @($phase4Documents | Select-Object -ExpandProperty Path -Unique)
$phase4Missing = @($phase4ChangedCs | Where-Object { $_ -notin $phase4DocumentPaths })
if ($phase4Missing.Count) { throw "Changed source missing from build PDB checksums: $($phase4Missing -join ', ')" }
$phase4Inputs = foreach ($phase4Relative in @($phase4Changes | Where-Object { $_.EndsWith('.xaml') -or $_.EndsWith('.appxmanifest') -or ($_ -like 'src/Files.App.CsWin32/*' -and $_.EndsWith('.txt')) })) {
    [pscustomobject]@{
        Path = $phase4Relative
        SHA256 = (Get-FileHash -LiteralPath (Resolve-Phase4Path ('source/' + $phase4Relative))).Hash
        CheckKind = 'source-input-hash-not-a-portable-PDB-document'
    }
}
$phase4SourceManifestPath = Resolve-Phase4Path 'source/src/Files.App/Package.appxmanifest'
$phase4SourceManifest = [xml][IO.File]::ReadAllText($phase4SourceManifestPath)
$phase4LayoutManifestPath = Resolve-Phase4Path ($Delivery + '/layout/AppxManifest.xml')
$phase4LayoutManifest = [xml][IO.File]::ReadAllText($phase4LayoutManifestPath)
foreach ($phase4Attribute in @('Name', 'Publisher', 'Version')) {
    if ($phase4SourceManifest.Package.Identity.GetAttribute($phase4Attribute) -ne $phase4LayoutManifest.Package.Identity.GetAttribute($phase4Attribute)) { throw "Source/package manifest identity differs: $phase4Attribute" }
}
$phase4Result = [ordered]@{
    VerifiedUtc = [DateTime]::UtcNow.ToString('o')
    Status = 'compiled-source-and-packaged-assembly-match'
    Scope = 'SHA256 Portable PDB checksums of every changed/new production C# file under src since Phase IV start; PDB CodeView identity; packaged assembly equality. XAML, package manifest and CsWin32 declarations have separate input hashes. No runtime verdict.'
    PhaseIIIStartCommit = $phase4Start
    SourceCommit = (& git -C $phase4Repository rev-parse HEAD)
    SourceWorkingTreeDirty = (@(& git -C $phase4Repository status --porcelain).Count -gt 0)
    Delivery = [IO.Path]::GetRelativePath($phase4Root, (Resolve-Phase4Path $Delivery))
    ChangedCSharpCount = $phase4ChangedCs.Count
    Assemblies = @($phase4Assemblies)
    Documents = @($phase4Documents)
    SourceInputs = @($phase4Inputs)
    Manifest = @{ Path = 'src/Files.App/Package.appxmanifest'; SourceSHA256 = (Get-FileHash -LiteralPath $phase4SourceManifestPath).Hash; LayoutSHA256 = (Get-FileHash -LiteralPath $phase4LayoutManifestPath).Hash; Identity = $phase4LayoutManifest.Package.Identity.Name; Publisher = $phase4LayoutManifest.Package.Identity.Publisher; Version = $phase4LayoutManifest.Package.Identity.Version }
}
$phase4Json = $phase4Result | ConvertTo-Json -Depth 8
$phase4Output = [IO.FileStream]::new($phase4ReportPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
try {
    $phase4Bytes = [Text.UTF8Encoding]::new($false).GetBytes($phase4Json)
    $phase4Output.Write($phase4Bytes, 0, $phase4Bytes.Length)
} finally { $phase4Output.Dispose() }
Write-Output $phase4Result.Status
