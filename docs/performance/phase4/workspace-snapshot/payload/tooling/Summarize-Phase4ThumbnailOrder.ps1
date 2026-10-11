#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$FirstSeenCsv,
    [Parameter(Mandatory)][string]$OutputReport,
    [ValidateRange(0,10)][double]$RowTolerancePixels = 2
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
function Resolve-TaskOrderPath([string]$Path) {
    $taskFull = [IO.Path]::GetFullPath($Path, $taskRoot)
    if (!$taskFull.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Input/output must remain inside this workspace.' }
    $taskCursor = $taskFull
    while ($taskCursor.Length -ge $taskRoot.Length) {
        if ((Test-Path -LiteralPath $taskCursor) -and ((Get-Item -LiteralPath $taskCursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked input/output path rejected.' }
        if ($taskCursor -eq $taskRoot) { break }
        $taskCursor = Split-Path $taskCursor -Parent
    }
    return $taskFull
}
$taskInput = Resolve-TaskOrderPath $FirstSeenCsv
$taskOutput = Resolve-TaskOrderPath $OutputReport
if (!(Test-Path -LiteralPath $taskInput -PathType Leaf)) { throw 'First-seen CSV is missing.' }
if (Test-Path -LiteralPath $taskOutput) { throw 'Use a fresh output report to preserve evidence.' }
$taskNumericParsers = @{ Invariant = 0; DecimalCommaEsES = 0 }
function Get-TaskOrderNumber([string]$Value, [string]$Column, [int]$Record) {
    $taskNumber = 0.0
    if ([double]::TryParse($Value, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$taskNumber)) {
        $taskNumericParsers.Invariant++
    } elseif ($Value -match '^[+-]?\d+,\d+(?:[eE][+-]?\d+)?$' -and
        [double]::TryParse($Value, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::GetCultureInfo('es-ES'), [ref]$taskNumber)) {
        $taskNumericParsers.DecimalCommaEsES++
    } else { throw "Invalid numeric $Column in CSV record $Record. Expected invariant or decimal comma without thousands." }
    if (![double]::IsFinite($taskNumber)) { throw "Non-finite $Column in CSV record $Record." }
    return $taskNumber
}
$taskDetections = [Collections.Generic.List[object]]::new()
$taskRecord = 0
foreach ($taskDetection in Import-Csv -LiteralPath $taskInput) {
    $taskRecord++
    if ($taskRecord -gt 100000) { throw 'First-seen CSV exceeds the 100000-record analysis bound.' }
    foreach ($taskColumn in @('Session','Variant','Repetition','CaptureCompletedMs','ViewportX','ViewportY')) {
        if (!$taskDetection.PSObject.Properties[$taskColumn]) { throw "Missing first-seen column: $taskColumn" }
    }
    if ([string]::IsNullOrWhiteSpace($taskDetection.Session) -or [string]::IsNullOrWhiteSpace($taskDetection.Variant)) { throw "Missing session/variant in CSV record $taskRecord." }
    $taskRepetition = [int]::Parse($taskDetection.Repetition, [Globalization.CultureInfo]::InvariantCulture)
    $taskCapture = Get-TaskOrderNumber $taskDetection.CaptureCompletedMs 'CaptureCompletedMs' $taskRecord
    if ($taskRepetition -lt 0 -or $taskCapture -lt 0) { throw "Negative repetition/capture time in CSV record $taskRecord." }
    $taskDetections.Add([pscustomobject]@{
        Session = [string]$taskDetection.Session
        Variant = [string]$taskDetection.Variant
        Repetition = $taskRepetition
        CaptureMs = $taskCapture
        ViewportX = Get-TaskOrderNumber $taskDetection.ViewportX 'ViewportX' $taskRecord
        ViewportY = Get-TaskOrderNumber $taskDetection.ViewportY 'ViewportY' $taskRecord
        FixtureLabel = [string]$taskDetection.Fixture
        RowIndex = -1
    })
}
if (!$taskDetections.Count) { throw 'First-seen CSV has no detection records.' }
$taskRepetitions = [Collections.Generic.List[object]]::new()
foreach ($taskGroup in $taskDetections | Group-Object Session,Variant,Repetition) {
    $taskFirst = $taskGroup.Group[0]
    $taskRowGroups = [Collections.Generic.List[object]]::new()
    foreach ($taskDetection in $taskGroup.Group | Sort-Object ViewportY,ViewportX,CaptureMs) {
        $taskRow = if ($taskRowGroups.Count) { $taskRowGroups[$taskRowGroups.Count - 1] } else { $null }
        if (!$taskRow -or $taskDetection.ViewportY - $taskRow.AnchorY -gt $RowTolerancePixels) {
            $taskRow = [pscustomobject]@{ Index = $taskRowGroups.Count; AnchorY = $taskDetection.ViewportY; Records = [Collections.Generic.List[object]]::new() }
            $taskRowGroups.Add($taskRow)
        }
        $taskDetection.RowIndex = $taskRow.Index
        $taskRow.Records.Add($taskDetection)
    }
    $taskRows = @(
        foreach ($taskRow in $taskRowGroups) {
            $taskTimes = @($taskRow.Records.CaptureMs | Sort-Object)
            [pscustomobject]@{
                RowIndex = $taskRow.Index
                AnchorViewportY = $taskRow.AnchorY
                MinimumViewportY = ($taskRow.Records | Measure-Object ViewportY -Minimum).Minimum
                MaximumViewportY = ($taskRow.Records | Measure-Object ViewportY -Maximum).Maximum
                DetectionRecords = $taskRow.Records.Count
                FirstDetectionCaptureMs = $taskTimes[0]
                LastDetectionCaptureMs = $taskTimes[-1]
                CompleteAmongObservedRecordsMs = $taskTimes[-1]
            }
        }
    )
    $taskTransitions = @(
        for ($taskIndex = 0; $taskIndex -lt $taskRows.Count - 1; $taskIndex++) {
            $taskUpper = $taskRows[$taskIndex]; $taskLower = $taskRows[$taskIndex + 1]
            [pscustomobject]@{
                UpperRowIndex = $taskUpper.RowIndex
                LowerRowIndex = $taskLower.RowIndex
                UpperCompleteMs = $taskUpper.CompleteAmongObservedRecordsMs
                LowerFirstDetectionMs = $taskLower.FirstDetectionCaptureMs
                LowerCompleteMs = $taskLower.CompleteAmongObservedRecordsMs
                UpperCompleteBeforeOrSameCaptureAsLowerComplete = $taskUpper.CompleteAmongObservedRecordsMs -le $taskLower.CompleteAmongObservedRecordsMs
                UpperCompleteBeforeOrSameCaptureAsLowerStart = $taskUpper.CompleteAmongObservedRecordsMs -le $taskLower.FirstDetectionCaptureMs
            }
        }
    )
    $taskByTime = @($taskGroup.Group | Sort-Object CaptureMs,RowIndex,ViewportX)
    $taskFirstCapture = $taskByTime[0].CaptureMs
    $taskEarliest = @($taskByTime | Where-Object CaptureMs -eq $taskFirstCapture)
    $taskEarliestRows = @($taskEarliest.RowIndex | Sort-Object -Unique)
    $taskAvailableFour = [Math]::Min(4, $taskByTime.Count)
    $taskCutoff = $taskByTime[$taskAvailableFour - 1].CaptureMs
    $taskFourCandidates = @($taskByTime | Where-Object CaptureMs -le $taskCutoff)
    $taskBeforeCutoff = @($taskByTime | Where-Object CaptureMs -lt $taskCutoff).Count
    $taskAtCutoff = @($taskByTime | Where-Object CaptureMs -eq $taskCutoff).Count
    $taskUniquePositions = @($taskGroup.Group | Select-Object ViewportX,ViewportY -Unique).Count
    $taskRepetitions.Add([pscustomobject]@{
        Session = $taskFirst.Session
        Variant = $taskFirst.Variant
        Repetition = $taskFirst.Repetition
        DetectionRecords = $taskGroup.Count
        UniqueExactViewportPositions = $taskUniquePositions
        DuplicatePositionRecords = $taskGroup.Count - $taskUniquePositions
        RowCount = $taskRows.Count
        FourRowsAnd52DetectionRecords = ($taskRows.Count -eq 4 -and $taskGroup.Count -eq 52)
        FirstDetectionCaptureMs = $taskFirstCapture
        FirstCaptureRowIndices = $taskEarliestRows
        FirstCaptureIncludesTopRow = 0 -in $taskEarliestRows
        FirstCaptureOnlyTopRow = ($taskEarliestRows.Count -eq 1 -and $taskEarliestRows[0] -eq 0)
        FirstFour = [ordered]@{
            RequestedDetections = 4
            AvailableDetections = $taskAvailableFour
            FourDetectionsAvailable = $taskGroup.Count -ge 4
            CutoffCaptureMs = $taskCutoff
            CandidatesThroughCutoffCapture = $taskFourCandidates.Count
            RecordsBeforeCutoffCapture = $taskBeforeCutoff
            RecordsAtCutoffCapture = $taskAtCutoff
            TieCrossesFourthDetectionBoundary = ($taskGroup.Count -ge 4 -and $taskFourCandidates.Count -gt 4)
            ExactObservedFirstFourSetKnown = ($taskGroup.Count -ge 4 -and $taskFourCandidates.Count -eq 4)
            AllCandidatesInTopRow = if ($taskGroup.Count -ge 4) { @($taskFourCandidates | Where-Object RowIndex -ne 0).Count -eq 0 } else { $null }
            Candidates = @($taskFourCandidates | Select-Object CaptureMs,RowIndex,ViewportX,ViewportY,FixtureLabel)
            Interpretation = 'All candidates through the capture containing the fourth detected record are retained. Display sorting by row/X does not establish order within a capture.'
        }
        RowTransitionsApplicable = $taskTransitions.Count -gt 0
        RowCompletionsTopToBottom = if ($taskTransitions.Count) { @($taskTransitions | Where-Object { !$_.UpperCompleteBeforeOrSameCaptureAsLowerComplete }).Count -eq 0 } else { $null }
        EachUpperRowCompleteBeforeNextRowStarts = if ($taskTransitions.Count) { @($taskTransitions | Where-Object { !$_.UpperCompleteBeforeOrSameCaptureAsLowerStart }).Count -eq 0 } else { $null }
        Rows = $taskRows
        Transitions = $taskTransitions
    })
}
$taskSessionSummaries = @(
    foreach ($taskGroup in $taskRepetitions | Group-Object Session,Variant) {
        $taskFirst = $taskGroup.Group[0]
        [pscustomobject]@{
            Session = $taskFirst.Session
            Variant = $taskFirst.Variant
            Repetitions = $taskGroup.Count
            DetectionRecords = ($taskGroup.Group | Measure-Object DetectionRecords -Sum).Sum
            FourRow52RecordRepetitions = @($taskGroup.Group | Where-Object FourRowsAnd52DetectionRecords).Count
            FirstCaptureIncludesTopRow = @($taskGroup.Group | Where-Object FirstCaptureIncludesTopRow).Count
            FirstCaptureOnlyTopRow = @($taskGroup.Group | Where-Object FirstCaptureOnlyTopRow).Count
            RepetitionsWithFourDetections = @($taskGroup.Group | Where-Object { $_.FirstFour.FourDetectionsAvailable }).Count
            FirstFourAllCandidatesInTopRow = @($taskGroup.Group | Where-Object { $_.FirstFour.AllCandidatesInTopRow }).Count
            FirstFourBoundaryTieRepetitions = @($taskGroup.Group | Where-Object { $_.FirstFour.TieCrossesFourthDetectionBoundary }).Count
            RepetitionsWithRowTransitions = @($taskGroup.Group | Where-Object RowTransitionsApplicable).Count
            RowCompletionsTopToBottom = @($taskGroup.Group | Where-Object { $_.RowCompletionsTopToBottom -eq $true }).Count
            EachUpperRowCompleteBeforeNextRowStarts = @($taskGroup.Group | Where-Object { $_.EachUpperRowCompleteBeforeNextRowStarts -eq $true }).Count
        }
    }
)
$taskResult = [ordered]@{
    GeneratedUtc = [DateTime]::UtcNow.ToString('o')
    InputCsv = [IO.Path]::GetRelativePath($taskRoot, $taskInput)
    InputSHA256 = (Get-FileHash -LiteralPath $taskInput -Algorithm SHA256).Hash
    ScriptSHA256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    NumericParsing = 'Invariant Float first; decimal-comma es-ES fallback only for unambiguous comma decimals, without thousands. JSON numbers are culture independent.'
    NumericParserCounts = $taskNumericParsers
    RowTolerancePixels = $RowTolerancePixels
    RowGrouping = 'Ascending viewport Y; a row is anchored at its lowest Y and accepts records within the configured tolerance of that anchor. No chained merging.'
    DetectionRecords = $taskDetections.Count
    RepetitionCount = $taskRepetitions.Count
    Sessions = $taskSessionSummaries
    Repetitions = @($taskRepetitions)
    PhysicalPresentationMeasured = $false
    IdentityPerFixtureProven = $false
    CompletenessOfAllExpectedPicturesProven = $false
    Limitations = @(
        'Only existing first-seen detection records are analyzed; no UI calls, new captures or provider instrumentation.',
        'Row completion is the latest timestamp among records present in this CSV, not proof that every expected thumbnail was detected.',
        'Shared synthetic patterns do not prove individual file identity; fixture strings are observer labels.',
        'Capture ties do not establish physical event order or the exact first four items when the tie crosses rank four.',
        'Single-row repetitions have no vertical transitions; their completion-order verdicts are null. Fewer than four detections yield no first-four verdict.',
        '52 records across four rows is an observed shape check, not a unique-file or viewport-completeness guarantee.'
    )
}
$null = [IO.Directory]::CreateDirectory((Split-Path $taskOutput -Parent))
$taskJson = ($taskResult | ConvertTo-Json -Depth 12).Replace("`r`n", "`n").Replace("`n", "`r`n") + "`r`n"
$taskStream = [IO.FileStream]::new($taskOutput, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
try {
    $taskBytes = [Text.UTF8Encoding]::new($false).GetBytes($taskJson)
    $taskStream.Write($taskBytes, 0, $taskBytes.Length)
} finally { $taskStream.Dispose() }
Write-Output $taskOutput
