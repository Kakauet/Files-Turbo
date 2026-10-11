param(
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunSuffix = 'topdown-final',
    [ValidatePattern('^[0-9a-fA-F]{40}$')][string]$ExpectedSourceHead
)

$ErrorActionPreference = 'Stop'
$taskWorkspace = Split-Path $PSScriptRoot -Parent
$taskSourceRoot = Join-Path $taskWorkspace 'source'
$taskSearchRelative = "artifacts/phase4-search-regression-$RunSuffix"
$taskSearchRoot = Join-Path $taskWorkspace $taskSearchRelative
$taskIndexPath = Join-Path $taskWorkspace "reports/phase4-regression-contracts-$RunSuffix.json"
$taskInteropRelative = 'source/src/Files.App.CsWin32/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/Files.App.CsWin32.dll'
$taskInteropPath = Join-Path $taskWorkspace $taskInteropRelative
$taskCases = [Collections.Generic.List[object]]::new()
$taskBuilds = [Collections.Generic.List[object]]::new()
$taskCompiledSources = @()
$taskPreparation = @()
$taskInterop = $null
$taskStatus = 'Running'
$taskCurrentAction = 'Preflight'
$taskFailure = $null
$taskStartedUtc = [DateTimeOffset]::UtcNow.ToString('o')

function Get-TaskRelativePath([string]$Path) {
    [IO.Path]::GetRelativePath($taskWorkspace, [IO.Path]::GetFullPath($Path)).Replace('\', '/')
}

function Get-TaskSourceHead {
    $taskHead = (& git -C $taskSourceRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the production source commit.' }
    $taskHead
}

function Assert-TaskNoOtherBuild {
    $taskOtherBuilds = @(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -ieq 'MSBuild.exe' -and $_.CommandLine -notmatch '(?i)/nodemode:' -or
        $_.Name -ieq 'dotnet.exe' -and $_.CommandLine -match '(?i)(?:\s|")(?:build|msbuild|publish)(?:\s|")'
    })
    if ($taskOtherBuilds.Count -gt 0) {
        throw ('A build is already running; contract builds must be sequential. PIDs: ' + ($taskOtherBuilds.ProcessId -join ', '))
    }
}

function Assert-TaskFrozenInputs {
    if ((Get-TaskSourceHead) -cne $taskHead) { throw 'Production HEAD changed during the contract run.' }
    foreach ($taskInput in @($taskCompiledSources) + @($taskPreparation)) {
        $taskCurrentHash = (Get-FileHash -LiteralPath (Join-Path $taskWorkspace $taskInput.Path) -Algorithm SHA256).Hash
        if ($taskCurrentHash -cne $taskInput.Sha256) { throw ('Contract input changed during the run: ' + $taskInput.Path) }
    }
    if ((Get-FileHash -LiteralPath $taskInteropPath -Algorithm SHA256).Hash -cne $taskInterop.Sha256) {
        throw 'The production CsWin32 reference changed during the contract run.'
    }
}

function Write-TaskIndex {
    $taskTotal = 0
    $taskFailed = 0
    foreach ($taskCase in $taskCases) { $taskTotal += $taskCase.Passed + $taskCase.Failed; $taskFailed += $taskCase.Failed }
    $taskBuildErrors = 0
    foreach ($taskBuild in $taskBuilds) { if ($taskBuild.ExitCode -ne 0) { $taskBuildErrors++ } }
    $taskReport = [ordered]@{
        CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        StartedAtUtc = $taskStartedUtc
        Status = $taskStatus
        SourceHead = $taskHead
        SourceWorkingTreeDirtyAtStart = $false
        ExpectedContracts = 159
        TotalContracts = $taskTotal
        Failed = $taskFailed
        Scope = 'Fresh sequential rebuilds and contract runs over frozen current extracted/linked sources. Controlled Shell/dispatcher/WinRT plus native search against fresh synthetic NTFS fixture and junction. No WinUI, rendered frame or app file-operation claim.'
        Cases = @($taskCases.ToArray())
        Builds = @($taskBuilds.ToArray())
        CompiledSources = @($taskCompiledSources)
        PreparationInputs = @($taskPreparation)
        InteropReference = $taskInterop
        FreshSearchFixture = "$taskSearchRelative/fixture"
        FixtureFileSystem = $taskFileSystem
        BuildErrors = $taskBuildErrors
        BuildErrorsScope = 'Number of contract rebuilds with a nonzero exit code; exact compiler diagnostics are retained in each build log.'
        BuildWarningsScope = 'Summary count from isolated harness build logs when present; no assertion of application warning delta.'
        Sdk = $taskSdk
        DotnetPath = Get-TaskRelativePath $taskDotnet
        DotnetRuntimes = @($taskRuntimes)
        CurrentAction = $taskCurrentAction
        Failure = $taskFailure
    }
    [IO.File]::WriteAllText($taskIndexPath, (($taskReport | ConvertTo-Json -Depth 12) -replace '\r?\n', "`r`n") + "`r`n")
}

function Invoke-TaskDotnet([string[]]$Arguments, [string]$LogPath) {
    & $taskDotnet @Arguments *> $LogPath
    $taskExitCode = $LASTEXITCODE
    [pscustomobject]@{ ExitCode = $taskExitCode; Log = Get-TaskRelativePath $LogPath; LogSha256 = (Get-FileHash -LiteralPath $LogPath -Algorithm SHA256).Hash }
}

function Invoke-TaskBuild($Project) {
    Assert-TaskNoOtherBuild
    Assert-TaskFrozenInputs
    $script:taskCurrentAction = 'Rebuild ' + $Project.Project
    $taskLogPath = Join-Path $taskSearchRoot ($Project.Id + '-build.log')
    $taskExecution = Invoke-TaskDotnet @('build', $Project.Project, '-c', 'Release', '-t:Rebuild', '-v:quiet', '-clp:ErrorsOnly') $taskLogPath
    $taskLogText = [IO.File]::ReadAllText($taskLogPath)
    $taskWarningMatch = [regex]::Match($taskLogText, '(?im)^\s*(\d+)\s+Warning\(s\)\s*$')
    $taskWarnings = if ($taskWarningMatch.Success) { [int]$taskWarningMatch.Groups[1].Value } else { $null }
    $taskBuild = [pscustomobject]@{
        Project = $Project.Project
        Configuration = 'Release'
        Target = 'Rebuild'
        ExitCode = $taskExecution.ExitCode
        BuildErrors = if ($taskExecution.ExitCode -eq 0) { 0 } else { $null }
        BuildWarnings = $taskWarnings
        Log = $taskExecution.Log
        LogSha256 = $taskExecution.LogSha256
        Assembly = $Project.Assembly
        AssemblySha256 = $null
        RuntimeConfigSha256 = $null
        CopiedInteropSha256 = $null
    }
    $taskBuilds.Add($taskBuild)
    if ($taskExecution.ExitCode -ne 0) { throw ('Contract rebuild failed: ' + $Project.Project + '. See ' + $taskExecution.Log) }
    $taskAssemblyPath = Join-Path $taskWorkspace $Project.Assembly
    if (-not (Test-Path -LiteralPath $taskAssemblyPath -PathType Leaf)) { throw ('Built contract assembly is missing: ' + $Project.Assembly) }
    $taskBuild.AssemblySha256 = (Get-FileHash -LiteralPath $taskAssemblyPath -Algorithm SHA256).Hash
    $taskRuntimeConfig = [IO.Path]::ChangeExtension($taskAssemblyPath, '.runtimeconfig.json')
    $taskBuild.RuntimeConfigSha256 = (Get-FileHash -LiteralPath $taskRuntimeConfig -Algorithm SHA256).Hash
    if ($Project.Native) {
        $taskCopiedInterop = Join-Path (Split-Path $taskAssemblyPath -Parent) 'Files.App.CsWin32.dll'
        $taskBuild.CopiedInteropSha256 = (Get-FileHash -LiteralPath $taskCopiedInterop -Algorithm SHA256).Hash
        if ($taskBuild.CopiedInteropSha256 -cne $taskInterop.Sha256) { throw ('Built project copied a different CsWin32 reference: ' + $Project.Project) }
    }
    Assert-TaskFrozenInputs
    Write-TaskIndex
    $taskBuild
}

function Add-TaskContractEvidence($Project, $Build, $Execution, [string]$Evidence) {
    $taskEvidencePath = Join-Path $taskWorkspace $Evidence
    if (-not (Test-Path -LiteralPath $taskEvidencePath -PathType Leaf)) {
        throw ('Contract execution did not produce evidence: ' + $Evidence + '. See ' + $Execution.Log)
    }
    $taskEvidence = Get-Content -LiteralPath $taskEvidencePath -Raw | ConvertFrom-Json
    $taskRows = if ($Project.Native -or $Project.Id -eq 'suggestions' -or $Project.Id -eq 'traversal') { @($taskEvidence) } else { @($taskEvidence.Results) }
    $taskPassed = 0
    foreach ($taskRow in $taskRows) {
        if ($Project.Id -eq 'native-search') { if ($taskRow.Status -ceq 'passed') { $taskPassed++ } }
        elseif ($taskRow.Passed -eq $true) { $taskPassed++ }
    }
    $taskCases.Add([pscustomobject]@{
        Group = $Project.Group
        Passed = $taskPassed
        Failed = $taskRows.Count - $taskPassed
        ExpectedContracts = $Project.Count
        BuildWarnings = $Build.BuildWarnings
        Evidence = $Evidence
        EvidenceSha256 = (Get-FileHash -LiteralPath $taskEvidencePath -Algorithm SHA256).Hash
        ExecutionExitCode = $Execution.ExitCode
        ExecutionLog = $Execution.Log
        ExecutionLogSha256 = $Execution.LogSha256
    })
    Assert-TaskFrozenInputs
    Write-TaskIndex
    if ($taskRows.Count -ne $Project.Count -or $taskPassed -ne $Project.Count -or $Execution.ExitCode -ne 0) {
        throw ('Contract group failed or changed its expected case count: ' + $Project.Group + '. See ' + $Evidence)
    }
}

function Assert-TaskBuiltAssembly($Build) {
    $taskBuiltPath = Join-Path $taskWorkspace $Build.Assembly
    if ((Get-FileHash -LiteralPath $taskBuiltPath -Algorithm SHA256).Hash -cne $Build.AssemblySha256 -or
        (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($taskBuiltPath, '.runtimeconfig.json')) -Algorithm SHA256).Hash -cne $Build.RuntimeConfigSha256) {
        throw ('Built contract assembly or runtime configuration changed: ' + $Build.Assembly)
    }
    if ($Build.CopiedInteropSha256) {
        $taskCopiedPath = Join-Path (Split-Path $taskBuiltPath -Parent) 'Files.App.CsWin32.dll'
        if ((Get-FileHash -LiteralPath $taskCopiedPath -Algorithm SHA256).Hash -cne $Build.CopiedInteropSha256) {
            throw ('Copied CsWin32 reference changed: ' + $Build.Assembly)
        }
    }
}

$taskProjects = @(
    @{ Id = 'path-navigation'; Group = 'ToolbarPathNavigation'; Count = 37; Project = 'tests-extra/Phase3PathNavigation/PathNavigationContracts.csproj'; Assembly = 'tests-extra/Phase3PathNavigation/bin/Release/net10.0/PathNavigationContracts.dll'; Prepare = 'tests-extra/Phase3PathNavigation/Prepare-PathContracts.ps1'; Native = $false; Evidence = "reports/phase4-path-navigation-contracts-$RunSuffix.json" },
    @{ Id = 'working-directory'; Group = 'WorkingDirectoryAndBreadcrumb'; Count = 41; Project = 'tests-extra/Phase3WorkingDirectory/WorkingDirectoryContracts.csproj'; Assembly = 'tests-extra/Phase3WorkingDirectory/bin/Release/net10.0/WorkingDirectoryContracts.dll'; Prepare = 'tests-extra/Phase3WorkingDirectory/Prepare-WorkingDirectoryContracts.ps1'; Native = $false; Evidence = "reports/phase4-working-directory-contracts-$RunSuffix.json" },
    @{ Id = 'shell-navigation'; Group = 'ShellNavigationAndControlledEnumeration'; Count = 22; Project = 'tests-extra/Phase3Navigation/NavigationContracts.csproj'; Assembly = 'tests-extra/Phase3Navigation/bin/Release/net10.0/NavigationContracts.dll'; Prepare = 'tests-extra/Phase3Navigation/Prepare-ShellContracts.ps1'; Native = $false; Evidence = "reports/phase4-navigation-shell-contracts-$RunSuffix.json" },
    @{ Id = 'adaptive'; Group = 'AdaptiveLayout'; Count = 17; Project = 'tests-extra/Adaptive/AdaptiveContracts.csproj'; Assembly = 'tests-extra/Adaptive/bin/Release/net10.0/AdaptiveContracts.dll'; Native = $false; Evidence = "reports/phase4-adaptive-contracts-$RunSuffix.json" },
    @{ Id = 'native-search'; Group = 'NativeNameSearchFreshSyntheticNTFS'; Count = 28; Project = 'tests-extra/Search/Search.Tests.csproj'; Assembly = 'tests-extra/Search/bin/Release/net10.0-windows10.0.26100.0/Search.Tests.dll'; Native = $true; Evidence = "$taskSearchRelative/native-contracts/search-contracts.json" },
    @{ Id = 'traversal'; Group = 'RecursiveSearchAndJunctionCycle'; Count = 3; Project = 'benchmarks/Search/Search.csproj'; Assembly = 'benchmarks/Search/bin/Release/net10.0-windows10.0.26100.0/FilesTurbo.SearchBench.dll'; Native = $true; Evidence = "$taskSearchRelative/search-traversal-contracts.json" },
    @{ Id = 'suggestions'; Group = 'SuggestionsScheduling'; Count = 11; Project = 'benchmarks/Phase3/Search/SearchPipeline.csproj'; Assembly = 'benchmarks/Phase3/Search/bin/Release/net10.0-windows10.0.26100.0/SearchPipeline.dll'; Native = $true; Evidence = "$taskSearchRelative/suggestion-contracts/search-pipeline-contracts.json" }
)

Assert-TaskNoOtherBuild
foreach ($taskExistingPath in @($taskIndexPath, $taskSearchRoot) + @($taskProjects | ForEach-Object { Join-Path $taskWorkspace $_.Evidence })) {
    if (Test-Path -LiteralPath $taskExistingPath) { throw ('Output already exists; preserve it and choose a fresh RunSuffix: ' + (Get-TaskRelativePath $taskExistingPath)) }
}
$taskHead = Get-TaskSourceHead
if ($ExpectedSourceHead -and $taskHead -cne $ExpectedSourceHead.ToLowerInvariant()) { throw 'Current production HEAD differs from ExpectedSourceHead.' }
$taskDirty = @(& git -C $taskSourceRoot status --porcelain -- src)
if ($LASTEXITCODE -ne 0 -or $taskDirty.Count -gt 0) { throw 'Production src must be committed before freezing the final contract inputs.' }
if (-not (Test-Path -LiteralPath $taskInteropPath -PathType Leaf)) { throw 'The Release x64 production CsWin32 assembly must exist before running these contracts.' }
$taskDriveLetter = [IO.Path]::GetPathRoot($taskWorkspace).Substring(0, 1)
$taskFileSystem = (Get-Volume -DriveLetter $taskDriveLetter).FileSystemType
if ($taskFileSystem -ne 'NTFS') { throw 'The fresh native-search fixture must reside on NTFS.' }

. (Join-Path $PSScriptRoot 'Enter-WorkspaceEnvironment.ps1')
$taskDotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$taskSdk = (& $taskDotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or $taskSdk -ne '10.0.401') { throw 'The workspace SDK must resolve to 10.0.401.' }
$taskRuntimes = @(& $taskDotnet --list-runtimes)
if ($LASTEXITCODE -ne 0) { throw 'Could not capture the workspace runtime inventory.' }
$taskInterop = [pscustomobject]@{ Path = $taskInteropRelative; Sha256 = (Get-FileHash -LiteralPath $taskInteropPath -Algorithm SHA256).Hash }
New-Item -ItemType Directory -Path $taskSearchRoot | Out-Null
New-Item -ItemType Directory -Path (Split-Path $taskIndexPath -Parent) -Force | Out-Null
$taskPreviousLocation = Get-Location
Set-Location -LiteralPath $taskWorkspace
try {
    Write-TaskIndex
    foreach ($taskProject in $taskProjects) {
        if ($taskProject.Prepare) {
            $taskCurrentAction = 'Prepare ' + $taskProject.Prepare
            & (Join-Path $taskWorkspace $taskProject.Prepare) *> (Join-Path $taskSearchRoot ($taskProject.Id + '-prepare.log'))
        }
    }
    $taskFullMethodSources = @(
        'source/src/Files.App/ViewModels/UserControls/NavigationToolbarViewModel.cs',
        'source/src/Files.App/ViewModels/ShellViewModel.cs',
        'source/src/Files.App/Views/Shells/BaseShellPage.cs',
        'source/src/Files.App/Views/Shells/ModernShellPage.xaml.cs'
    )
    $taskCompiledSources = @(foreach ($taskProject in $taskProjects) {
        $taskProjectPath = Join-Path $taskWorkspace $taskProject.Project
        $taskProjectDirectory = Split-Path $taskProjectPath -Parent
        [xml]$taskProjectXml = [IO.File]::ReadAllText($taskProjectPath)
        $taskPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $taskPaths.Add($taskProjectPath) | Out-Null
        foreach ($taskCompile in @($taskProjectXml.Project.ItemGroup.Compile)) {
            if ($taskCompile.Include) { $taskPaths.Add([IO.Path]::GetFullPath((Join-Path $taskProjectDirectory $taskCompile.Include))) | Out-Null }
        }
        if (@($taskProjectXml.Project.PropertyGroup.EnableDefaultCompileItems) -notcontains 'false') {
            foreach ($taskFile in Get-ChildItem -LiteralPath $taskProjectDirectory -File -Filter '*.cs') { $taskPaths.Add($taskFile.FullName) | Out-Null }
        }
        foreach ($taskPath in $taskPaths | Sort-Object) {
            [pscustomobject]@{ Project = $taskProject.Project; Path = Get-TaskRelativePath $taskPath; Sha256 = (Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash }
        }
    })
    $taskPreparation = @(foreach ($taskPath in $taskFullMethodSources + @($taskProjects | Where-Object { $_.Prepare } | ForEach-Object { $_.Prepare }) + @(
        'tests-extra/Phase3PathNavigation/source-hash.txt',
        'tests-extra/Phase3WorkingDirectory/source-hashes.json',
        'tests-extra/Phase3Navigation/source-hash.txt',
        'tooling/Enter-WorkspaceEnvironment.ps1',
        'tooling/Test-Phase4FinalContracts.ps1'
    )) { [pscustomobject]@{ Path = $taskPath; Sha256 = (Get-FileHash -LiteralPath (Join-Path $taskWorkspace $taskPath) -Algorithm SHA256).Hash } })
    Assert-TaskFrozenInputs
    Write-TaskIndex
    foreach ($taskProject in $taskProjects) {
        $taskBuild = Invoke-TaskBuild $taskProject
        $taskCurrentAction = 'Execute ' + $taskProject.Group
        Assert-TaskNoOtherBuild
        Assert-TaskBuiltAssembly $taskBuild
        $taskAssemblyPath = Join-Path $taskWorkspace $taskProject.Assembly
        $taskEvidencePath = Join-Path $taskWorkspace $taskProject.Evidence
        $taskArguments = switch ($taskProject.Id) {
            'native-search' { @($taskAssemblyPath, (Join-Path $taskSearchRoot 'fixture'), (Join-Path $taskSearchRoot 'native-contracts')) }
            'traversal' {
                $taskFixture = (Resolve-Path -LiteralPath (Join-Path $taskSearchRoot 'fixture')).Path
                if (-not $taskFixture.StartsWith($taskSearchRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'The search fixture escaped its fresh output directory.' }
                New-Item -ItemType Junction -Path (Join-Path $taskFixture 'junction') -Value $taskFixture | Out-Null
                @($taskAssemblyPath, '--traversal-tests', $taskFixture, $taskEvidencePath)
            }
            'suggestions' { @($taskAssemblyPath, '--contracts', (Join-Path $taskSearchRoot 'suggestion-contracts')) }
            default { @($taskAssemblyPath, $taskEvidencePath) }
        }
        $taskExecution = Invoke-TaskDotnet $taskArguments (Join-Path $taskSearchRoot ($taskProject.Id + '-execution.log'))
        Assert-TaskBuiltAssembly $taskBuild
        Add-TaskContractEvidence $taskProject $taskBuild $taskExecution $taskProject.Evidence
    }
    Assert-TaskFrozenInputs
    foreach ($taskBuild in $taskBuilds) { Assert-TaskBuiltAssembly $taskBuild }
    $taskTotalContracts = ($taskCases | ForEach-Object { $_.Passed + $_.Failed } | Measure-Object -Sum).Sum
    if ($taskTotalContracts -ne 159) { throw 'The aggregate contract count is not 159.' }
    $taskStatus = 'Completed'
    $taskCurrentAction = 'Complete'
    Write-TaskIndex
    Write-Output ('Passed 159/159 sequential regression contracts. ' + (Get-TaskRelativePath $taskIndexPath))
}
catch {
    $taskStatus = 'Failed'
    $taskFailure = $_.Exception.Message
    Write-TaskIndex
    throw
}
finally { Set-Location -LiteralPath $taskPreviousLocation }
