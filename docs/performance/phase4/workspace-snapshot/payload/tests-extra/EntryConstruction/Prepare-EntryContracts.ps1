#Requires -Version 7.2
[CmdletBinding()]
param([ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$SnapshotName = 'verified')
$ErrorActionPreference = 'Stop'
$entryRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$entryRepository = Join-Path $entryRoot 'source'
$entrySnapshot = Join-Path $PSScriptRoot "snapshots/$SnapshotName"
if (Test-Path -LiteralPath $entrySnapshot) { throw 'Snapshot exists; use a fresh name to preserve extracted sources.' }
if ((& git -C $entryRepository rev-parse files-turbo-phase3-start) -ne 'b18efc91ed3033081691f407a2bb7b5ee3d7d21d') { throw 'Unexpected Phase II reference.' }
function Read-EntryOriginal([string]$Relative) {
    $entryLines = @(& git -C $entryRepository show "files-turbo-phase3-start:$Relative")
    if ($LASTEXITCODE -ne 0) { throw "Could not read original source: $Relative" }
    return ($entryLines -join "`r`n") + "`r`n"
}
function Get-EntryBalanced([string]$Source, [int]$Start, [char]$Open, [char]$Close) {
    $entryDepth = 0
    for ($entryIndex = $Start; $entryIndex -lt $Source.Length; $entryIndex++) {
        if ($Source[$entryIndex] -eq $Open) { $entryDepth++ }
        elseif ($Source[$entryIndex] -eq $Close) {
            $entryDepth--
            if (!$entryDepth) { return $Source.Substring($Start, $entryIndex - $Start + 1) }
        }
    }
    throw 'Could not extract balanced member.'
}
function Get-EntryMember([string]$Source, [string]$Marker) {
    $entryStart = $Source.IndexOf($Marker, [StringComparison]::Ordinal)
    if ($entryStart -lt 0) { throw "Member missing: $Marker" }
    $entryBrace = $Source.IndexOf('{', $entryStart)
    return $Source.Substring($entryStart, $entryBrace - $entryStart) + (Get-EntryBalanced $Source $entryBrace '{' '}')
}
function Get-EntryCondition([string]$Source, [string]$Marker) {
    $entryStart = $Source.IndexOf($Marker, [StringComparison]::Ordinal)
    if ($entryStart -lt 0) { throw "Condition missing: $Marker" }
    $entryParen = $Source.IndexOf('(', $entryStart)
    $entryCondition = Get-EntryBalanced $Source $entryParen '(' ')'
    return $entryCondition.Substring(1, $entryCondition.Length - 2)
}
function Write-EntrySource([string]$Name, [string]$Text) {
    [IO.File]::WriteAllText((Join-Path $entrySnapshot $Name), ($Text -replace '\r?\n', "`r`n"), [Text.UTF8Encoding]::new($false))
}
$entryListedPath = 'src/Files.App/Data/Items/ListedItem.cs'
$entryNativePath = 'src/Files.App/Utils/Storage/Enumerators/Win32StorageEnumerator.cs'
$entryExtensionsPath = 'src/Files.Shared/Helpers/FileExtensionHelpers.cs'
$entryZipPath = 'src/Files.App/Utils/Storage/StorageItems/ZipStorageFolder.cs'
$entryShellPath = 'src/Files.App/ViewModels/ShellViewModel.cs'
$entryOldListed = Read-EntryOriginal $entryListedPath
$entryNewListed = [IO.File]::ReadAllText((Join-Path $entryRepository $entryListedPath))
$entryOldNative = Read-EntryOriginal $entryNativePath
$entryNewNative = [IO.File]::ReadAllText((Join-Path $entryRepository $entryNativePath))
$entryOldExtensions = Read-EntryOriginal $entryExtensionsPath
$entryNewExtensions = [IO.File]::ReadAllText((Join-Path $entryRepository $entryExtensionsPath))
$entryZip = [IO.File]::ReadAllText((Join-Path $entryRepository $entryZipPath))
$entryOldShell = Read-EntryOriginal $entryShellPath
$entryNewShell = [IO.File]::ReadAllText((Join-Path $entryRepository $entryShellPath))
$null = New-Item -ItemType Directory -Path $entrySnapshot
foreach ($entryVariant in @('Original', 'Current')) {
    $entryListed = if ($entryVariant -eq 'Original') { $entryOldListed } else { $entryNewListed }
    $entryHelper = if ($entryVariant -eq 'Original') { $entryOldExtensions } else { $entryNewExtensions }
    $entryNative = if ($entryVariant -eq 'Original') { $entryOldNative } else { $entryNewNative }
    $entryShell = if ($entryVariant -eq 'Original') { $entryOldShell } else { $entryNewShell }
    $entryNameMember = Get-EntryMember $entryListed 'public virtual string? Name'
    Write-EntrySource "$entryVariant-Name.cs" @"
using System.IO;
using EntryConstruction.Contracts;
namespace EntryConstruction.Generated;
public class ${entryVariant}ListedItem
{
    protected static FakeUserSettings UserSettingsService => Settings.UserSettingsService;
    private string? itemNameRaw;
    public string? ItemNameRaw { get => itemNameRaw; set => itemNameRaw = value; }
    public StorageItemTypes PrimaryItemAttribute { get; set; }
    $entryNameMember
}
"@
    $entryHelper = $entryHelper.Replace('namespace Files.Shared.Helpers', "namespace Files.Shared.Helpers.$entryVariant")
    Write-EntrySource "$entryVariant-Extensions.cs" ("using Directory = EntryConstruction.Contracts.ProbeDirectory;`r`n" + $entryHelper)
    $entryZipMember = Get-EntryMember $entryZip 'public static bool IsZipPath'
    Write-EntrySource "$entryVariant-Zip.cs" @"
using System.Diagnostics.CodeAnalysis;
using IO = EntryConstruction.Contracts.ProbeIO;
using FileExtensionHelpers = Files.Shared.Helpers.$entryVariant.FileExtensionHelpers;
namespace EntryConstruction.Generated;
public static class ${entryVariant}Zip
{
    $entryZipMember
}
"@
    $entryMarker = if ($entryVariant -eq 'Original') { 'else if (FileExtensionHelpers.IsShortcutOrUrlFile(findData.cFileName))' } else { 'else if (string.Equals(itemFileExtension, ".lnk"' }
    $entryCondition = Get-EntryCondition $entryNative $entryMarker
    $entryIsUrlStart = $entryNative.IndexOf('var isUrl =', $entryNative.IndexOf($entryMarker, [StringComparison]::Ordinal))
    if ($entryIsUrlStart -lt 0) { throw 'URL classification missing.' }
    $entryIsUrlEnd = $entryNative.IndexOf(';', $entryIsUrlStart)
    $entryIsUrl = $entryNative.Substring($entryIsUrlStart + 'var isUrl ='.Length, $entryIsUrlEnd - $entryIsUrlStart - 'var isUrl ='.Length).Trim()
    Write-EntrySource "$entryVariant-Classification.cs" @"
using System.IO;
using EntryConstruction.Contracts;
using FileExtensionHelpers = Files.Shared.Helpers.$entryVariant.FileExtensionHelpers;
namespace EntryConstruction.Generated;
public static class ${entryVariant}Classification
{
    public static LinkKind Classify(string name, string root)
    {
        var findData = new FindData(name);
        var itemPath = Path.Combine(root, name);
        string? itemFileExtension = name.Contains('.') ? Path.GetExtension(itemPath) : null;
        bool isShortcut = $entryCondition;
        bool isUrl = isShortcut && ($entryIsUrl);
        return new LinkKind(isShortcut, isUrl);
    }
}
"@
    $entrySolutionMember = Get-EntryMember $entryShell 'private void CheckForSolutionFile()'
    $entryRequireLock = if ($entryVariant -eq 'Original') { 'false' } else { 'true' }
    Write-EntrySource "$entryVariant-Solution.cs" @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EntryConstruction.Contracts;
using FileExtensionHelpers = Files.Shared.Helpers.$entryVariant.FileExtensionHelpers;
namespace EntryConstruction.Generated;
public sealed class ${entryVariant}Solution
{
    private readonly object loadCollectionSync = new();
    private readonly List<SolutionItem> filesAndFolders = new();
    private string? solutionFilePath;
    public bool SetterRanInsideCollectionLock { get; private set; }
    public string? SolutionFilePath
    {
        get => solutionFilePath;
        set { SetterRanInsideCollectionLock = Monitor.IsEntered(loadCollectionSync); solutionFilePath = value; }
    }
    public void SetItems(params SolutionItem[] items)
    {
        filesAndFolders.Clear();
        foreach (var item in items)
        {
            item.RequiredLock = $entryRequireLock ? loadCollectionSync : null;
            filesAndFolders.Add(item);
        }
    }
    public void Run() => CheckForSolutionFile();
    $entrySolutionMember
}
"@
}
$entrySourceHashes = foreach ($entryPath in @($entryListedPath, $entryNativePath, $entryExtensionsPath, $entryZipPath, $entryShellPath)) {
    [pscustomobject]@{ Path = "source/$entryPath"; SHA256 = (Get-FileHash -LiteralPath (Join-Path $entryRepository $entryPath)).Hash }
}
$entryMetadata = [ordered]@{
    PreparedUtc = [DateTime]::UtcNow.ToString('o')
    OriginalReference = 'files-turbo-phase3-start'
    OriginalCommit = 'b18efc91ed3033081691f407a2bb7b5ee3d7d21d'
    Scope = 'Verbatim production Name property, extension helper, known-file shortcut condition and URL expression, IsZipPath method and CheckForSolutionFile method. Controlled model/settings/collection and filesystem probe counters; not full GetFile or WinUI.'
    CurrentSource = @($entrySourceHashes)
    SnapshotName = $SnapshotName
    HarnessSource = @(foreach ($entryHarnessPath in @('Program.cs', 'EntryConstruction.csproj', 'Prepare-EntryContracts.ps1')) { [pscustomobject]@{ Path = "tests-extra/EntryConstruction/$entryHarnessPath"; SHA256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $entryHarnessPath)).Hash } })
    ExtractedSource = @(Get-ChildItem -LiteralPath $entrySnapshot -Filter '*.cs' -File | ForEach-Object { [pscustomobject]@{ Path = $_.Name; SHA256 = (Get-FileHash -LiteralPath $_.FullName).Hash } })
}
Write-EntrySource 'source-metadata.json' ($entryMetadata | ConvertTo-Json -Depth 6)
Write-Output $entrySnapshot
