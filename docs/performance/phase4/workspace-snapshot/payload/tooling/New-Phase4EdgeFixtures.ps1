#Requires -Version 7.2
[CmdletBinding()]
param([string]$Destination = 'benchmarks/media-phase4-edge')
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$taskTarget = [IO.Path]::GetFullPath((Join-Path $taskRoot $Destination))
if (!$taskTarget.StartsWith((Join-Path $taskRoot 'benchmarks') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixtures must remain inside workspace benchmarks.' }
$taskCursor = $taskTarget
while ($taskCursor.Length -ge $taskRoot.Length) {
    if ((Test-Path -LiteralPath $taskCursor) -and ((Get-Item -LiteralPath $taskCursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked fixture path rejected.' }
    if ($taskCursor -eq $taskRoot) { break }
    $taskCursor = Split-Path $taskCursor -Parent
}
if (Test-Path -LiteralPath $taskTarget) { throw 'Fixtures already exist; preserve them.' }
$taskManifest = $taskTarget + '-manifest.json'
if (Test-Path -LiteralPath $taskManifest) { throw 'Fixture manifest already exists; preserve it.' }
Add-Type -AssemblyName System.Drawing.Common
$null = [IO.Directory]::CreateDirectory($taskTarget)
$taskInventory = [Collections.Generic.List[object]]::new()

function Add-EdgeInventory([string]$Name, [string]$Kind, [string]$Expected, [int]$Width = 0, [int]$Height = 0) {
    $taskFile = Join-Path $taskTarget $Name
    $taskInventory.Add([ordered]@{ Name = $Name; Kind = $Kind; Width = $Width; Height = $Height; Expected = $Expected; Bytes = (Get-Item -LiteralPath $taskFile).Length; SHA256 = (Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash })
}

# Alpha regions and a fine pattern expose matte artifacts and undersized requests.
$taskBitmap = [Drawing.Bitmap]::new(1024,1024,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
$taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
try {
    $taskGraphics.Clear([Drawing.Color]::Transparent)
    $taskColors = @([Drawing.Color]::FromArgb(255,220,30,60),[Drawing.Color]::FromArgb(160,25,195,70),[Drawing.Color]::FromArgb(80,40,80,220))
    for ($taskIndex = 0; $taskIndex -lt $taskColors.Count; $taskIndex++) {
        $taskBrush = [Drawing.SolidBrush]::new($taskColors[$taskIndex])
        try { $taskGraphics.FillRectangle($taskBrush,64 + $taskIndex * 304,64,256,704) } finally { $taskBrush.Dispose() }
    }
    $taskPen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(255,255,255,255),2)
    try { for ($taskY = 816; $taskY -lt 960; $taskY += 8) { $taskGraphics.DrawLine($taskPen,64,$taskY,960,$taskY) } } finally { $taskPen.Dispose() }
    $taskBitmap.Save((Join-Path $taskTarget 'alpha-regions.png'),[Drawing.Imaging.ImageFormat]::Png)
} finally { $taskGraphics.Dispose(); $taskBitmap.Dispose() }
Add-EdgeInventory 'alpha-regions.png' 'PNG alpha' 'Transparent margins; red opaque, green alpha160, blue alpha80; opaque fine lines at bottom.' 1024 1024

# Store landscape pixels with EXIF orientation6, plus the expected clockwise display.
$taskBitmap = [Drawing.Bitmap]::new(1024,512)
$taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
$taskFont = [Drawing.Font]::new('Segoe UI',42,[Drawing.FontStyle]::Bold)
try {
    $taskColors = @([Drawing.Color]::FromArgb(220,30,60),[Drawing.Color]::FromArgb(25,195,70),[Drawing.Color]::FromArgb(40,80,220),[Drawing.Color]::FromArgb(225,205,25))
    $taskLabels = @('RAW TL','RAW TR','RAW BL','RAW BR')
    for ($taskIndex = 0; $taskIndex -lt 4; $taskIndex++) {
        $taskX = [int](($taskIndex % 2) * 512)
        $taskY = [int]([Math]::Floor($taskIndex / 2) * 256)
        $taskBrush = [Drawing.SolidBrush]::new($taskColors[$taskIndex])
        try { $taskGraphics.FillRectangle($taskBrush,$taskX,$taskY,512,256) } finally { $taskBrush.Dispose() }
        $taskGraphics.DrawString($taskLabels[$taskIndex],$taskFont,[Drawing.Brushes]::Black,$taskX + 24,$taskY + 80)
    }
    $taskStream = [IO.MemoryStream]::new()
    try { $taskBitmap.Save($taskStream,[Drawing.Imaging.ImageFormat]::Jpeg); $taskJpeg = $taskStream.ToArray() } finally { $taskStream.Dispose() }
} finally { $taskFont.Dispose(); $taskGraphics.Dispose() }
try {
    $taskExif = [byte[]]@(0xFF,0xE1,0,34,0x45,0x78,0x69,0x66,0,0,0x49,0x49,0x2A,0,8,0,0,0,1,0,0x12,1,3,0,1,0,0,0,6,0,0,0,0,0,0,0)
    $taskOriented = [IO.MemoryStream]::new()
    try {
        $taskOriented.Write($taskJpeg,0,2)
        $taskOriented.Write($taskExif,0,$taskExif.Length)
        $taskOriented.Write($taskJpeg,2,$taskJpeg.Length - 2)
        [IO.File]::WriteAllBytes((Join-Path $taskTarget 'orientation-6.jpg'),$taskOriented.ToArray())
    } finally { $taskOriented.Dispose() }
    $taskBitmap.RotateFlip([Drawing.RotateFlipType]::Rotate90FlipNone)
    $taskBitmap.Save((Join-Path $taskTarget 'orientation-6-reference.png'),[Drawing.Imaging.ImageFormat]::Png)
} finally { $taskBitmap.Dispose() }
Add-EdgeInventory 'orientation-6.jpg' 'JPEG EXIF orientation6' 'Display portrait512x1024: TL blue, TR red, BL yellow, BR green. Raw pixels1024x512; no embedded thumbnail.' 1024 512
Add-EdgeInventory 'orientation-6-reference.png' 'PNG orientation reference' 'Pixel reference for the expected clockwise EXIF orientation; rotated labels are intentional.' 512 1024

# A deliberately incomplete PNG tests the fallback path, not visual similarity.
[IO.File]::WriteAllBytes((Join-Path $taskTarget 'corrupt-truncated.png'),[byte[]]@(137,80,78,71,13,10,26,10,0,0,0,13,73,72,68))
Add-EdgeInventory 'corrupt-truncated.png' 'Intentionally corrupt PNG' 'Signature and truncated header only. Thumbnail failure/fallback expected; must not block subsequent files.'

# Minimal valid PDF with vector content and no /Thumb entry; Shell support is provider-dependent.
$taskPdfContent = "1 0 0 rg 12 156 108 132 re f`n0 1 0 rg 120 156 108 132 re f`n0 0 1 rg 12 12 108 144 re f`n1 1 0 rg 120 12 108 144 re f`n0 0 0 rg BT /F1 12 Tf 24 280 Td (Synthetic PDF fixture) Tj ET`n"
$taskPdfObjects = @(
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 300] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>',
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
    "<< /Length $([Text.Encoding]::ASCII.GetByteCount($taskPdfContent)) >>`nstream`n$taskPdfContent" + 'endstream'
)
$taskPdf = [Text.StringBuilder]::new("%PDF-1.4`n")
$taskOffsets = [Collections.Generic.List[int]]::new()
for ($taskIndex = 0; $taskIndex -lt $taskPdfObjects.Count; $taskIndex++) {
    $taskOffsets.Add([Text.Encoding]::ASCII.GetByteCount($taskPdf.ToString()))
    $null = $taskPdf.Append("$($taskIndex + 1) 0 obj`n$($taskPdfObjects[$taskIndex])`nendobj`n")
}
$taskXrefOffset = [Text.Encoding]::ASCII.GetByteCount($taskPdf.ToString())
$null = $taskPdf.Append("xref`n0 6`n0000000000 65535 f `n")
foreach ($taskOffset in $taskOffsets) { $null = $taskPdf.Append(('{0:D10} 00000 n ' -f $taskOffset) + "`n") }
$null = $taskPdf.Append("trailer`n<< /Size 6 /Root 1 0 R >>`nstartxref`n$taskXrefOffset`n%%EOF`n")
[IO.File]::WriteAllBytes((Join-Path $taskTarget 'vector-no-embedded-thumbnail.pdf'),[Text.Encoding]::ASCII.GetBytes($taskPdf.ToString()))
Add-EdgeInventory 'vector-no-embedded-thumbnail.pdf' 'PDF without embedded thumbnail' 'Valid one-page vector PDF. Installed providers may produce a thumbnail; generic icon is also a supported outcome.' 240 300

$taskFfmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue | Select-Object -First 1
$taskWebpStatus = 'Skipped: FFmpeg unavailable'
$taskFfmpegVersion = $null
if ($taskFfmpeg) {
    $taskFfmpegVersion = @(& $taskFfmpeg.Source -version)[0]
    $taskEncoders = @(& $taskFfmpeg.Source -hide_banner -encoders 2>&1)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate FFmpeg encoders; partial fixtures retained.' }
    if ($taskEncoders -match '\blibwebp\b') {
        & $taskFfmpeg.Source -hide_banner -loglevel error -nostdin -n -threads 1 -i (Join-Path $taskTarget 'alpha-regions.png') -frames:v 1 -c:v libwebp -lossless 1 -threads 1 (Join-Path $taskTarget 'alpha-lossless.webp')
        if ($LASTEXITCODE -ne 0) { throw 'WebP fixture generation failed; partial fixtures retained.' }
        Add-EdgeInventory 'alpha-lossless.webp' 'Lossless WebP alpha' 'Same alpha regions as PNG. Actual Shell thumbnail support depends on installed codecs/providers.' 1024 1024
        $taskWebpStatus = 'Created: libwebp lossless'
    } else { $taskWebpStatus = 'Skipped: libwebp encoder unavailable' }
}
[ordered]@{ CreatedUtc = [DateTime]::UtcNow.ToString('o'); FixturePath = $taskTarget; Scope = 'Synthetic edge cases only; no user media, Shell extraction or system cache manipulation. Expected display is a reference, not evidence of provider support.'; ScriptSHA256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash; FfmpegVersion = $taskFfmpegVersion; WebpStatus = $taskWebpStatus; Files = @($taskInventory) } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $taskManifest -Encoding utf8
Write-Output $taskManifest
