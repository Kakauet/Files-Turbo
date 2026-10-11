[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet(1000, 10000, 50000, 100000)]
    [int[]]$Counts = @(1000, 10000, 50000),

    [ValidateRange(0, 4096)]
    [int]$FileBytes = 0,

    [ValidateRange(1, 200000)]
    [int]$MaxTotalFiles = 200000,

    [ValidateRange(1, 16)]
    [int]$MaxEstimatedGiB = 2,

    [ValidateRange(1, 16)]
    [int]$ReserveFreeGiB = 2,

    [ValidateRange(10, 900)]
    [int]$MaxDurationSeconds = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$datasetWorkspace = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\', '/')
$datasetWorkspacePrefix = $datasetWorkspace + [IO.Path]::DirectorySeparatorChar
$datasetRoot = [IO.Path]::GetFullPath((Join-Path $datasetWorkspace 'benchmarks\synthetic'))

function Assert-DatasetPath {
    param([Parameter(Mandatory)][string]$Path)

    $datasetFullPath = [IO.Path]::GetFullPath($Path)
    if (!$datasetFullPath.StartsWith($datasetWorkspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "La ruta sale del workspace: $datasetFullPath"
    }

    # Check existing ancestors without following junctions or symbolic links.
    $datasetPathRoot = [IO.Path]::GetPathRoot($datasetFullPath)
    $datasetCurrent = $datasetPathRoot
    $datasetRelativeSegments = $datasetFullPath.Substring($datasetPathRoot.Length).Split(
        [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar),
        [StringSplitOptions]::RemoveEmptyEntries)
    foreach ($datasetSegment in $datasetRelativeSegments) {
        $datasetCurrent = [IO.Path]::Combine($datasetCurrent, $datasetSegment)
        try {
            $datasetAttributes = [IO.File]::GetAttributes($datasetCurrent)
            if (($datasetAttributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "No se permiten enlaces ni junctions: $datasetCurrent"
            }
        }
        catch [IO.FileNotFoundException] { break }
        catch [IO.DirectoryNotFoundException] { break }
    }
    return $datasetFullPath
}

function Assert-DatasetCapacity {
    param([long]$NeededBytes)

    $datasetDrive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($datasetWorkspace))
    if (!$datasetDrive.IsReady) { throw 'La unidad del workspace no esta disponible.' }
    if ($datasetDrive.AvailableFreeSpace -lt ($NeededBytes + ($ReserveFreeGiB * 1GB))) {
        throw 'Espacio insuficiente para los datos sinteticos y la reserva configurada.'
    }
    return $datasetDrive.AvailableFreeSpace
}

$datasetCounts = @($Counts | Sort-Object -Unique)
$datasetTotalFiles = [long](($datasetCounts | Measure-Object -Sum).Sum)
if ($datasetTotalFiles -gt $MaxTotalFiles) { throw 'El total de archivos supera MaxTotalFiles.' }

# Conservative allowance for NTFS metadata and allocation; this is not a size measurement.
$datasetEstimatedBytes = $datasetTotalFiles * (8192L + [long]$FileBytes)
if ($datasetEstimatedBytes -gt ($MaxEstimatedGiB * 1GB)) {
    throw 'El tamano estimado supera MaxEstimatedGiB.'
}

$null = Assert-DatasetPath $datasetRoot
if ([IO.Directory]::Exists($datasetRoot) -or [IO.File]::Exists($datasetRoot)) {
    throw 'benchmarks\synthetic ya existe. Este generador no borra, reutiliza ni sobrescribe datos.'
}
$datasetFreeBytesBefore = Assert-DatasetCapacity $datasetEstimatedBytes

if (!$PSCmdlet.ShouldProcess($datasetRoot, "Crear $datasetTotalFiles archivos sinteticos")) { return }

$datasetPayload = [byte[]]::new($FileBytes)
for ($datasetByte = 0; $datasetByte -lt $FileBytes; $datasetByte++) {
    $datasetPayload[$datasetByte] = [byte](65 + ($datasetByte % 26))
}
$datasetExtensions = @('.txt', '.dat', '.log', '.bin')
$datasetUnicodeSuffix = [string][char]0x00E9 + '_' + [char]0x6771 + [char]0x4EAC + '_' + [char]0x00F1
$datasetStopwatch = [Diagnostics.Stopwatch]::StartNew()
$datasetRows = [Collections.Generic.List[object]]::new()
$datasetCreatedFiles = 0L
$datasetCompleted = $false
$datasetFailure = $null
$null = [IO.Directory]::CreateDirectory((Assert-DatasetPath $datasetRoot))

try {
    foreach ($datasetCount in $datasetCounts) {
        $datasetDirectory = Assert-DatasetPath ([IO.Path]::Combine($datasetRoot, [string]$datasetCount))
        $null = [IO.Directory]::CreateDirectory($datasetDirectory)
        $datasetRow = [pscustomobject]@{
            requestedFiles = $datasetCount
            createdFiles = 0
            directory = $datasetDirectory
            complete = $false
        }
        $datasetRows.Add($datasetRow)

        for ($datasetIndex = 0; $datasetIndex -lt $datasetCount; $datasetIndex++) {
            if (($datasetIndex % 128) -eq 0 -and $datasetStopwatch.Elapsed.TotalSeconds -gt $MaxDurationSeconds) {
                throw 'Tiempo limite del generador alcanzado; se conservan los datos parciales identificados.'
            }
            if (($datasetIndex % 4096) -eq 0) {
                $null = Assert-DatasetPath $datasetDirectory
                $null = Assert-DatasetCapacity 0
            }

            $datasetSuffix = if (($datasetIndex % 257) -eq 0) { '_' + $datasetUnicodeSuffix } else { '' }
            $datasetName = 'file_{0:D6}_{1}{2}{3}' -f $datasetIndex, ($datasetIndex % 11), $datasetSuffix, $datasetExtensions[$datasetIndex % $datasetExtensions.Count]
            $datasetFile = [IO.Path]::Combine($datasetDirectory, $datasetName)
            $datasetStream = [IO.FileStream]::new($datasetFile, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try {
                if ($FileBytes -gt 0) { $datasetStream.Write($datasetPayload, 0, $datasetPayload.Length) }
            }
            finally { $datasetStream.Dispose() }
            $datasetRow.createdFiles++
            $datasetCreatedFiles++
        }
        $datasetRow.complete = $true
    }
    $datasetCompleted = $true
}
catch {
    $datasetFailure = $_.Exception.Message
    throw
}
finally {
    $datasetStopwatch.Stop()
    $datasetManifest = [ordered]@{
        schemaVersion = 1
        purpose = 'Synthetic directory enumeration, sorting and filename search benchmarks; no valid thumbnail media.'
        generatedUtc = [DateTime]::UtcNow.ToString('O')
        workspaceRoot = $datasetWorkspace
        root = $datasetRoot
        counts = $datasetCounts
        fileBytes = $FileBytes
        totalFiles = $datasetCreatedFiles
        complete = $datasetCompleted
        failure = $datasetFailure
        elapsedSeconds = [Math]::Round($datasetStopwatch.Elapsed.TotalSeconds, 3)
        estimatedBytesBeforeGeneration = $datasetEstimatedBytes
        freeBytesBeforeGeneration = $datasetFreeBytesBefore
        directories = $datasetRows.ToArray()
        nameRecipe = 'file_{index:D6}_{index%11}_{unicode every 257}{extension cycling txt/dat/log/bin}'
    }
    $datasetManifestPath = Assert-DatasetPath ([IO.Path]::Combine($datasetRoot, 'dataset-manifest.json'))
    $datasetJson = $datasetManifest | ConvertTo-Json -Depth 5
    $datasetManifestStream = [IO.FileStream]::new($datasetManifestPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $datasetManifestWriter = [IO.StreamWriter]::new($datasetManifestStream, [Text.UTF8Encoding]::new($false))
        try { $datasetManifestWriter.Write($datasetJson) }
        finally { $datasetManifestWriter.Dispose() }
    }
    finally { $datasetManifestStream.Dispose() }
}

[pscustomobject]@{
    Root = $datasetRoot
    TotalFiles = $datasetCreatedFiles
    FileBytes = $FileBytes
    ElapsedSeconds = [Math]::Round($datasetStopwatch.Elapsed.TotalSeconds, 3)
    Manifest = $datasetManifestPath
}
