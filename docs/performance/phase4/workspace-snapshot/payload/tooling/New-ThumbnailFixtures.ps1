#Requires -Version 7.2
[CmdletBinding()]
param([string]$Destination = 'benchmarks/media-phase2')
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$taskTarget = [IO.Path]::GetFullPath((Join-Path $taskRoot $Destination))
if (!$taskTarget.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Destination outside workspace.' }
$taskCursor = Split-Path $taskTarget -Parent
while ($taskCursor.Length -ge $taskRoot.Length) {
    if ((Test-Path -LiteralPath $taskCursor) -and ((Get-Item -LiteralPath $taskCursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked destination rejected.' }
    if ($taskCursor -eq $taskRoot) { break }
    $taskCursor = Split-Path $taskCursor -Parent
}
if (Test-Path -LiteralPath $taskTarget) { throw 'Fixtures already exist; preserve them.' }
$null = [IO.Directory]::CreateDirectory($taskTarget)
$taskInventory = foreach ($taskSize in @(64,256,1024)) {
    $taskStride = (($taskSize * 3 + 3) -band -4)
    $taskPixels = $taskStride * $taskSize
    $taskPath = Join-Path $taskTarget ("synthetic-gradient-$taskSize.bmp")
    $taskWriter = [IO.BinaryWriter]::new([IO.File]::Open($taskPath, [IO.FileMode]::CreateNew))
    try {
        $taskWriter.Write([byte[]]@(0x42,0x4D))
        $taskWriter.Write([int]($taskPixels + 54))
        $taskWriter.Write([int]0); $taskWriter.Write([int]54); $taskWriter.Write([int]40)
        $taskWriter.Write([int]$taskSize); $taskWriter.Write([int]$taskSize)
        $taskWriter.Write([short]1); $taskWriter.Write([short]24)
        $taskWriter.Write([int]0); $taskWriter.Write([int]$taskPixels)
        $taskWriter.Write([int]2835); $taskWriter.Write([int]2835)
        $taskWriter.Write([int]0); $taskWriter.Write([int]0)
        $taskRow = [byte[]]::new($taskStride)
        for ($taskY = 0; $taskY -lt $taskSize; $taskY++) {
            for ($taskX = 0; $taskX -lt $taskSize; $taskX++) {
                $taskRow[$taskX * 3] = [byte](255 * $taskX / ($taskSize - 1))
                $taskRow[$taskX * 3 + 1] = [byte](255 * $taskY / ($taskSize - 1))
                $taskRow[$taskX * 3 + 2] = [byte]128
            }
            $taskWriter.Write($taskRow)
        }
    } finally { $taskWriter.Dispose() }
    [pscustomobject]@{ File = [IO.Path]::GetRelativePath($taskRoot, $taskPath); Width = $taskSize; Height = $taskSize; Bytes = (Get-Item -LiteralPath $taskPath).Length; SHA256 = (Get-FileHash -LiteralPath $taskPath).Hash }
}
@{ Scope = 'Valid uncompressed BMPs for voluntary UI/thumbnail tests; no videos and no shell thumbnail generation executed'; Files = @($taskInventory) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskTarget 'manifest.json')
$taskInventory
