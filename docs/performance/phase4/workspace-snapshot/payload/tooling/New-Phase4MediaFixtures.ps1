#Requires -Version 7.2
[CmdletBinding()]
param([ValidateRange(80,500)][int]$Images = 96, [ValidateRange(0,12)][int]$Videos = 3, [string]$Destination = 'benchmarks/media-phase4')
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$taskTarget = [IO.Path]::GetFullPath((Join-Path $taskRoot $Destination))
if (!$taskTarget.StartsWith((Join-Path $taskRoot 'benchmarks') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixtures must remain inside workspace benchmarks.' }
$taskCursor = $taskTarget
while ($taskCursor.Length -ge $taskRoot.Length) {
    if ([IO.Directory]::Exists($taskCursor) -and ((Get-Item -LiteralPath $taskCursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked fixture path rejected.' }
    if ($taskCursor -eq $taskRoot) { break }
    $taskCursor = Split-Path $taskCursor -Parent
}
if (Test-Path -LiteralPath $taskTarget) { throw 'Fixtures already exist; preserve them.' }
$taskManifest = $taskTarget + '-manifest.json'
if ([IO.File]::Exists($taskManifest)) { throw 'Fixture manifest already exists.' }
Add-Type -AssemblyName System.Drawing.Common
$null = [IO.Directory]::CreateDirectory($taskTarget)
$taskColors = @([Drawing.Color]::FromArgb(220,30,60),[Drawing.Color]::FromArgb(25,195,70),[Drawing.Color]::FromArgb(40,80,220),[Drawing.Color]::FromArgb(225,205,25))
$taskInventory = [Collections.Generic.List[object]]::new()
for ($taskIndex = 0; $taskIndex -lt $Images; $taskIndex++) {
    $taskExtension = @('png','jpg','bmp')[$taskIndex % 3]
    $taskSize = @(256,1024,2048)[$taskIndex % 3]
    $taskBitmap = [Drawing.Bitmap]::new($taskSize,$taskSize)
    $taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
    try {
        for ($taskQuadrant = 0; $taskQuadrant -lt 4; $taskQuadrant++) {
            $taskBrush = [Drawing.SolidBrush]::new($taskColors[$taskQuadrant])
            try { $taskGraphics.FillRectangle($taskBrush, [int](($taskQuadrant % 2) * $taskSize / 2), [int]([Math]::Floor($taskQuadrant / 2) * $taskSize / 2), [int]($taskSize / 2), [int]($taskSize / 2)) } finally { $taskBrush.Dispose() }
        }
        $taskPen = [Drawing.Pen]::new([Drawing.Color]::White,[single]([Math]::Max(2,$taskSize / 128)))
        try { $taskInset = [int]($taskSize * (0.05 + ($taskIndex % 8) * 0.005)); $taskGraphics.DrawRectangle($taskPen,$taskInset,$taskInset,$taskSize - 2*$taskInset,$taskSize - 2*$taskInset) } finally { $taskPen.Dispose() }
        $taskPath = Join-Path $taskTarget ('pattern-{0:D3}.{1}' -f $taskIndex,$taskExtension)
        $taskFormat = if ($taskExtension -eq 'jpg') { [Drawing.Imaging.ImageFormat]::Jpeg } elseif ($taskExtension -eq 'png') { [Drawing.Imaging.ImageFormat]::Png } else { [Drawing.Imaging.ImageFormat]::Bmp }
        $taskBitmap.Save($taskPath,$taskFormat)
    } finally { $taskGraphics.Dispose(); $taskBitmap.Dispose() }
    $taskInventory.Add([pscustomobject]@{ Name = [IO.Path]::GetFileName($taskPath); Kind = 'Image'; Width = $taskSize; Height = $taskSize; Bytes = (Get-Item -LiteralPath $taskPath).Length; SHA256 = (Get-FileHash -LiteralPath $taskPath).Hash })
}
$taskFfmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue | Select-Object -First 1
if ($Videos -gt 0 -and !$taskFfmpeg) { throw 'FFmpeg unavailable; images retained but video fixture generation is incomplete.' }
for ($taskIndex = 0; $taskIndex -lt $Videos; $taskIndex++) {
    $taskPath = Join-Path $taskTarget ('pattern-video-{0:D3}.mp4' -f $taskIndex)
    $taskInput = Join-Path $taskTarget 'pattern-000.png'
    & $taskFfmpeg.Source -hide_banner -loglevel error -nostdin -n -loop 1 -i $taskInput -t 2 -vf scale=640:640 -r 24 -c:v libx264 -pix_fmt yuv420p -movflags +faststart $taskPath
    if ($LASTEXITCODE -ne 0) { throw 'FFmpeg fixture generation failed; partial fixtures retained for review.' }
    $taskInventory.Add([pscustomobject]@{ Name = [IO.Path]::GetFileName($taskPath); Kind = 'Video'; Width = 640; Height = 640; DurationSeconds = 2; Codec = 'H264'; Bytes = (Get-Item -LiteralPath $taskPath).Length; SHA256 = (Get-FileHash -LiteralPath $taskPath).Hash })
}
[ordered]@{ CreatedUtc = [DateTime]::UtcNow.ToString('o'); FixturePath = $taskTarget; Images = $Images; Videos = $Videos; Pattern = 'Square TL=(220,30,60), TR=(25,195,70), BL=(40,80,220), BR=(225,205,25), narrow white inset border'; Ffmpeg = $taskFfmpeg.Source; Scope = 'New synthetic media only; no user media and no Shell cache manipulation'; Files = @($taskInventory) } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $taskManifest -Encoding utf8
Write-Output $taskManifest
