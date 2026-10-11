param([string]$NavigationDirectory = 'benchmarks/results/phase2/navigation-final-verified')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $workspace
function Percentile($Rows, [string]$Field, [double]$Percent) {
    $values = @($Rows | ForEach-Object { [double]$_.$Field } | Sort-Object)
    $position = ($values.Count - 1) * $Percent
    $lower = [int][Math]::Floor($position)
    $upper = [int][Math]::Ceiling($position)
    return $values[$lower] + ($values[$upper] - $values[$lower]) * ($position - $lower)
}
$navigationRows = foreach ($round in 0, 1) {
    foreach ($variant in 'baseline', 'phase1', 'phase2') {
        Get-Content "$NavigationDirectory/round-$round/$variant-samples.json" -Raw | ConvertFrom-Json
    }
}
$navigation = foreach ($group in ($navigationRows | Group-Object Variant, Count)) {
    $rows = $group.Group
    [pscustomobject]@{
        variant = $rows[0].Variant; count = $rows[0].Count; samples = $rows.Count
        p50TotalMs = Percentile $rows TotalMs 0.5; p95TotalMs = Percentile $rows TotalMs 0.95
        p50FirstResultMs = Percentile $rows FirstResultMs 0.5; p95FirstResultMs = Percentile $rows FirstResultMs 0.95
        p50AllocBytes = Percentile $rows AllocBytes 0.5
        p50CpuMs = Percentile $rows CpuMs 0.5
        p50Publications = Percentile $rows Publications 0.5
        p50PublishedReferences = Percentile $rows PublicationItems 0.5
        p50LongestPublicationMs = Percentile $rows MaxPublishMs 0.5
        peakProcessWorkingSetBytes = ($rows | Measure-Object PeakWorkingSetBytes -Maximum).Maximum
    }
}
$dateRows = foreach ($round in 0, 1) {
    foreach ($variant in 'baseline', 'phase1', 'phase2') {
        Get-Content "benchmarks/results/phase2/dates-comparison/round-$round/$variant-samples.json" -Raw | ConvertFrom-Json
    }
}
$dates = foreach ($group in ($dateRows | Group-Object variant, count, visibleRows)) {
    $rows = $group.Group
    [pscustomobject]@{
        variant = $rows[0].variant; count = $rows[0].count; visibleRows = $rows[0].visibleRows; samples = $rows.Count
        p50TotalMs = Percentile $rows totalMs 0.5; p95TotalMs = Percentile $rows totalMs 0.95
        p50MaterializeMs = Percentile $rows materializeMs 0.5
        p50AllocBytes = Percentile $rows allocatedBytes 0.5
        p50FormatCalls = Percentile $rows formatCalls 0.5
    }
}
$summary = [ordered]@{
    generatedUtc = [DateTime]::UtcNow.ToString('o')
    scope = 'Source UniversalStorageEnumerator + source collections/sort using filesystem-backed test storage; separately source ListedItem date properties and date formatters. WinUI/COM/native thumbnails not measured.'
    dataset = 'Existing 161000 empty synthetic files; names,size,timestamps read; no regenerated dataset.'
    methodology = 'Two independent processes per variant, forward/reverse order, navigation pipeline warmup on 1k files and date warmup per row-count/visibility scenario; five timed samples per process. Percentiles linearly interpolate pooled ten samples.'
    filesystemCache = 'Warm/uncontrolled; no OS caches flushed and no cold-cache claim.'
    navigationResults = $NavigationDirectory
    provenance = 'reports/phase2-navigation-final-provenance.json'
    supersededSeries = 'Prior navigation-comparison series used Phase II collection source in the phase1 harness and is not an exact Phase I comparison. Raw samples and prior summaries remain preserved, with binary/source hashes in reports/phase2-navigation-prior-comparison.'
    caveats = 'Natural name comparer test adapter is ordinal-ignore-case; numeric padded synthetic names sort equivalently. Storage wrappers/icon service/WinUI are replaced stubs. Peak working set is process lifetime maximum, not per-folder peak. These times cannot be treated as complete application navigation latency.'
    navigation = @($navigation)
    dates = @($dates)
}
$summary | ConvertTo-Json -Depth 8 | Set-Content reports/phase2-navigation-summary.json -Encoding utf8
$navigation | Export-Csv reports/phase2-navigation-summary.csv -NoTypeInformation -Encoding utf8
$dates | Export-Csv reports/phase2-date-label-summary.csv -NoTypeInformation -Encoding utf8
$navigation | Where-Object count -EQ 100000 | Format-Table variant,p50TotalMs,p95TotalMs,p50AllocBytes,p50Publications,p50FirstResultMs
$dates | Where-Object { $_.count -eq 100000 } | Format-Table variant,visibleRows,p50TotalMs,p95TotalMs,p50AllocBytes,p50FormatCalls
