$ErrorActionPreference = 'Stop'
$workspace = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
Set-Location $workspace
function Percentile($Rows, [string]$Field, [double]$Percent) {
    $values = @($Rows | ForEach-Object { [double]$_.$Field } | Sort-Object)
    $position = ($values.Count - 1) * $Percent
    $lower = [int][Math]::Floor($position)
    $upper = [int][Math]::Ceiling($position)
    return $values[$lower] + ($values[$upper] - $values[$lower]) * ($position - $lower)
}
$summary = foreach ($mode in 'warm-default', 'tiering-off') {
    foreach ($variant in 'phase1', 'phase2') {
        $rows = @(foreach ($round in 0, 1) {
            Get-Content "benchmarks/results/phase2/dates-control-$mode/round-$round/$variant-samples.json" -Raw | ConvertFrom-Json
        })
        foreach ($group in ($rows | Group-Object visibleRows)) {
            $samples = $group.Group
            [pscustomobject]@{
                mode = $mode; variant = $variant; count = 50000; visibleRows = $samples[0].visibleRows
                samples = $samples.Count; warmupsPerScenario = 8
                p50Ms = Percentile $samples totalMs 0.5; p95Ms = Percentile $samples totalMs 0.95
                minMs = ($samples | Measure-Object totalMs -Minimum).Minimum
                maxMs = ($samples | Measure-Object totalMs -Maximum).Maximum
                p50AllocatedBytes = Percentile $samples allocatedBytes 0.5
                p50CpuMs = Percentile $samples cpuMs 0.5
                gen0Collections = @($samples.gen0Collections | Sort-Object -Unique)
                gen1Collections = @($samples.gen1Collections | Sort-Object -Unique)
                gen2Collections = @($samples.gen2Collections | Sort-Object -Unique)
            }
        }
    }
}
$csvRows = foreach ($row in $summary) {
    $columns = [ordered]@{}
    foreach ($property in $row.PSObject.Properties) {
        $columns[$property.Name] = if ($property.Value -is [array]) { $property.Value -join ';' }
            elseif ($property.Value -is [double]) { $property.Value.ToString([Globalization.CultureInfo]::InvariantCulture) }
            else { $property.Value }
    }
    [pscustomobject]$columns
}
$csvRows | Export-Csv reports/phase2-dates-control-summary.csv -NoTypeInformation -Encoding utf8
[pscustomobject]@{
    generatedUtc = [DateTime]::UtcNow.ToString('O')
    scope = 'Source ListedItem date fields/getters/setters/refresh and actual date formatters; no WinUI objects.'
    method = 'Two independent processes per variant and runtime mode, ABBA order, eight warmups per 50k visibility scenario and five samples per process. Runtime switches apply only to child benchmark processes.'
    originalNegativeCaseRetained = 'reports/phase2-date-label-summary.csv: 50k/all Phase I p50/p95 15.37/16.43 -> Phase II 33.00/55.54 ms.'
    conclusion = 'No sustained all-label regression reproduced after controlled warmup. JIT/tiering is an inference from progression and controls; JIT events were not captured. First UI realization remains unverified.'
    rows = $summary
} | ConvertTo-Json -Depth 8 | Set-Content reports/phase2-dates-control-summary.json -Encoding utf8
