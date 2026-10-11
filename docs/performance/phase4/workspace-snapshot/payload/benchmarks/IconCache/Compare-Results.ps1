param(
    [string]$WorkspaceRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [string]$ResultsRelativeRoot = 'artifacts\minimal-controlled'
)

$ErrorActionPreference = 'Stop'
$WorkspaceRoot = [System.IO.Path]::GetFullPath($WorkspaceRoot)
$resultsDirectory = [System.IO.Path]::GetFullPath((Join-Path $WorkspaceRoot $ResultsRelativeRoot))
if (!$resultsDirectory.StartsWith($WorkspaceRoot.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Results must remain inside WorkspaceRoot.'
}
$resultsLink = '../' + $ResultsRelativeRoot.Replace('\', '/').TrimEnd('/')
$datasets = @{}
$testSummaries = @()
foreach ($pass in 1, 2) {
    foreach ($variant in 'baseline', 'turbo') {
        $path = Join-Path $resultsDirectory "pass-$pass\icon-cache-$variant-samples.json"
        $datasets["$pass/$variant"] = @(Get-Content -LiteralPath $path -Raw | ConvertFrom-Json)
        if ($datasets["$pass/$variant"].Count -ne 496) { throw "Unexpected sample count: $path" }
        $tests = @(Get-Content -LiteralPath (Join-Path $resultsDirectory "pass-$pass\icon-cache-$variant-tests.json") -Raw | ConvertFrom-Json)
        $testSummaries += [pscustomobject]@{
            pass = $pass
            variant = $variant
            passed = @($tests | Where-Object Status -eq 'passed').Count
            failed = @($tests | Where-Object Status -eq 'failed').Count
            observed = @($tests | Where-Object Status -eq 'observed').Count
        }
    }
}

function Get-Median([double[]]$Sorted) {
    $center = [int][math]::Floor($Sorted.Count / 2)
    if ($Sorted.Count % 2) { return $Sorted[$center] }
    return ($Sorted[$center - 1] + $Sorted[$center]) / 2
}

function Get-Summary($Samples) {
    $times = [double[]]@($Samples.ElapsedMs | Sort-Object)
    $bytes = [double[]]@($Samples.AllocatedBytes | Sort-Object)
    [pscustomobject]@{
        samples = $times.Count
        median_ms = Get-Median $times
        p95_ms = $times[[int][math]::Ceiling($times.Count * 0.95) - 1]
        minimum_ms = $times[0]
        maximum_ms = $times[-1]
        median_allocated_bytes = [long](Get-Median $bytes)
    }
}

$comparisons = @()
foreach ($scope in 'pass-1', 'pass-2', 'pooled') {
    foreach ($workload in 'lowercase-extension', 'uppercase-extension', 'folder', 'mixed') {
        foreach ($operations in 1000, 10000, 50000, 100000) {
            $summaries = @{}
            foreach ($variant in 'baseline', 'turbo') {
                $samples = if ($scope -eq 'pooled') { @($datasets["1/$variant"] + $datasets["2/$variant"]) } else { $datasets["$($scope.Substring(5))/$variant"] }
                $samples = @($samples | Where-Object { $_.Workload -eq $workload -and $_.Operations -eq $operations })
                $summaries[$variant] = Get-Summary $samples
            }
            $baseline = $summaries['baseline']
            $turbo = $summaries['turbo']
            $comparisons += [pscustomobject]@{
                scope = $scope
                workload = $workload
                operations = $operations
                samples_per_variant = $baseline.samples
                baseline_median_ms = $baseline.median_ms
                turbo_median_ms = $turbo.median_ms
                median_percent_reduction = 100 * (1 - $turbo.median_ms / $baseline.median_ms)
                baseline_p95_ms = $baseline.p95_ms
                turbo_p95_ms = $turbo.p95_ms
                p95_percent_reduction = 100 * (1 - $turbo.p95_ms / $baseline.p95_ms)
                baseline_min_ms = $baseline.minimum_ms
                turbo_min_ms = $turbo.minimum_ms
                baseline_max_ms = $baseline.maximum_ms
                turbo_max_ms = $turbo.maximum_ms
                baseline_median_allocated_bytes = $baseline.median_allocated_bytes
                turbo_median_allocated_bytes = $turbo.median_allocated_bytes
                allocated_bytes_percent_reduction = 100 * (1 - $turbo.median_allocated_bytes / $baseline.median_allocated_bytes)
                median_regressed = $turbo.median_ms -gt $baseline.median_ms
                p95_regressed = $turbo.p95_ms -gt $baseline.p95_ms
                maximum_regressed = $turbo.maximum_ms -gt $baseline.maximum_ms
                allocations_regressed = $turbo.median_allocated_bytes -gt $baseline.median_allocated_bytes
            }
        }
    }
}

$pooled100k = @($comparisons | Where-Object { $_.scope -eq 'pooled' -and $_.operations -eq 100000 })
$baselineSum = ($pooled100k.baseline_median_ms | Measure-Object -Sum).Sum
$turboSum = ($pooled100k.turbo_median_ms | Measure-Object -Sum).Sum
$baselineBytes = ($pooled100k.baseline_median_allocated_bytes | Measure-Object -Sum).Sum
$turboBytes = ($pooled100k.turbo_median_allocated_bytes | Measure-Object -Sum).Sum
$aggregate = [pscustomobject]@{
    definition = 'Equal-weight sum of the four workload medians at 100,000 warm cache calls each; derived statistic, not measured wall-time of one composed operation. No aggregate p95 is claimed.'
    calls_represented = 400000
    samples_per_workload_per_variant = 62
    baseline_sum_medians_ms = $baselineSum
    turbo_sum_medians_ms = $turboSum
    median_sum_percent_reduction = 100 * (1 - $turboSum / $baselineSum)
    baseline_sum_median_allocated_bytes = $baselineBytes
    turbo_sum_median_allocated_bytes = $turboBytes
    allocated_bytes_sum_percent_reduction = 100 * (1 - $turboBytes / $baselineBytes)
}
$regressions = @($comparisons | Where-Object { $_.median_regressed -or $_.p95_regressed -or $_.maximum_regressed -or $_.allocations_regressed })
$result = [pscustomobject]@{
    schema_version = 1
    data_root = $ResultsRelativeRoot.Replace('\', '/')
    baseline_commit = '1994d9a4269f5144c70bc67d6a48de4a20616912'
    baseline_blob = 'e6a22b2c00666187e3c6bda464047f1de9f295e7'
    baseline_source_sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $WorkspaceRoot 'benchmarks\IconCache\baseline\IconCacheService.cs')).Hash
    turbo_source_sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $WorkspaceRoot 'source\src\Files.App\Services\Storage\IconCacheService.cs')).Hash
    configuration = 'Release; .NET 10.0.12 x64; workstation GC; DOTNET_TieredCompilation=0; warm cache; shell/WinUI stubs excluded from timing'
    methodology = 'Two sequential passes, first baseline then turbo, second turbo then baseline. 31 samples per case per pass. Pooled medians use the mean of the two central values for 62 samples; p95 uses nearest rank.'
    comparisons = $comparisons
    aggregate_100k_equal_weight = $aggregate
    regressions = $regressions
    test_results = $testSummaries
    limits = @('Internal component only; no UI/navigation/thumbnail extraction latency measured.', 'No cold-cache native shell timing.', 'Lazy/task caching adds fixed memory per key; retained bytes/idle RAM are not measured.', 'Uppercase extensions still allocate invariant lowercase strings to preserve original semantics.', 'DPI invalidation, explicit cache bounds and bitmap Clear races remain pending.')
}
$comparisonDirectory = $resultsDirectory
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $comparisonDirectory 'icon-cache-comparison.json') -Encoding utf8
$csvRows = foreach ($comparison in $comparisons) {
    $values = [ordered]@{}
    foreach ($property in $comparison.PSObject.Properties) {
        $value = $property.Value
        $values[$property.Name] = if ($value -is [double]) { $value.ToString('R', [System.Globalization.CultureInfo]::InvariantCulture) } else { $value }
    }
    [pscustomobject]$values
}
$csvRows | Export-Csv -LiteralPath (Join-Path $comparisonDirectory 'icon-cache-comparison.csv') -NoTypeInformation -Encoding utf8

$culture = [System.Globalization.CultureInfo]::InvariantCulture
function Format-Value($Value, $Digits = 4) { ([double]$Value).ToString("F$Digits", $culture) }
$table = @()
foreach ($row in $pooled100k) {
    $label = switch ($row.workload) { 'lowercase-extension' { 'Extensiones en minúsculas' }; 'uppercase-extension' { 'Extensiones en mayúsculas' }; 'folder' { 'Carpetas' }; 'mixed' { 'Claves mixtas' } }
    $table += "| $label | $(Format-Value $row.baseline_median_ms) | $(Format-Value $row.turbo_median_ms) | $(Format-Value $row.median_percent_reduction 2) % | $(Format-Value $row.baseline_p95_ms) / $(Format-Value $row.turbo_p95_ms) | $($row.baseline_median_allocated_bytes) / $($row.turbo_median_allocated_bytes) |"
}
$ranges = @()
foreach ($row in $pooled100k) {
    $ranges += "| $($row.workload) | $(Format-Value $row.baseline_min_ms)–$(Format-Value $row.baseline_max_ms) | $(Format-Value $row.turbo_min_ms)–$(Format-Value $row.turbo_max_ms) |"
}
$regressionDescription = if ($regressions.Count -eq 0) { 'No se observaron regresiones en mediana, p95, máximo ni bytes transitorios en los 16 subcasos de cada pasada ni en los 16 subcasos combinados (48 comparaciones).' } else { "$($regressions.Count) comparaciones presentan al menos una métrica peor; consulte el campo regressions del JSON, sin ocultar máximos aislados." }
$report = @"
# Resultados de IconCacheService

Se modificó exclusivamente el servicio real de iconos genéricos en ``source/src/Files.App/Services/Storage/IconCacheService.cs``. Los enumeradores nativo y universal y la precarga de resultados de búsqueda lo llaman por elemento. La versión original formaba una clave interpolada y devolvía una tarea nueva en cada acierto con un resultado no nulo. La modificación utiliza una clave estructurada, conserva exactamente ``ToLowerInvariant`` y devuelve la tarea ya cacheada. La carga pendiente utiliza ``Lazy<Task<byte[]?>>`` para que los solicitantes de la misma clave compartan una extracción. La creación de la lambda queda en un método de miss separado, evitando una asignación de cierre en los aciertos.

El original corresponde al commit ``1994d9a4269f5144c70bc67d6a48de4a20616912`` y blob ``e6a22b2c00666187e3c6bda464047f1de9f295e7``. Se compila su fuente textual exacta, normalizada solo a CRLF, y la variante Turbo enlaza el fichero de producción modificado. Sus hashes están en [la comparación JSON]($resultsLink/icon-cache-comparison.json).

## Alcance y método

Este benchmark mide exclusivamente búsquedas en una caché caliente de un componente interno. Las APIs shell y las clases WinUI se sustituyen por dobles explícitos; la región cronometrada comprueba que no solicita ninguna extracción shell. No mide navegación completa, arranque, iconos nativos, miniaturas, UI, RAM en reposo ni el tiempo hasta mostrar archivos.

Las métricas finales proceden de ``$($ResultsRelativeRoot.Replace('\', '/'))``. Se registraron dos pasadas secuenciales de 31 repeticiones por cada uno de 16 casos (4 patrones × 1.000/10.000/50.000/100.000 llamadas), 496 muestras por variante y pasada, 1.984 muestras en total. La primera pasada ejecutó original antes que Turbo; la segunda invirtió el orden. Cada caso combinado utiliza 62 muestras por variante. Las cifras son llamadas a caché, no archivos físicos. La mediana combinada promedia los dos valores centrales; p95 usa rango más próximo. Antes de registrar se calientan las rutas de código. GC se fuerza fuera de la región medida y no se cronometra. Las comparaciones anteriores en ``artifacts/controlled`` y ``artifacts/final-controlled`` se conservan; estas tablas emplean únicamente la repetición de entrega correspondiente al cambio mínimo conservado de colecciones, sin cambios adicionales en IconCacheService.

Configuración: Release, .NET 10.0.12 x64, Windows build 26200, 16 procesadores lógicos, GC de estación de trabajo, ``DOTNET_TieredCompilation=0`` solo para los procesos de benchmark. La primera serie, preservada en ``artifacts/icon-cache``, usó JIT por niveles predeterminado y mostró calentamiento variable en el primer patrón; no se mezcla con esta comparación. El porcentaje controlado depende de esta configuración y no representa una mejora medida de toda la aplicación ni garantiza el mismo porcentaje con el JIT de producción.

## 100.000 llamadas por patrón, dos pasadas combinadas

| Patrón | Mediana original ms | Mediana Turbo ms | Reducción de tiempo | p95 original / Turbo ms | Bytes asignados original / Turbo |
|---|---:|---:|---:|---:|---:|
$($table -join "`r`n")

| Patrón | Mínimo–máximo original ms | Mínimo–máximo Turbo ms |
|---|---:|---:|
$($ranges -join "`r`n")

Como agregado con igual peso de los cuatro patrones, la suma de sus medianas de 100.000 llamadas pasa de **$(Format-Value $aggregate.baseline_sum_medians_ms) ms a $(Format-Value $aggregate.turbo_sum_medians_ms) ms**, una reducción de **$(Format-Value $aggregate.median_sum_percent_reduction 2) %**. La suma de los bytes transitorios medianos pasa de **$($aggregate.baseline_sum_median_allocated_bytes) a $($aggregate.turbo_sum_median_allocated_bytes)** (**$(Format-Value $aggregate.allocated_bytes_sum_percent_reduction 2) % menos**). Este agregado es una operación estadística sobre cuatro mediciones independientes y representa 400.000 llamadas; no es el cronometraje de una operación compuesta ni una métrica de navegación. No se atribuye p95 al agregado.

$regressionDescription Las comparaciones de cada pasada y tamaño, con mínimo/máximo y banderas de regresión, están en [CSV]($resultsLink/icon-cache-comparison.csv) y [JSON]($resultsLink/icon-cache-comparison.json). Son mediciones de duración y asignación transitoria, sin intervalo de confianza ni evidencia de rendimiento de extremo a extremo.

## Fiabilidad verificada

En ambas variantes y pasadas pasan los 12 contratos dirigidos. Turbo exige además las condiciones de deduplicación e invalidación que el original registra como observaciones: 32 solicitudes pendientes y 32 solicitantes paralelos programaban 32 cargas en el original; Turbo programa una. Un ``Clear`` mientras hay una carga pendiente permite que termine el solicitante existente, pero impide que repueble la caché de bytes borrada. Un fallo antiguo no borra una entrada nueva creada tras ``Clear``. La excepción de extracción se propaga y la solicitud siguiente reintenta; el resultado ``null`` permanece cacheado, como antes.

También se verifican separación de tamaños, iconos genéricos independientes del path, extensión ignorada en carpetas, diferencia original entre extensión null y vacía, semántica de minúscula invariante (incluida la distinción ``.s``/``.ſ``), ``Clear`` de caché de bitmap sin cargas pendientes y rechazo del bitmap cuando falta dispatcher. Las clases bitmap/dispatcher son dobles; estos contratos no validan COM, renderizado ni afinidad de hilo WinUI real. El registro de ejecuciones está en [component-benchmarks-minimal.log](component-benchmarks-minimal.log).

## Costes y pendientes

Cada clave ahora retiene un ``Lazy`` y una tarea, añadiendo memoria fija frente al byte array original. No se midieron bytes retenidos ni RAM en reposo. El beneficio demostrado corresponde a las asignaciones transitorias por llamada: las extensiones ya en minúsculas y las carpetas no asignan; las mayúsculas conservan la asignación de ``ToLowerInvariant`` (32 bytes por llamada en este conjunto) y las claves mixtas 8 bytes de media. Sustituirlo indiscriminadamente por ``OrdinalIgnoreCase`` alteraría la semántica original de extensiones Unicode como ``.s``/``.ſ``.

Siguen pendientes límites explícitos/LRU y invalidación por cambio DPI de la caché. La caché de bitmap mantiene la ruta asíncrona original: una creación de bitmap iniciada antes de ``Clear`` aún podría repoblarla al finalizar; esta modificación solo corrige la invalidación pendiente de la caché de bytes. No se reclama una mejora de extracción de miniaturas, su cancelación o su cacheado por archivo. El pool STA existente tiene cola sin límite y la extracción shell no recibe token; requieren mediciones UI/COM para modificaciones posteriores. Las operaciones de archivos y las integraciones nativas no se modifican en esta optimización.

## Reproducción

Los comandos, requisitos y dobles se documentan en [benchmarks/IconCache/README.md](../benchmarks/IconCache/README.md). El script [Compare-Results.ps1](../benchmarks/IconCache/Compare-Results.ps1) recompone esta comparación y el informe a partir de ambas pasadas guardadas, sin compilar ni volver a medir. Admite ``-WorkspaceRoot`` y ``-ResultsRelativeRoot``; se ha ejecutado con ``-ResultsRelativeRoot artifacts\minimal-controlled``, también la carpeta predeterminada.
"@
$reportPath = Join-Path $WorkspaceRoot 'reports\ICON_CACHE_RESULTS.md'
[System.IO.File]::WriteAllText($reportPath, ($report -replace "`r?`n", "`r`n"), [System.Text.UTF8Encoding]::new($false))
$pooled100k | Select-Object workload, baseline_median_ms, turbo_median_ms, median_percent_reduction, baseline_median_allocated_bytes, turbo_median_allocated_bytes | Format-Table -AutoSize
$aggregate | ConvertTo-Json -Compress
Write-Output "Regression comparisons: $($regressions.Count) / $($comparisons.Count)"
$testSummaries | ConvertTo-Json -Compress
