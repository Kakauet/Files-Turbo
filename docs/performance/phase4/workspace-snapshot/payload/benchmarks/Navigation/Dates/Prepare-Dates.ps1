param([ValidateSet('baseline', 'phase1', 'phase2')][string]$Variant = 'phase2')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$listedFile = if ($Variant -eq 'phase2') { Join-Path $workspace 'source/src/Files.App/Data/Items/ListedItem.cs' } else { Join-Path $workspace 'source-baseline/src/Files.App/Data/Items/ListedItem.cs' }
$text = [IO.File]::ReadAllText($listedFile)
$start = $text.IndexOf('private string? itemDateModified;', [StringComparison]::Ordinal)
$end = $text.IndexOf('private ObservableCollection<FileProperty>? itemProperties;', $start, [StringComparison]::Ordinal)
$properties = $text.Substring($start, $end - $start)
$refreshStart = $text.IndexOf('public virtual bool IsRealChanges', [StringComparison]::Ordinal)
$refreshEnd = $text.IndexOf('/// <summary>', $refreshStart, [StringComparison]::Ordinal)
$refresh = $text.Substring($refreshStart, $refreshEnd - $refreshStart)
$generated = "namespace Files.App.Utils { public partial class ListedItem : ObservableObject {`r`n" +
    'protected static readonly CountingFormatter dateTimeFormatter = CountingFormatter.Shared;' + "`r`n" + $properties + "`r`n" + $refresh + "`r`n}}`r`n"
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'ListedDates.generated.cs'), $generated)
[IO.File]::WriteAllText((Join-Path $PSScriptRoot "$Variant-source-hash.txt"), (Get-FileHash $listedFile -Algorithm SHA256).Hash + "`r`n")
