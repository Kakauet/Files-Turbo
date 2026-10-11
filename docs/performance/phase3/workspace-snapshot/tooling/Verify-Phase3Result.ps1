#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Delivery = 'artifacts/development-layout/managed-release-phase3-stable06',
    [string]$OutputReport = 'reports/phase3-final-verification.json',
    [string]$BuildProvenance = 'reports/phase3-build-provenance.json',
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$BuildName = 'phase3-managed-release-06',
    [string[]]$GuiSessionDirectories = @()
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
function Read-Phase3Json([string]$Path) { Get-Content -LiteralPath (Resolve-Phase3Path $Path) -Raw | ConvertFrom-Json }
function Assert-Phase3Hash([string]$Path, [string]$Expected) {
    if ((Get-FileHash -LiteralPath (Resolve-Phase3Path $Path)).Hash -ne $Expected) { throw "Evidence changed: $Path" }
}
function Get-Phase3GuiInteger($Value, [string]$Field) {
    $phase3GuiNumber = 0L
    if ($null -eq $Value -or ![long]::TryParse([string]$Value, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref]$phase3GuiNumber)) { throw "Missing or invalid GUI integer/fixture label: $Field" }
    return $phase3GuiNumber
}
function Assert-Phase3GuiBoolean($Value, [bool]$Expected, [string]$Field) {
    $phase3GuiBoolean = $false
    if ($null -eq $Value -or ![bool]::TryParse([string]$Value, [ref]$phase3GuiBoolean) -or $phase3GuiBoolean -ne $Expected) { throw "Missing or unexpected GUI Boolean: $Field" }
}
function Get-Phase3GuiNumber($Value, [Globalization.CultureInfo]$Culture, [string]$Field) {
    $phase3GuiNumber = 0.0
    if ($null -eq $Value -or ![double]::TryParse([string]$Value, [Globalization.NumberStyles]::Float, $Culture, [ref]$phase3GuiNumber) -or ![double]::IsFinite($phase3GuiNumber) -or $phase3GuiNumber -lt 0) { throw "Missing or invalid GUI numeric field for culture $($Culture.Name): $Field" }
    return $phase3GuiNumber
}
function Test-Phase3GuiData($Metadata, [object[]]$Samples, [object[]]$Requests, [string]$ExpectedLayout) {
    if ($Metadata.Status -cne 'Completed' -or $Metadata.Variant -cne 'PhaseIII') { throw 'GUI record is incomplete or belongs to a different variant.' }
    if ($Metadata.Action -cnotin @('Navigation', 'Selection', 'Scroll', 'RapidNavigation')) { throw 'GUI record is not one of the final primary actions.' }
    $phase3GuiPackageIdentity = [regex]::Match($Metadata.Package, '^FilesTurbo\.Experimental_(?<Version>\d+\.\d+\.\d+\.\d+)_x64__[a-z0-9]+$')
    if (!$phase3GuiPackageIdentity.Success -or $phase3GuiPackageIdentity.Groups['Version'].Value -ne '4.2.39.4') { throw 'GUI record does not identify the final 4.2.39.4 experimental package.' }
    if ((Resolve-Phase3Path $Metadata.InstallLocation.TrimEnd('\')) -ne $ExpectedLayout) { throw 'GUI record does not identify the final delivery layout.' }
    if ((Get-Phase3GuiInteger $Metadata.ProcessId 'ProcessId') -le 0) { throw 'GUI process ID is missing.' }
    $phase3GuiInstanceFields = @('ExperimentalInstanceCountAtStart','ExperimentalInstanceCountAtEnd','ExperimentalProcessIdsAtStart','ExperimentalProcessIdsAtEnd')
    $phase3GuiInstanceControl = 'not-recorded-uncontrolled'
    $phase3GuiStartInstances = $null
    $phase3GuiEndInstances = $null
    $phase3GuiStartProcesses = $null
    $phase3GuiEndProcesses = $null
    if (@($phase3GuiInstanceFields | Where-Object { $Metadata.PSObject.Properties[$_] }).Count) {
        if (@($phase3GuiInstanceFields | Where-Object { !$Metadata.PSObject.Properties[$_] }).Count) { throw 'GUI experimental-instance metadata is incomplete.' }
        $phase3GuiStartInstances = Get-Phase3GuiInteger $Metadata.ExperimentalInstanceCountAtStart 'ExperimentalInstanceCountAtStart'
        $phase3GuiEndInstances = Get-Phase3GuiInteger $Metadata.ExperimentalInstanceCountAtEnd 'ExperimentalInstanceCountAtEnd'
        if ($phase3GuiStartInstances -ne 1 -or $phase3GuiEndInstances -ne 1) { throw 'GUI record did not retain exactly one path-scoped experimental instance.' }
        $phase3GuiStartProcesses = @($Metadata.ExperimentalProcessIdsAtStart)
        $phase3GuiEndProcesses = @($Metadata.ExperimentalProcessIdsAtEnd)
        if ($phase3GuiStartProcesses.Count -ne 1 -or $phase3GuiEndProcesses.Count -ne 1 -or (Get-Phase3GuiInteger $phase3GuiStartProcesses[0] 'ExperimentalProcessIdsAtStart') -ne $Metadata.ProcessId -or (Get-Phase3GuiInteger $phase3GuiEndProcesses[0] 'ExperimentalProcessIdsAtEnd') -ne $Metadata.ProcessId) { throw 'GUI experimental-instance PID snapshots differ from the measured process.' }
        $phase3GuiInstanceControl = 'recorded-single-instance-at-start-and-end'
    }
    if ([string]::IsNullOrWhiteSpace($Metadata.Culture)) { throw 'GUI culture metadata is missing.' }
    $phase3GuiCulture = [Globalization.CultureInfo]::GetCultureInfo($Metadata.Culture)
    Assert-Phase3GuiBoolean $Metadata.PhysicalPresentationMeasured $false 'PhysicalPresentationMeasured'
    $phase3GuiStarted = [DateTimeOffset]::Parse($Metadata.StartedUtc, [Globalization.CultureInfo]::InvariantCulture)
    $phase3GuiCompleted = [DateTimeOffset]::Parse($Metadata.CompletedUtc, [Globalization.CultureInfo]::InvariantCulture)
    if ($phase3GuiCompleted -lt $phase3GuiStarted) { throw 'GUI session interval is reversed.' }
    $phase3GuiRepetitions = Get-Phase3GuiInteger $Metadata.Repetitions 'Repetitions'
    $phase3GuiExpectedRepetitions = if ($Metadata.Action -eq 'RapidNavigation') { 30 } else { 5 }
    if ($phase3GuiRepetitions -ne $phase3GuiExpectedRepetitions) { throw 'GUI repetitions do not meet the final primary session requirement.' }
    $phase3GuiFixtures = @($Metadata.Scenarios | ForEach-Object { Get-Phase3GuiInteger $_ 'Scenarios' })
    $phase3GuiExpectedFixtures = if ($Metadata.Action -in @('Navigation', 'RapidNavigation')) { @(1000L,10000L,50000L,100000L) } else { @(100000L) }
    if ($phase3GuiFixtures.Count -ne $phase3GuiExpectedFixtures.Count -or @(Compare-Object ($phase3GuiFixtures | Sort-Object) ($phase3GuiExpectedFixtures | Sort-Object)).Count) { throw 'GUI fixture labels are missing, duplicated or outside the required numeric fixtures.' }
    $phase3GuiModes = @('SmallIncrement','LargeIncrement','JumpEnd','JumpStart')
    $phase3GuiExpectedSamples = switch ($Metadata.Action) {
        'Navigation' { $phase3GuiRepetitions * $phase3GuiFixtures.Count }
        'Selection' { $phase3GuiRepetitions }
        'Scroll' { $phase3GuiRepetitions * $phase3GuiModes.Count }
        'RapidNavigation' { $phase3GuiRepetitions }
    }
    if ((Get-Phase3GuiInteger $Metadata.Samples 'Samples') -ne $phase3GuiExpectedSamples -or $Samples.Count -ne $phase3GuiExpectedSamples) { throw 'GUI sample count is incomplete or inconsistent with Action/Repetitions.' }
    $phase3GuiKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($phase3GuiSample in $Samples) {
        if ($phase3GuiSample.Variant -cne 'PhaseIII') { throw 'GUI CSV sample belongs to a different variant.' }
        $phase3GuiRepetition = Get-Phase3GuiInteger $phase3GuiSample.Repetition 'sample.Repetition'
        if ($phase3GuiRepetition -ge $phase3GuiRepetitions) { throw 'GUI sample repetition is outside the recorded range.' }
        foreach ($phase3GuiField in $phase3GuiSample.PSObject.Properties) {
            if ($phase3GuiField.Name.EndsWith('Ms') -and ![string]::IsNullOrWhiteSpace([string]$phase3GuiField.Value)) { $null = Get-Phase3GuiNumber $phase3GuiField.Value $phase3GuiCulture $phase3GuiField.Name }
        }
        $phase3GuiKey = [string]$phase3GuiRepetition
        if ($Metadata.Action -eq 'RapidNavigation') {
            if ($phase3GuiSample.Scenario -cne 'RapidNavigation' -or $phase3GuiSample.Metric -cne 'UIA_RapidNavigationFinal') { throw 'Rapid navigation sample has an unexpected scenario or metric.' }
            if ((Get-Phase3GuiInteger $phase3GuiSample.FinalObservedFixture 'FinalObservedFixture') -ne 1000 -or (Get-Phase3GuiInteger $phase3GuiSample.StatusCountObserved 'StatusCountObserved') -ne 1000 -or (Get-Phase3GuiInteger $phase3GuiSample.SubmittedNavigations 'SubmittedNavigations') -ne 7) { throw 'Rapid navigation did not record the final 1000-item fixture after seven setters.' }
            Assert-Phase3GuiBoolean $phase3GuiSample.ConfirmedAfterIdle $true 'ConfirmedAfterIdle'
            Assert-Phase3GuiBoolean $phase3GuiSample.TimedOut $false 'TimedOut'
            $null = Get-Phase3GuiNumber $phase3GuiSample.StatusFullCountMs $phase3GuiCulture 'StatusFullCountMs'
        }
        else {
            $phase3GuiFixture = Get-Phase3GuiInteger $phase3GuiSample.Scenario 'sample.Scenario'
            if ($phase3GuiFixture -notin $phase3GuiExpectedFixtures) { throw 'GUI sample has an unexpected numeric fixture label.' }
            $phase3GuiKey = "$phase3GuiFixture/$phase3GuiRepetition"
            switch ($Metadata.Action) {
                'Navigation' {
                    if ($phase3GuiSample.Metric -cne 'UIA_FirstViewportName') { throw 'Navigation sample has an unexpected metric.' }
                    Assert-Phase3GuiBoolean $phase3GuiSample.TimedOut $false 'TimedOut'
                    $null = Get-Phase3GuiNumber $phase3GuiSample.AccessibleNamesMs $phase3GuiCulture 'AccessibleNamesMs'
                    $null = Get-Phase3GuiNumber $phase3GuiSample.StatusFullCountMs $phase3GuiCulture 'StatusFullCountMs'
                }
                'Selection' {
                    if ($phase3GuiSample.Metric -cne 'UIA_SelectionConfirm') { throw 'Selection sample has an unexpected metric.' }
                    Assert-Phase3GuiBoolean $phase3GuiSample.Selected $true 'Selected'
                    $null = Get-Phase3GuiNumber $phase3GuiSample.CallMs $phase3GuiCulture 'CallMs'
                    $null = Get-Phase3GuiNumber $phase3GuiSample.ObservationMs $phase3GuiCulture 'ObservationMs'
                    if ($phase3GuiSample.PSObject.Properties['TimedOut']) { Assert-Phase3GuiBoolean $phase3GuiSample.TimedOut $false 'TimedOut' }
                }
                'Scroll' {
                    if ($phase3GuiSample.Metric -cne 'UIA_ScrollPatternCall' -or $phase3GuiSample.Mode -cnotin $phase3GuiModes) { throw 'Scroll sample has an unexpected metric or mode.' }
                    Assert-Phase3GuiBoolean $phase3GuiSample.Moved $true 'Moved'
                    Assert-Phase3GuiBoolean $phase3GuiSample.TimedOut $false 'TimedOut'
                    $null = Get-Phase3GuiNumber $phase3GuiSample.CallMs $phase3GuiCulture 'CallMs'
                    $null = Get-Phase3GuiNumber $phase3GuiSample.ObservationMs $phase3GuiCulture 'ObservationMs'
                    $phase3GuiBefore = Get-Phase3GuiNumber $phase3GuiSample.BeforePercent $phase3GuiCulture 'BeforePercent'
                    $phase3GuiAfter = Get-Phase3GuiNumber $phase3GuiSample.AfterPercent $phase3GuiCulture 'AfterPercent'
                    if ($phase3GuiBefore -gt 100 -or $phase3GuiAfter -gt 100 -or $phase3GuiBefore -eq $phase3GuiAfter) { throw 'Scroll sample does not corroborate its movement with valid provider percentages.' }
                    $phase3GuiKey += '/' + $phase3GuiSample.Mode
                }
            }
        }
        if (!$phase3GuiKeys.Add($phase3GuiKey)) { throw 'GUI samples contain a duplicated scenario/mode/repetition.' }
    }
    $phase3GuiExpectedRequests = if ($Metadata.Action -eq 'RapidNavigation') { $phase3GuiRepetitions * 7 } else { 0 }
    if ((Get-Phase3GuiInteger $Metadata.Requests 'Requests') -ne $phase3GuiExpectedRequests -or $Requests.Count -ne $phase3GuiExpectedRequests) { throw 'GUI setter request count is incomplete or inconsistent with Action/Repetitions.' }
    $phase3GuiRequestTargets = @('100000','10000','50000','empty','100000','empty','1000')
    for ($phase3GuiIndex = 0; $phase3GuiIndex -lt $Requests.Count; $phase3GuiIndex++) {
        $phase3GuiRequest = $Requests[$phase3GuiIndex]
        $phase3GuiPosition = $phase3GuiIndex % 7
        if ((Get-Phase3GuiInteger $phase3GuiRequest.Repetition 'request.Repetition') -ne [Math]::Floor($phase3GuiIndex / 7) -or (Get-Phase3GuiInteger $phase3GuiRequest.Position 'request.Position') -ne $phase3GuiPosition -or $phase3GuiRequest.TargetFixture -cne $phase3GuiRequestTargets[$phase3GuiPosition]) { throw 'GUI setter requests do not preserve the complete seven-target sequence for each cycle.' }
        $phase3GuiRequestStarted = Get-Phase3GuiNumber $phase3GuiRequest.StartedMs $phase3GuiCulture 'request.StartedMs'
        $phase3GuiRequestCompleted = Get-Phase3GuiNumber $phase3GuiRequest.CompletedMs $phase3GuiCulture 'request.CompletedMs'
        $null = Get-Phase3GuiNumber $phase3GuiRequest.SetValueCallMs $phase3GuiCulture 'request.SetValueCallMs'
        if ($phase3GuiRequestCompleted -lt $phase3GuiRequestStarted) { throw 'GUI setter request interval is reversed.' }
    }
    return [pscustomobject]@{ Action = $Metadata.Action; Package = $Metadata.Package; Version = '4.2.39.4'; InstallLocation = $Metadata.InstallLocation; ProcessId = $Metadata.ProcessId; Culture = $phase3GuiCulture.Name; FixtureCounts = $phase3GuiFixtures; Repetitions = $phase3GuiRepetitions; VerifiedSamples = $Samples.Count; VerifiedSetterRequests = $Requests.Count; InstanceControl = $phase3GuiInstanceControl; ExperimentalInstanceCountAtStart = $phase3GuiStartInstances; ExperimentalInstanceCountAtEnd = $phase3GuiEndInstances; ExperimentalProcessIdsAtStart = $phase3GuiStartProcesses; ExperimentalProcessIdsAtEnd = $phase3GuiEndProcesses; AllRequiredObservationsConfirmed = $true; NoPhysicalPresentation = $true }
}
function Test-Phase3GuiSessions([string[]]$Directories, [string]$ExpectedLayout) {
    if (!$Directories.Count) { return [pscustomobject]@{ Status = 'not-supplied'; NoPhysicalPresentation = $true; Scope = 'Optional final UIA record validation; physical frames, presentation and FPS are not measured.'; Records = @() } }
    if ($Directories.Count -ne 4) { throw 'Supply exactly four primary GUI session directories: Navigation, Selection, Scroll and RapidNavigation.' }
    $phase3GuiRecords = @()
    $phase3GuiSeenDirectories = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($phase3GuiDirectory in $Directories) {
        $phase3GuiFullDirectory = Resolve-Phase3Path $phase3GuiDirectory
        if (!(Test-Path -LiteralPath $phase3GuiFullDirectory -PathType Container) -or !$phase3GuiSeenDirectories.Add($phase3GuiFullDirectory)) { throw 'GUI session directory is missing or duplicated.' }
        $phase3GuiRelative = [IO.Path]::GetRelativePath($phase3Root, $phase3GuiFullDirectory).Replace('\', '/')
        $phase3GuiMetadataPath = $phase3GuiRelative + '/session.json'
        $phase3GuiSamplesPath = $phase3GuiRelative + '/samples.csv'
        $phase3GuiMetadata = Read-Phase3Json $phase3GuiMetadataPath
        $phase3GuiSamples = @(Import-Csv -LiteralPath (Resolve-Phase3Path $phase3GuiSamplesPath))
        $phase3GuiRequestsPath = $phase3GuiRelative + '/requests.csv'
        $phase3GuiRequests = if ($phase3GuiMetadata.Action -eq 'RapidNavigation') { @(Import-Csv -LiteralPath (Resolve-Phase3Path $phase3GuiRequestsPath)) } else { @() }
        $phase3GuiRecord = Test-Phase3GuiData $phase3GuiMetadata $phase3GuiSamples $phase3GuiRequests $ExpectedLayout
        $phase3GuiRecord | Add-Member -NotePropertyName Directory -NotePropertyValue $phase3GuiRelative
        $phase3GuiEvidencePaths = @($phase3GuiMetadataPath, $phase3GuiSamplesPath)
        if ($phase3GuiMetadata.Action -eq 'RapidNavigation') { $phase3GuiEvidencePaths += $phase3GuiRequestsPath }
        $phase3GuiRecord | Add-Member -NotePropertyName Evidence -NotePropertyValue @(foreach ($phase3GuiPath in $phase3GuiEvidencePaths) { [pscustomobject]@{ Path = $phase3GuiPath; SHA256 = (Get-FileHash -LiteralPath (Resolve-Phase3Path $phase3GuiPath)).Hash } })
        $phase3GuiRecords += $phase3GuiRecord
    }
    if (@(Compare-Object @('Navigation','Selection','Scroll','RapidNavigation' | Sort-Object) @($phase3GuiRecords.Action | Sort-Object)).Count) { throw 'Required GUI actions are missing or duplicated.' }
    return [pscustomobject]@{ Status = 'primary-uia-records-validated'; NoPhysicalPresentation = $true; Scope = 'Completed UIA records from the final experimental version/layout, fixture labels, Action/Repetitions coverage, selection/movement/final idle confirmation and individual setter records; instance snapshots checked when recorded. No physical frames, presentation, FPS or timing comparison.'; NavigationSamples = ($phase3GuiRecords | Where-Object Action -eq 'Navigation').VerifiedSamples; SelectionSamples = ($phase3GuiRecords | Where-Object Action -eq 'Selection').VerifiedSamples; ScrollSamples = ($phase3GuiRecords | Where-Object Action -eq 'Scroll').VerifiedSamples; RapidCycles = ($phase3GuiRecords | Where-Object Action -eq 'RapidNavigation').VerifiedSamples; SetterRequests = ($phase3GuiRecords | Where-Object Action -eq 'RapidNavigation').VerifiedSetterRequests; AllSessionsRecordSingleInstance = (@($phase3GuiRecords | Where-Object InstanceControl -ne 'recorded-single-instance-at-start-and-end').Count -eq 0); Records = $phase3GuiRecords }
}
function Test-Phase3Layout([string]$Relative) {
    $phase3PackageRoot = Resolve-Phase3Path $Relative
    $phase3Validation = Read-Phase3Json ($Relative + '/package-validation.json')
    if ($phase3Validation.Status -ne 'content-validated-not-installed' -or !$phase3Validation.ManifestUnchanged -or $phase3Validation.Signed) { throw "Unexpected package validation state: $Relative" }
    if ((Resolve-Phase3Path $phase3Validation.Layout) -ne (Resolve-Phase3Path ($Relative + '/layout'))) { throw 'Package validation belongs to a different layout.' }
    $phase3Manifest = [xml][IO.File]::ReadAllText((Resolve-Phase3Path ($Relative + '/layout/AppxManifest.xml')))
    if ($phase3Manifest.Package.Identity.Name -ne 'FilesTurbo.Experimental' -or $phase3Manifest.Package.Identity.Publisher -ne 'CN=Files') { throw 'Package identity is not the isolated experimental identity.' }
    Assert-Phase3Hash $phase3Validation.OutputPackage $phase3Validation.OutputSHA256
    foreach ($phase3Entry in $phase3Validation.Inventory) {
        $phase3File = Resolve-Phase3Path ($Relative + '/layout/' + $phase3Entry.Path)
        if (!$phase3File.StartsWith($phase3PackageRoot + '\layout\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Package inventory path escaped layout.' }
        if ((Get-Item -LiteralPath $phase3File).Length -ne $phase3Entry.Length) { throw "Layout length differs: $($phase3Entry.Path)" }
        Assert-Phase3Hash $phase3File $phase3Entry.SHA256
    }
    $phase3LayoutFiles = @(Get-ChildItem -LiteralPath (Resolve-Phase3Path ($Relative + '/layout')) -File -Recurse -Force)
    if ($phase3LayoutFiles.Count -ne $phase3Validation.Inventory.Count -or @($phase3Validation.Inventory | Select-Object -ExpandProperty Path -Unique).Count -ne $phase3Validation.Inventory.Count) { throw "Unrecorded layout files or incomplete inventory: $Relative" }
    foreach ($phase3Entry in $phase3Validation.DependencyInventory) {
        $phase3File = Resolve-Phase3Path ($Relative + '/Dependencies/' + $phase3Entry.Path)
        if (!$phase3File.StartsWith($phase3PackageRoot + '\Dependencies\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Dependency inventory path escaped dependency directory.' }
        if ((Get-Item -LiteralPath $phase3File).Length -ne $phase3Entry.Length) { throw "Dependency length differs: $($phase3Entry.Path)" }
        Assert-Phase3Hash $phase3File $phase3Entry.SHA256
    }
    $phase3DependencyFiles = @(Get-ChildItem -LiteralPath (Resolve-Phase3Path ($Relative + '/Dependencies')) -File -Recurse -Force)
    if ($phase3DependencyFiles.Count -ne $phase3Validation.DependencyInventory.Count -or @($phase3Validation.DependencyInventory | Select-Object -ExpandProperty Path -Unique).Count -ne $phase3Validation.DependencyInventory.Count) { throw "Unrecorded dependency files or incomplete inventory: $Relative" }
    return $phase3Validation
}
$phase3ReportPath = Resolve-Phase3Path $OutputReport
if (Test-Path -LiteralPath $phase3ReportPath) { throw 'Report already exists; use a fresh output path to preserve evidence.' }
$phase3Repository = Resolve-Phase3Path 'source'
$phase3Original = '1994d9a4269f5144c70bc67d6a48de4a20616912'
$phase3PhaseII = 'b18efc91ed3033081691f407a2bb7b5ee3d7d21d'
if ((& git -C (Resolve-Phase3Path 'source-baseline') rev-parse HEAD) -ne $phase3Original) { throw 'Original baseline reference changed.' }
if (@(& git -C (Resolve-Phase3Path 'source-baseline') status --porcelain).Count) { throw 'Original baseline working tree changed.' }
foreach ($phase3Reference in @('files-turbo-phase2', 'files-turbo-phase3-start')) {
    if ((& git -C $phase3Repository rev-parse $phase3Reference) -ne $phase3PhaseII) { throw "Exact Phase II reference changed: $phase3Reference" }
}
& git -C $phase3Repository diff --check files-turbo-phase3-start
if ($LASTEXITCODE -ne 0) { throw 'Phase III source diff check failed.' }
$phase3Tracked = @(& git -C $phase3Repository diff files-turbo-phase3-start --name-only -- src)
$phase3Untracked = @(& git -C $phase3Repository ls-files --others --exclude-standard -- src)
$phase3Changes = @($phase3Tracked + $phase3Untracked | Sort-Object -Unique)
$phase3Sources = foreach ($phase3Relative in $phase3Changes) {
    $phase3File = Resolve-Phase3Path ('source/' + $phase3Relative)
    if ($phase3Relative -match '\.(cs|xaml|appxmanifest|txt|xml|json|csproj|props|targets|md)$' -and [IO.File]::ReadAllText($phase3File) -match '(?<!\r)\n') { throw "Changed source requires CRLF: $phase3Relative" }
    [pscustomobject]@{ Path = $phase3Relative; SHA256 = (Get-FileHash -LiteralPath $phase3File).Hash }
}
$phase3ScriptPaths = @((Get-ChildItem -LiteralPath (Resolve-Phase3Path 'tooling') -Filter '*.ps1' -File).FullName)
foreach ($phase3Directory in @('tests-extra/Phase3Navigation', 'tests-extra/Viewport', 'tests-extra/EntryConstruction', 'tests-extra/Adaptive', 'tests-extra/Phase3PathNavigation', 'tests-extra/Phase3WorkingDirectory', 'benchmarks/Phase3')) {
    if (Test-Path -LiteralPath (Resolve-Phase3Path $phase3Directory)) {
        $phase3ScriptPaths += @((Get-ChildItem -LiteralPath (Resolve-Phase3Path $phase3Directory) -Filter '*.ps1' -File -Recurse | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }).FullName)
    }
}
$phase3Parses = foreach ($phase3Script in @($phase3ScriptPaths | Sort-Object -Unique)) {
    $phase3SafeScript = Resolve-Phase3Path $phase3Script
    $phase3Tokens = $null; $phase3Errors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile($phase3SafeScript, [ref]$phase3Tokens, [ref]$phase3Errors)
    if ($phase3Errors.Count) { throw "PowerShell parse failed: $phase3SafeScript" }
    [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($phase3Root, $phase3SafeScript); SHA256 = (Get-FileHash -LiteralPath $phase3SafeScript).Hash }
}
$phase3Package = Test-Phase3Layout $Delivery
$phase3RecoveryPackage = Test-Phase3Layout 'artifacts/development-layout/managed-release-phase2'
$phase3HistoricalCopies = @(
    @('MORNING_REPORT.md', 'artifacts/phase2-recovery/MORNING_REPORT.md'),
    @('reports/final-verification.json', 'artifacts/phase2-recovery/final-verification.json')
)
foreach ($phase3Pair in $phase3HistoricalCopies) { Assert-Phase3Hash $phase3Pair[0] (Get-FileHash -LiteralPath (Resolve-Phase3Path $phase3Pair[1])).Hash }
$phase3HistoricalReports = @('MORNING_REPORT.md', 'MORNING_REPORT_PHASE2.md', 'PERFORMANCE_REPORT.md', 'README_FILES_TURBO.md') + @(
    Get-ChildItem -LiteralPath (Resolve-Phase3Path 'reports') -File | Where-Object { $_.Name -match '^(PHASE2_|phase2-|DELIVERY\.md$|final-verification\.json$)' } | ForEach-Object { [IO.Path]::GetRelativePath($phase3Root, $_.FullName) }
)
$phase3HistoricalInventory = foreach ($phase3HistoricalPath in @($phase3HistoricalReports | Sort-Object -Unique)) {
    [pscustomobject]@{ Path = $phase3HistoricalPath; SHA256 = (Get-FileHash -LiteralPath (Resolve-Phase3Path $phase3HistoricalPath)).Hash }
}
$phase3Provenance = Read-Phase3Json $BuildProvenance
$phase3Head = (& git -C $phase3Repository rev-parse HEAD)
if ($phase3Provenance.Status -ne 'compiled-source-and-packaged-assembly-match' -or $phase3Provenance.SourceCommit -ne $phase3Head -or $phase3Provenance.PhaseIIIStartCommit -ne $phase3PhaseII) { throw 'Build provenance does not identify the current source commit and exact Phase II start.' }
if ($phase3Provenance.SourceWorkingTreeDirty -or @(& git -C $phase3Repository status --porcelain).Count) { throw 'Final provenance requires a clean committed Phase III source tree.' }
$phase3ExpectedCs = @($phase3Changes | Where-Object { $_.EndsWith('.cs') })
if (@($phase3Provenance.Documents | Select-Object -ExpandProperty Path -Unique).Count -ne $phase3ExpectedCs.Count -or $phase3Provenance.ChangedCSharpCount -ne $phase3ExpectedCs.Count) { throw 'Final compiled source document coverage is incomplete.' }
$phase3ActualCs = @($phase3Provenance.Documents | Select-Object -ExpandProperty Path -Unique | Sort-Object)
if (@(Compare-Object @($phase3ExpectedCs | Sort-Object) $phase3ActualCs).Count) { throw 'Compiled document paths do not match changed production C# paths.' }
$phase3ExpectedInputs = @($phase3Changes | Where-Object { $_.EndsWith('.xaml') -or $_.EndsWith('.appxmanifest') -or ($_ -like 'src/Files.App.CsWin32/*' -and $_.EndsWith('.txt')) } | Sort-Object)
$phase3ActualInputs = @($phase3Provenance.SourceInputs | Select-Object -ExpandProperty Path -Unique | Sort-Object)
if (@(Compare-Object $phase3ExpectedInputs $phase3ActualInputs).Count) { throw 'XAML, manifest or CsWin32 source-input hash coverage is incomplete.' }
foreach ($phase3Entry in @($phase3Provenance.Documents) + @($phase3Provenance.SourceInputs)) { Assert-Phase3Hash ('source/' + $phase3Entry.Path) $phase3Entry.SHA256 }
foreach ($phase3Entry in $phase3Provenance.Assemblies) {
    if (!$phase3Entry.PdbIdentityMatchesAssembly) { throw 'Assembly/PDB identity not verified.' }
    Assert-Phase3Hash ($Delivery + '/layout/' + $phase3Entry.LayoutPath) $phase3Entry.SHA256
    Assert-Phase3Hash $phase3Entry.PdbPath $phase3Entry.PdbSHA256
}
Assert-Phase3Hash ('source/' + $phase3Provenance.Manifest.Path) $phase3Provenance.Manifest.SourceSHA256
Assert-Phase3Hash ($Delivery + '/layout/AppxManifest.xml') $phase3Provenance.Manifest.LayoutSHA256
$phase3Navigation = Read-Phase3Json 'reports/phase3-navigation-shell-contracts-final-04.json'
$phase3Viewport = Read-Phase3Json 'reports/phase3-viewport-contracts-final.json'
$phase3SearchPipeline = @(Read-Phase3Json 'artifacts/phase3-search-verified/contracts-additional/search-pipeline-contracts.json')
$phase3SearchNative = @(Read-Phase3Json 'artifacts/phase3-search-verified/native-contracts/search-contracts.json')
$phase3SearchTraversal = @(Read-Phase3Json 'artifacts/phase3-search-verified/search-traversal-contracts.json')
if ($phase3Navigation.results.Count -ne 22 -or $phase3Viewport.contracts.Count -ne 17 -or $phase3SearchPipeline.Count -ne 11 -or $phase3SearchNative.Count -ne 28 -or $phase3SearchTraversal.Count -ne 3) { throw 'Expected Phase III contract evidence is incomplete.' }
if (@($phase3Navigation.results | Where-Object { !$_.passed }).Count -or @($phase3Viewport.contracts | Where-Object { !$_.Passed }).Count -or @($phase3SearchPipeline | Where-Object { !$_.Passed }).Count -or @($phase3SearchNative | Where-Object Status -ne passed).Count -or @($phase3SearchTraversal | Where-Object { !$_.passed }).Count) { throw 'A required Phase III contract failed.' }
$phase3NavigationHash = [IO.File]::ReadAllText((Resolve-Phase3Path 'tests-extra/Phase3Navigation/source-hash.txt')).Trim()
Assert-Phase3Hash 'source/src/Files.App/ViewModels/ShellViewModel.cs' $phase3NavigationHash
Assert-Phase3Hash $phase3Viewport.productionSource $phase3Viewport.productionSourceSha256
$phase3SearchMetadata = Read-Phase3Json 'artifacts/phase3-search-verified/run-metadata.json'
Assert-Phase3Hash 'source/src/Files.App/Utils/Storage/Search/NativeNameSearch.cs' $phase3SearchMetadata.sourceNativeProviderSHA256
Assert-Phase3Hash 'source/src/Files.App/Utils/Storage/Search/SearchSuggestionPipeline.cs' $phase3SearchMetadata.sourceSuggestionPipelineSHA256
$phase3EntryContracts = Read-Phase3Json 'artifacts/phase3-entry-construction-final-02/entry-contracts.json'
if ($phase3EntryContracts.results.Count -ne 16 -or @($phase3EntryContracts.results | Where-Object { !$_.Passed }).Count) { throw 'Entry construction and solution detection contracts are incomplete or failed.' }
if ($phase3EntryContracts.source.OriginalCommit -ne $phase3PhaseII) { throw 'Entry construction contracts do not compare the exact Phase II source.' }
$phase3ExpectedEntrySources = @('source/src/Files.App/Data/Items/ListedItem.cs', 'source/src/Files.App/Utils/Storage/Enumerators/Win32StorageEnumerator.cs', 'source/src/Files.Shared/Helpers/FileExtensionHelpers.cs', 'source/src/Files.App/Utils/Storage/StorageItems/ZipStorageFolder.cs', 'source/src/Files.App/ViewModels/ShellViewModel.cs')
if (@(Compare-Object @($phase3ExpectedEntrySources | Sort-Object) @($phase3EntryContracts.source.CurrentSource | Select-Object -ExpandProperty Path | Sort-Object)).Count) { throw 'Entry construction source coverage is incomplete.' }
foreach ($phase3Entry in $phase3EntryContracts.source.CurrentSource) { Assert-Phase3Hash $phase3Entry.Path $phase3Entry.SHA256 }
if ($phase3EntryContracts.source.SnapshotName -ne 'verified-final04' -or $phase3EntryContracts.source.ExtractedSource.Count -ne 10 -or $phase3EntryContracts.source.HarnessSource.Count -ne 3) { throw 'Entry construction extraction or harness metadata is incomplete.' }
foreach ($phase3Entry in $phase3EntryContracts.source.ExtractedSource) { Assert-Phase3Hash ('tests-extra/EntryConstruction/snapshots/verified-final04/' + $phase3Entry.Path) $phase3Entry.SHA256 }
foreach ($phase3Entry in $phase3EntryContracts.source.HarnessSource) { Assert-Phase3Hash $phase3Entry.Path $phase3Entry.SHA256 }
$phase3Adaptive = Read-Phase3Json 'reports/phase3-adaptive-contracts-final.json'
if ($phase3Adaptive.Passed -ne 17 -or $phase3Adaptive.Failed -ne 0 -or $phase3Adaptive.Results.Count -ne 17 -or @($phase3Adaptive.Results | Where-Object { !$_.Passed }).Count) { throw 'Adaptive layout contracts are incomplete or failed.' }
if ((Resolve-Phase3Path $phase3Adaptive.ProductionSource) -ne (Resolve-Phase3Path 'source/src/Files.App/Helpers/Layout/AdaptiveLayoutHelpers.cs')) { throw 'Adaptive contracts identify an unexpected production source.' }
Assert-Phase3Hash $phase3Adaptive.ProductionSource $phase3Adaptive.SourceSha256
$phase3PathNavigation = Read-Phase3Json 'reports/phase3-path-navigation-contracts-final-02.json'
if ($phase3PathNavigation.Passed -ne 37 -or $phase3PathNavigation.Failed -ne 0 -or $phase3PathNavigation.Results.Count -ne 37 -or @($phase3PathNavigation.Results | Where-Object { !$_.Passed }).Count) { throw 'Toolbar path navigation contracts are incomplete or failed.' }
$phase3ExpectedPathSources = @('source/src/Files.App/ViewModels/UserControls/NavigationToolbarViewModel.cs', 'source/src/Files.App/Helpers/Navigation/LatestPathNavigationCoordinator.cs')
if (@(Compare-Object @($phase3ExpectedPathSources | Sort-Object) @($phase3PathNavigation.ProductionSources | Select-Object -ExpandProperty Path | Sort-Object)).Count) { throw 'Toolbar navigation source coverage is incomplete.' }
foreach ($phase3Entry in $phase3PathNavigation.ProductionSources) { Assert-Phase3Hash $phase3Entry.Path $phase3Entry.Sha256 }
Assert-Phase3Hash 'source/src/Files.App/ViewModels/UserControls/NavigationToolbarViewModel.cs' $phase3PathNavigation.ExtractedToolbarSha256
Assert-Phase3Hash 'tests-extra/Phase3PathNavigation/PathCore.generated.cs' $phase3PathNavigation.GeneratedMethodsSha256
$phase3WorkingDirectory = Read-Phase3Json 'reports/phase3-working-directory-contracts-final-01.json'
if (!$phase3WorkingDirectory.passed -or $phase3WorkingDirectory.total -ne 41 -or $phase3WorkingDirectory.failures -ne 0 -or $phase3WorkingDirectory.results.Count -ne 41 -or @($phase3WorkingDirectory.results | Where-Object { !$_.passed }).Count) { throw 'Working directory and breadcrumb contracts are incomplete or failed.' }
$phase3ExpectedWorkingMembers = @(
    @('source/src/Files.App/ViewModels/ShellViewModel.cs', 'public async Task SetWorkingDirectoryAsync(string? value)', 'WorkingDirectoryCore.generated.cs'),
    @('source/src/Files.App/ViewModels/ShellViewModel.cs', 'public async Task UpdateFolderThumbnailImageSource()', 'FolderThumbnailCore.generated.cs'),
    @('source/src/Files.App/ViewModels/ShellViewModel.cs', 'public bool IsWorkingDirectoryUpdatePending =>', 'WorkingDirectoryPending.generated.cs'),
    @('source/src/Files.App/Views/Shells/BaseShellPage.cs', 'public async Task UpdatePathUIToWorkingDirectoryAsync(string? newWorkingDir, string? singleItemOverride = null)', 'BreadcrumbCore.generated.cs'),
    @('source/src/Files.App/Views/Shells/ModernShellPage.xaml.cs', 'public override void NavigateToPath(string? navigationPath, Type? sourcePageType, NavigationArguments? navArgs = null)', 'ModernRouteCore.generated.cs')
)
if ($phase3WorkingDirectory.sources.Sources.Count -ne $phase3ExpectedWorkingMembers.Count) { throw 'Working directory extraction coverage is incomplete.' }
foreach ($phase3WorkingMember in $phase3ExpectedWorkingMembers) {
    $phase3Matches = @($phase3WorkingDirectory.sources.Sources | Where-Object { $_.Source -eq $phase3WorkingMember[0] -and $_.Signature -eq $phase3WorkingMember[1] -and $_.Generated -eq $phase3WorkingMember[2] -and $_.Verbatim })
    if ($phase3Matches.Count -ne 1) { throw 'Required working directory member extraction is missing or duplicated.' }
    $phase3Member = $phase3Matches[0]
    Assert-Phase3Hash $phase3Member.Source $phase3Member.SourceSha256
    Assert-Phase3Hash ('tests-extra/Phase3WorkingDirectory/' + $phase3Member.Generated) $phase3Member.GeneratedSha256
    $phase3SourceText = [IO.File]::ReadAllText((Resolve-Phase3Path $phase3Member.Source))
    $phase3MemberStart = $phase3SourceText.IndexOf($phase3Member.Signature, [StringComparison]::Ordinal)
    if ($phase3MemberStart -lt 0) { throw 'Working directory production member is missing.' }
    if ($phase3Member.Signature.EndsWith('=>')) { $phase3MemberEnd = $phase3SourceText.IndexOf(';', $phase3MemberStart) + 1 }
    else {
        $phase3MemberBrace = $phase3SourceText.IndexOf('{', $phase3MemberStart)
        $phase3MemberDepth = 1
        $phase3MemberEnd = $phase3MemberBrace + 1
        while ($phase3MemberEnd -lt $phase3SourceText.Length -and $phase3MemberDepth -gt 0) {
            if ($phase3SourceText[$phase3MemberEnd] -eq '{') { $phase3MemberDepth++ }
            elseif ($phase3SourceText[$phase3MemberEnd] -eq '}') { $phase3MemberDepth-- }
            $phase3MemberEnd++
        }
        if ($phase3MemberDepth) { throw 'Working directory production member has unbalanced braces.' }
    }
    if ($phase3MemberEnd -le $phase3MemberStart) { throw 'Working directory production member could not be extracted.' }
    $phase3MemberBytes = [Text.Encoding]::UTF8.GetBytes($phase3SourceText.Substring($phase3MemberStart, $phase3MemberEnd - $phase3MemberStart))
    if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($phase3MemberBytes)) -ne $phase3Member.MethodUtf8Sha256) { throw 'Working directory literal production method differs from the compiled extraction.' }
}
$phase3SearchRows = @(Import-Csv -LiteralPath (Resolve-Phase3Path 'artifacts/phase3-search-verified/pipeline/pipeline-publication.csv'))
$phase3ExpectedSamples = 6 * 4 * 6 * [int]$phase3SearchMetadata.measuredRepetitionsPerPass
if ($phase3SearchRows.Count -ne $phase3ExpectedSamples) { throw 'Search pipeline sample count is incomplete.' }
foreach ($phase3Group in ($phase3SearchRows | Group-Object count,scenario)) {
    if (@($phase3Group.Group | Select-Object -ExpandProperty variant -Unique).Count -ne 3 -or @($phase3Group.Group | Select-Object -ExpandProperty result_hash -Unique).Count -ne 1) { throw "Search suggestion result sets differ: $($phase3Group.Name)" }
}
if (!$BuildName) {
    $phase3LatestBuild = Get-ChildItem -LiteralPath (Resolve-Phase3Path 'reports') -Filter 'phase3-*-final-build.log' -File | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if (!$phase3LatestBuild) { throw 'Final Phase III build log missing.' }
    $BuildName = $phase3LatestBuild.Name -replace '-final-build\.log$', ''
}
$phase3BuildLog = "reports/$BuildName-final-build.log"
$phase3BuildText = [IO.File]::ReadAllText((Resolve-Phase3Path $phase3BuildLog))
if ($phase3BuildText -notmatch '(?m)^\s*0 (?:Error\(s\)|Errores)\s*$') { throw 'Final integrated build did not report zero errors.' }
$phase3Warnings = [regex]::Match($phase3BuildText, '(?m)^\s*(\d+) (?:Warning\(s\)|Advertencia\(s\))\s*$').Groups[1].Value
if (!$phase3Warnings) { throw 'Final integrated warning count missing.' }
$phase3EvidencePaths = @($BuildProvenance, $phase3BuildLog, 'reports/phase3-navigation-shell-contracts-final-04.json', 'reports/phase3-viewport-contracts-final.json', 'artifacts/phase3-search-verified/contracts-additional/search-pipeline-contracts.json', 'artifacts/phase3-search-verified/native-contracts/search-contracts.json', 'artifacts/phase3-search-verified/search-traversal-contracts.json', 'artifacts/phase3-search-verified/run-metadata.json', 'artifacts/phase3-search-verified/pipeline/pipeline-publication.csv', 'artifacts/phase3-search-verified/pipeline/summary-corrected.csv', 'artifacts/phase3-entry-construction-final-02/entry-contracts.json', 'artifacts/phase3-entry-construction-build/build-final-02.log', 'reports/phase3-adaptive-contracts-final.json', 'reports/phase3-path-navigation-contracts-final-02.json', 'reports/phase3-working-directory-contracts-final-01.json', 'tests-extra/Phase3WorkingDirectory/source-hashes.json')
$phase3Evidence = foreach ($phase3EvidencePath in $phase3EvidencePaths) { [pscustomobject]@{ Path = $phase3EvidencePath; SHA256 = (Get-FileHash -LiteralPath (Resolve-Phase3Path $phase3EvidencePath)).Hash } }
$phase3GuiVerification = Test-Phase3GuiSessions $GuiSessionDirectories (Resolve-Phase3Path ($Delivery + '/layout'))
$phase3Result = [ordered]@{
    VerifiedUtc = [DateTime]::UtcNow.ToString('o')
    Status = $(if ($GuiSessionDirectories.Count) { 'content-components-and-primary-gui-records-validated' } else { 'content-and-components-validated-runtime-reported-separately' })
    Scope = 'Exact source references, clean committed source, CRLF, script syntax, Release/PDB/source provenance, complete recorded package/dependency hashes, directed component contracts and optional final primary UIA record coverage. Physical presentation, FPS and broad COM behavior are not validated by this record.'
    BaselineCommit = $phase3Original
    PhaseIICommit = $phase3PhaseII
    PhaseIIICommit = $phase3Head
    BaselineClean = $true
    HistoricalSnapshotCopiesVerified = @($phase3HistoricalCopies)
    HistoricalReportInventory = @($phase3HistoricalInventory)
    PhaseIIRecoveryPackageSHA256 = $phase3RecoveryPackage.OutputSHA256
    Source = @($phase3Sources)
    ScriptParses = @($phase3Parses)
    Contracts = @{ Navigation = $phase3Navigation.results.Count; Viewport = $phase3Viewport.contracts.Count; SearchPipeline = $phase3SearchPipeline.Count; SearchProvider = $phase3SearchNative.Count; RecursiveSearch = $phase3SearchTraversal.Count; EntryConstruction = $phase3EntryContracts.results.Count; AdaptiveLayout = $phase3Adaptive.Results.Count; ToolbarPathNavigation = $phase3PathNavigation.Results.Count; WorkingDirectory = $phase3WorkingDirectory.results.Count; Total = $phase3Navigation.results.Count + $phase3Viewport.contracts.Count + $phase3SearchPipeline.Count + $phase3SearchNative.Count + $phase3SearchTraversal.Count + $phase3EntryContracts.results.Count + $phase3Adaptive.Results.Count + $phase3PathNavigation.Results.Count + $phase3WorkingDirectory.results.Count }
    SearchBenchmark = @{ Samples = $phase3SearchRows.Count; ResultSetsEquivalent = $true; Metric = 'NTFS names and debounce-to-harness-callback; not visible UI latency' }
    GuiRecords = $phase3GuiVerification
    Evidence = @($phase3Evidence)
    IntegratedBuild = @{ Configuration = 'Release managed x64'; Errors = 0; Warnings = [int]$phase3Warnings; CompiledChangedCSharpDocuments = $phase3ExpectedCs.Count }
    Package = @{ Delivery = $Delivery; Path = [IO.Path]::GetRelativePath($phase3Root, $phase3Package.OutputPackage); SHA256 = $phase3Package.OutputSHA256; ContentFiles = $phase3Package.Inventory.Count; Dependencies = $phase3Package.DependencyInventory.Count; Signed = $false; Identity = 'FilesTurbo.Experimental'; Version = $phase3Provenance.Manifest.Version; SourceManifestSHA256 = $phase3Provenance.Manifest.SourceSHA256 }
}
$phase3Json = $phase3Result | ConvertTo-Json -Depth 10
$phase3Output = [IO.FileStream]::new($phase3ReportPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
try {
    $phase3Bytes = [Text.UTF8Encoding]::new($false).GetBytes($phase3Json)
    $phase3Output.Write($phase3Bytes, 0, $phase3Bytes.Length)
} finally { $phase3Output.Dispose() }
Write-Output $phase3Result.Status
