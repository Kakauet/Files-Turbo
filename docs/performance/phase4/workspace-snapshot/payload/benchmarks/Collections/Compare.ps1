param(
    [Parameter(Mandatory)][string]$BaselineCsv,
    [Parameter(Mandatory)][string]$TurboCsv,
    [Parameter(Mandatory)][string]$OutputJson
)

$ErrorActionPreference = 'Stop'

function Get-Percentile([double[]]$Values, [double]$Quantile) {
    $sorted = @($Values | Sort-Object)
    $index = [Math]::Max(0, [Math]::Ceiling($Quantile * $sorted.Length) - 1)
    return $sorted[$index]
}

function Get-Median([double[]]$Values) {
    $sorted = @($Values | Sort-Object)
    $midpoint = [int][Math]::Floor($sorted.Length / 2)
    if (($sorted.Length % 2) -eq 0) { return ($sorted[$midpoint - 1] + $sorted[$midpoint]) / 2 }
    return $sorted[$midpoint]
}

$baseline = @(Import-Csv -LiteralPath $BaselineCsv)
$turbo = @(Import-Csv -LiteralPath $TurboCsv)
$comparison = foreach ($group in ($baseline | Group-Object scenario, count)) {
    $first = $group.Group[0]
    $turboGroup = @($turbo | Where-Object { $_.scenario -eq $first.scenario -and $_.count -eq $first.count })
    if ($group.Count -ne $turboGroup.Count -or $turboGroup.Count -eq 0) { throw "Missing or unmatched repetitions: $($group.Name)" }
    $baselineTime = [double[]]@($group.Group | ForEach-Object { [double]::Parse($_.elapsed_ms, [Globalization.CultureInfo]::InvariantCulture) / [int]$_.iterations })
    $turboTime = [double[]]@($turboGroup | ForEach-Object { [double]::Parse($_.elapsed_ms, [Globalization.CultureInfo]::InvariantCulture) / [int]$_.iterations })
    $baselineAllocation = [double[]]@($group.Group | ForEach-Object { [long]$_.allocated_bytes / [int]$_.iterations })
    $turboAllocation = [double[]]@($turboGroup | ForEach-Object { [long]$_.allocated_bytes / [int]$_.iterations })
    $baselineMedian = Get-Median $baselineTime
    $turboMedian = Get-Median $turboTime
    $baselineBytes = Get-Median $baselineAllocation
    $turboBytes = Get-Median $turboAllocation
    [PSCustomObject]@{
        scenario = $first.scenario
        count = [int]$first.count
        repetitions = $group.Count
        cache = 'warm-component'
        baseline_median_ms = $baselineMedian
        turbo_median_ms = $turboMedian
        time_reduction_percent = (1 - $turboMedian / $baselineMedian) * 100
        baseline_p95_ms = Get-Percentile $baselineTime 0.95
        turbo_p95_ms = Get-Percentile $turboTime 0.95
        baseline_min_ms = ($baselineTime | Measure-Object -Minimum).Minimum
        baseline_max_ms = ($baselineTime | Measure-Object -Maximum).Maximum
        turbo_min_ms = ($turboTime | Measure-Object -Minimum).Minimum
        turbo_max_ms = ($turboTime | Measure-Object -Maximum).Maximum
        baseline_median_allocated_bytes = $baselineBytes
        turbo_median_allocated_bytes = $turboBytes
        allocation_reduction_percent = (1 - $turboBytes / $baselineBytes) * 100
    }
}
$record = [PSCustomObject]@{
    scope = 'Actual repository collection component; WinUI, filesystem and application not measured'
    percentile_method = 'Nearest-rank p95; median is average of two middle values for even n'
    baseline_csv = $BaselineCsv
    turbo_csv = $TurboCsv
    comparison = @($comparison)
}
[IO.File]::WriteAllText($OutputJson, ($record | ConvertTo-Json -Depth 8) + "`r`n", [Text.UTF8Encoding]::new($false))
$comparison | Select-Object scenario, count, baseline_median_ms, turbo_median_ms, time_reduction_percent, baseline_median_allocated_bytes, turbo_median_allocated_bytes | Format-Table -AutoSize
