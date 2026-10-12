<#
.SYNOPSIS
    Creates the synthetic folders used by FilesBench. Everything is written under -Root; nothing else is touched.

.DESCRIPTION
    natural-N : N empty files "Documento 1.txt" ... "Documento N.txt" plus 20 folders "Proyecto 1" ... "Proyecto 20".
                NTFS returns them in ordinal order ("Documento 1", "Documento 10", ...; folders last), which differs from
                the natural, folders-first order the explorers display. An early unsorted paint is therefore not final.
    empty     : an empty folder used as the starting point of every navigation.

    The zero-padded folders 1000/10000/50000/100000 in benchmarks\synthetic predate this script; NTFS returns those
    in their display order.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Root,
    [int[]]$NaturalSizes = @(10000, 100000)
)
$ErrorActionPreference = 'Stop'
$Root = [IO.Path]::GetFullPath($Root)
New-Item -ItemType Directory -Path (Join-Path $Root 'empty') -Force | Out-Null
foreach ($size in $NaturalSizes) {
    $folder = Join-Path $Root "natural-$size"
    if (Test-Path -LiteralPath $folder) { Write-Output "Exists: $folder"; continue }
    New-Item -ItemType Directory -Path $folder | Out-Null
    for ($i = 1; $i -le 20; $i++) { New-Item -ItemType Directory -Path (Join-Path $folder "Proyecto $i") | Out-Null }
    for ($i = 1; $i -le $size; $i++) { [IO.File]::WriteAllBytes((Join-Path $folder "Documento $i.txt"), [byte[]]@()) }
    Write-Output "Created: $folder ($size files, 20 folders)"
}
