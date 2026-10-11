# Snapshot recuperable del workspace — Phase III

Este directorio conserva scripts, proyectos, fuentes de contratos, miembros extraídos y pequeños registros finales. Las rutas de su contenido reproducen las rutas relativas del workspace: `tooling/`, `tests-extra/`, `benchmarks/`, `reports/` y `artifacts/`. La aplicación se recupera mediante el commit de Git que contiene este snapshot, en `source/`; este directorio no duplica sus fuentes de producción.

La referencia exacta de comienzo es `files-turbo-phase3-start` = `b18efc91ed3033081691f407a2bb7b5ee3d7d21d`, el commit final de Phase II. `files-turbo-phase2` debe resolver al mismo commit. La referencia original de `source-baseline/` es `1994d9a4269f5144c70bc67d6a48de4a20616912`.

`snapshot-inventory.json` registra el SHA256 original y el almacenado de cada archivo pequeño. El snapshot utiliza CRLF. `Restore-WorkspaceSnapshot.ps1` reconstruye los LF originales cuando corresponda, verifica ambos hashes y conserva los archivos existentes: sólo crea archivos ausentes y acepta los que ya son idénticos. Un archivo diferente detiene la restauración antes de escribir. Así no sobrescribe informes históricos ni modificaciones locales.

La `.gitattributes` local desactiva la normalización de texto sólo dentro de este directorio de evidencia: Git conserva los bytes CRLF y los espacios finales históricos, incluso con `core.autocrlf=false`. Así siguen siendo válidos los hashes al clonar o restaurar el commit. No modifica los atributos de las fuentes de aplicación. Este archivo de atributos y los helpers/inventarios propios del snapshot pertenecen al repositorio; no se copian sobre rutas de aplicación durante la restauración.

Cada actualización de `Capture-WorkspaceSnapshot.ps1` conserva primero el inventario anterior y todos sus archivos en `history/`, con rutas y hashes propios. `snapshot-inventory-initial.json` identifica la primera captura; `README-initial.md` conserva su explicación inicial. Para recuperar una versión histórica, pasar su nombre de inventario a `Restore-WorkspaceSnapshot.ps1 -InventoryName snapshot-inventory-initial.json` sobre un workspace sin archivos diferentes.

No contiene SDK, ejecutables, ensamblados, paquetes NuGet, paquetes/dependencias MSIX, fixtures, bancos CSV completos ni nettrace. El responsable de la entrega incorpora los informes GUI, el informe integrado, el morning report y los JSON pequeños de las sesiones definitivas mediante una actualización explícita cuando estén cerrados. El inventario enumera lo incluido en cada captura. Los registros de contratos son evidencia histórica, con las rutas de la máquina original; al mover el workspace se deben repetir los contratos y la procedencia con nombres nuevos.

## Restaurar las rutas relativas

Desde la raíz de un workspace con el checkout final de Phase III en `source/`, en PowerShell 7.2 o posterior:

```powershell
$phase3Snapshot = Join-Path (Resolve-Path source).Path 'docs/performance/phase3/workspace-snapshot'
& (Join-Path $phase3Snapshot 'Restore-WorkspaceSnapshot.ps1') -WorkspaceRoot (Get-Location).Path -WhatIf
& (Join-Path $phase3Snapshot 'Restore-WorkspaceSnapshot.ps1') -WorkspaceRoot (Get-Location).Path
git -C source rev-parse files-turbo-phase3-start
git -C source rev-parse files-turbo-phase2
New-Item -ItemType Directory -Path reports, artifacts -Force | Out-Null
```

Si `source-baseline/` no existe, preparar un worktree de la referencia original, sin modificar `source/`:

```powershell
git -C source worktree add --detach ../source-baseline 1994d9a4269f5144c70bc67d6a48de4a20616912
```

## Compilación y contratos

Instalar o recuperar el SDK portátil 10.0.401 dentro de `tooling/`; el instalador consulta metadatos oficiales y comprueba SHA512. Se necesitan los componentes de Windows/Visual Studio y las dependencias nativas del proyecto. El snapshot no los contiene. `Build-NativeHelpers.ps1` conserva el requisito existente de VS2022 y de `tooling/nuget.exe` firmado por Microsoft; no instala Visual Studio. Ejecutar las compilaciones secuencialmente.

```powershell
& tooling\Install-LocalSdk.ps1 -Version 10.0.401
. tooling\Enter-WorkspaceEnvironment.ps1
$phase3Dotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
& tooling\Build-NativeHelpers.ps1
& tooling\Build-FilesTurbo.ps1 -Managed -Package -Configuration Release -OutputName phase3-recovery-build

& tests-extra\Phase3Navigation\Prepare-ShellContracts.ps1
& $phase3Dotnet build tests-extra\Phase3Navigation\NavigationContracts.csproj -c Release
& $phase3Dotnet tests-extra\Phase3Navigation\bin\Release\net10.0\NavigationContracts.dll reports/phase3-recovery-navigation.json
& $phase3Dotnet build tests-extra\Viewport\ViewportContracts.csproj -c Release
& $phase3Dotnet tests-extra\Viewport\bin\Release\net10.0\ViewportContracts.dll reports/phase3-recovery-viewport.json
& $phase3Dotnet build tests-extra\Adaptive\AdaptiveContracts.csproj -c Release
& $phase3Dotnet tests-extra\Adaptive\bin\Release\net10.0\AdaptiveContracts.dll reports/phase3-recovery-adaptive.json
& tests-extra\Phase3PathNavigation\Prepare-PathContracts.ps1
& $phase3Dotnet build tests-extra\Phase3PathNavigation\PathNavigationContracts.csproj -c Release
& $phase3Dotnet tests-extra\Phase3PathNavigation\bin\Release\net10.0\PathNavigationContracts.dll reports/phase3-recovery-path-navigation.json
& tests-extra\Phase3WorkingDirectory\Prepare-WorkingDirectoryContracts.ps1
& $phase3Dotnet build tests-extra\Phase3WorkingDirectory\WorkingDirectoryContracts.csproj -c Release
& $phase3Dotnet tests-extra\Phase3WorkingDirectory\bin\Release\net10.0\WorkingDirectoryContracts.dll reports/phase3-recovery-working-directory.json
& tests-extra\EntryConstruction\Prepare-EntryContracts.ps1 -SnapshotName recovery
& $phase3Dotnet build tests-extra\EntryConstruction\EntryConstruction.csproj -c Release -p:SnapshotRoot=snapshots/recovery
& $phase3Dotnet tests-extra\EntryConstruction\bin\Release\net10.0\EntryConstruction.dll --output artifacts/phase3-recovery-entry
```

Cada nombre de salida debe ser nuevo. Las aserciones de navegación/cola utilizan servicios controlados; Adaptive enlaza su clase completa con dependencias sustituidas; EntryConstruction compila miembros reales extraídos. Estos contratos no acreditan píxeles visibles, shell/COM universal ni operaciones de archivos destructivas.

Para reproducir el banco de preparación de sugerencias y sus 11+28+3 contratos, primero generar las carpetas sintéticas y compilar las dos dependencias de los contratos nativos. El proveedor CsWin32 proviene del build Release de la aplicación:

```powershell
& tooling\New-SyntheticDataset.ps1 -Counts 1000,10000,50000,100000 -MaxTotalFiles 161000
& $phase3Dotnet build tests-extra\Search\Search.Tests.csproj -c Release
& $phase3Dotnet build benchmarks\Search\Search.csproj -c Release
& tooling\Run-Phase3SearchBenchmarks.ps1 -RunName phase3-recovery-search -Repetitions 3
```

La métrica del banco termina en un callback; no mide resultados visibles de WinUI. Los resultados originales completos quedan en el workspace entregado, fuera de este snapshot.

## Layout y procedencia

`Complete-DevelopmentPackage.ps1` acepta el MSIX recién generado, el output Release del servidor y un destino nuevo dentro de `artifacts/development-layout/`. Ejemplo que obtiene esas rutas mediante los propios outputs relativos:

```powershell
$phase3Package = Get-ChildItem artifacts/phase3-recovery-build-package -Recurse -Filter 'Files.App_*_x64.msix' | Select-Object -First 1
$phase3Server = (Resolve-Path source/src/Files.App.Server/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64).Path
$phase3Destination = Join-Path (Get-Location).Path 'artifacts/development-layout/phase3-recovery-layout'
$phase3DependencyDirectories = @(Join-Path $phase3Package.Directory.FullName 'Dependencies' | Where-Object { Test-Path -LiteralPath $_ -PathType Container })
& tooling\Complete-DevelopmentPackage.ps1 -InputPackage $phase3Package.FullName -ServerOutput $phase3Server -DependencyDirs $phase3DependencyDirectories -Destination $phase3Destination
& tooling\Test-Phase3BuildProvenance.ps1 -Delivery artifacts/development-layout/phase3-recovery-layout -OutputReport reports/phase3-recovery-build-provenance.json
```

La verificación final depende también del layout conservado de Phase II, sus inventarios, los bancos completos, el build integrado y los registros de recuperación; esos archivos grandes no se reproducen por copiar este directorio. `Verify-Phase3Result.ps1` comprueba los nombres definitivos de la entrega y exige un checkout limpio/committed. Recuperar los artefactos originales o ejecutar todos los pasos requeridos y revisar explícitamente las nuevas rutas antes de usar esa verificación.

El parámetro opcional `-GuiSessionDirectories` acepta las cuatro carpetas definitivas de Navigation, Selection, Scroll y RapidNavigation. Comprueba versión/layout, registros completos, cultura, fixtures numéricos, las 20/5/20/30 observaciones y los 210 setters. Cuando existen snapshots de instancias experimentales al comienzo/final exige exactamente una y el PID medido; los registros antiguos sin esos campos se marcan como no controlados. Registra hashes de JSON/CSV pequeños y `NoPhysicalPresentation=true`; no compara tiempos ni acredita pixels/FPS. Pasar únicamente las sesiones finales seleccionadas para la entrega, después de cerrar sus mediciones.

La instrumentación offline usa `benchmarks/Analyze-EventPipe.ps1` y la versión local de dotnet-trace/TraceEvent indicada por el script. Instalar esa herramienta dentro de `tooling/diagnostics` cuando se requiera; no se copia ningún trace ni biblioteca diagnóstica. La captura/registro experimental sigue las restricciones descritas en los informes, requiere la instalación experimental ya autorizada y nunca se ejecuta durante esta restauración.

Para actualizar los informes definitivos, desde la raíz del workspace, refrescar las fuentes pequeñas y pasar explícitamente esos informes, el resumen JSON y los session.json seleccionados. El helper permite exactamente `MORNING_REPORT_PHASE3.md` en la raíz; conserva las restricciones de prefijos, extensiones y tamaño para los demás archivos. Rechaza CSV, ejecutables, traces, fixtures y archivos de más de 512 KiB, y conserva todas las versiones anteriores antes de escribir las nuevas copias. Hacer esta actualización antes del commit final; producir después la procedencia y verificación vinculadas a ese HEAD limpio fuera del repositorio.

```powershell
& (Join-Path $phase3Snapshot 'Capture-WorkspaceSnapshot.ps1') -WorkspaceRoot (Get-Location).Path -ExtraPaths reports/PHASE3_MEASUREMENT.md,reports/PHASE3_REAL_WORLD_PERFORMANCE.md,MORNING_REPORT_PHASE3.md,benchmarks/results/phase3/ui-final06-single-off-summary/summary.json
& (Join-Path $phase3Snapshot 'Restore-WorkspaceSnapshot.ps1') -WorkspaceRoot (Get-Location).Path -WhatIf
```
