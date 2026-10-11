# Files Turbo IV — entrega y recuperación

11 de octubre de 2026. **Final07 construido, verificado, registrado, medido y abierto para entrega con diagnóstico OFF.** La fuente está en **codex/files-turbo-phase4**, commit **bf4390ee0a2eed6cece0e99d1a021ce7fd255574**, versión **4.2.39.9**. Build08 Release x64 terminó con **0 errores y 88 advertencias, en 1:52,50**. Pasan **327/327 contratos**. El paquete corresponde a esa fuente limpia y el registro seleccionó el layout correcto. El tag local files-turbo-phase4 está previsto y pendiente de verificación tras guardar el snapshot documental.

La revisión .9 conserva planificación visible superior→inferior, decode físico con DPI/proporción, la ruta síncrona acotada para PNG pequeños y el overlay separado de imagen. Además registra la referencia del icono genérico realmente asignado y evita una segunda aplicación Low solo si la referencia exacta y el estado del item siguen vigentes. La mediana capturada de completar las 52 imágenes es **659,79 ms**, frente a **1.320,87 ms en III**; las cifras .8 se conservan como evidencia histórica.

## Binario candidato

**C:\Users\guill\Documents\ChatGPT\Files Fast\artifacts\development-layout\managed-release-phase4-final07**

Contiene layout, Dependencies, package-validation.json y el MSIX de desarrollo. El inventario comprende **1.141 archivos del layout y un paquete de dependencia**. El MSIX no está firmado. Todos los paquetes anteriores y sus resultados se conservan.

| Dato | Valor |
|---|---|
| Identidad / Publisher | FilesTurbo.Experimental / CN=Files |
| Versión / configuración | 4.2.39.9 / x64 Managed Release |
| Protocolo / alias | files-turbo / files-turbo.exe |
| Recuperación III | managed-release-phase3-stable06, versión 4.2.39.4 |
| Fuente III recuperable | 1b66432ad3baf122aa4e608b67ea47a5401a78ed |
| Tag final IV | files-turbo-phase4, previsto; verificación local pendiente |
| Build | phase4-managed-release-08, 0 errores, 88 advertencias, 1:52,50 |
| Provenance | reports/phase4-build-provenance-firstvisit.json, compiled-source-and-packaged-assembly-match |
| Registro real | registration-20261011-181315-87e236f4.json, registered, sin error |
| Apertura OFF primaria medida | reports/phase4-final07-off-launch.json, PID 28760 |
| Apertura OFF de entrega | reports/phase4-final07-off-delivery-launch.json, PID 38424, HWND 4655090 |

SHA-256 del **MSIX de desarrollo repaquetado final07**:

**FD48E145CA4FCB9604C3626BE9AC0B336B970818EEA3CCCED12193F10A3A35CD**

SHA-256 de **Files.dll final07**:

**8475A90D239F6158440A2E06BE2818DA64BAA97C00E60C18750BF1CDF1218656**

[phase4-build-provenance-firstvisit.json](phase4-build-provenance-firstvisit.json) contrasta los **nueve C# cambiados**, sus checksums Portable PDB, identidades PDB/assembly y igualdad de assemblies build/layout. El working tree de producción estaba limpio. Manifiesto, XAML y otras entradas fuera de PDB tienen hashes separados. El MSIX de entrada del build tiene hash **F6AF05CB7075AAA1D9F7AFD1E2F0A212CFB5816C7BD64781727103F351519727** y permanece conservado; se distingue del MSIX de desarrollo anterior. La correspondencia acredita fuente y contenido, sin convertirla en prueba de comportamiento.

El registro pasó desde final06/.8 a final07/.9 y confirmó RequestedLocation. La apertura OFF primaria fue PID 28760. [La apertura de entrega](phase4-final07-off-delivery-launch.json) confirma **PID 38424, HWND 4655090**, paquete, ruta y control listos a **18:31:57,802 UTC**. La configuración de diagnóstico local se eliminó y la carpeta benchmarks/media-phase4 quedó confirmada como ruta actual. No mide arranque físico. El campo histórico UiTested=false de package-validation.json describe la validación de contenido anterior al registro; la ejecución posterior se conserva por separado.

Original, III y IV alternan la identidad experimental existente para comparar. Files original instalado y Windows Explorer siguen disponibles.

## Actualizar y abrir

Usar PowerShell 7.2 o posterior en esta máquina, con el registro experimental ya autorizado. Guardar el trabajo pendiente en Files Turbo: el script solicita cierre de su ventana y, si no termina en tres segundos, finaliza únicamente el ejecutable del layout experimental registrado.

~~~powershell
$taskWorkspace = 'C:\Users\guill\Documents\ChatGPT\Files Fast'
$taskDelivery = 'artifacts/development-layout/managed-release-phase4-final07'
& "$taskWorkspace\tooling\Register-Phase4Experimental.ps1" -Delivery $taskDelivery -ValidateOnly
& "$taskWorkspace\tooling\Measure-Phase3ManualSession.ps1" -Action Disable
& "$taskWorkspace\tooling\Register-Phase4Experimental.ps1" -Delivery $taskDelivery
& "$taskWorkspace\tooling\Start-Phase4Experimental.ps1"
$taskFixture = Join-Path $taskWorkspace 'benchmarks\synthetic\1000'
Start-Process ('files-turbo:?folder=' + [Uri]::EscapeDataString($taskFixture))
~~~

Los defaults apuntan a **final07/.9 y provenance-firstvisit**. ValidateOnly comprueba contenido, dependencias, hashes, identidad x64 e inventario preservado de III sin cambiar el registro. Disable desactiva diagnóstico optativo para el siguiente proceso y conserva las trazas. Start-Phase4Experimental vuelve a exigir layout, identidad, versión y hash de Files.dll coincidentes y espera un HWND no cero con CurrentPathSet del proceso exacto. La apertura real final07 ya pasó; la salida documenta readiness de ventana/control.

El registro rechaza destinos fuera de development-layout y enlaces, exige la identidad experimental ya instalada y guarda el resultado en artifacts/phase4-recovery/registration-*.json. No instala runtimes, certificados ni cambia políticas. Un fallo de validación impide el registro y devuelve su error.

Confirmar el destino:

~~~powershell
Get-AppxPackage -Name FilesTurbo.Experimental |
    Select-Object Name, Publisher, Version, InstallLocation
~~~

Debe mostrar 4.2.39.9 y el layout final07. Un registro o lanzamiento correcto no acredita todas las operaciones del explorador; [FINAL_STABILITY.md](FINAL_STABILITY.md) delimita la cobertura de cada snapshot.

## Recuperar Phase III

Entrega íntegra:

**C:\Users\guill\Documents\ChatGPT\Files Fast\artifacts\development-layout\managed-release-phase3-stable06**

~~~powershell
$taskWorkspace = 'C:\Users\guill\Documents\ChatGPT\Files Fast'
& "$taskWorkspace\tooling\Register-Phase4Experimental.ps1" -RestorePhase3 -ValidateOnly
& "$taskWorkspace\tooling\Measure-Phase3ManualSession.ps1" -Action Disable
& "$taskWorkspace\tooling\Register-Phase4Experimental.ps1" -RestorePhase3
$taskFixture = Join-Path $taskWorkspace 'benchmarks\synthetic\1000'
Start-Process ('files-turbo:?folder=' + [Uri]::EscapeDataString($taskFixture))
~~~

La recuperación se ejecutó realmente: registration-20261011-162434-c0c7977d.json documenta el regreso desde el Original adaptado a III, sin error. El inventario de inicio está en artifacts/phase4-recovery/phase3-inventory.json y se contrasta antes de actualizar o restaurar.

Restaurar cambia el paquete registrado y conserva datos normales de ejecución; no revierte cada preferencia. No borrar un layout registrado. Se preservan tags files-turbo-baseline, files-turbo-phase1, files-turbo-phase2, files-turbo-phase3 y files-turbo-phase4-start; el último es anotado y se resuelve con ^{commit}. **No se ha hecho push.**

## Referencias .8 y .7 preservadas

Final06, versión **4.2.39.8**, fuente **081841c996fd02bc24d901ce3092764dd545bc08**, corresponde a build07: **0 errores, 88 advertencias, 1:57,01**. [phase4-build-provenance-speed.json](phase4-build-provenance-speed.json) contrasta ocho C# sobre fuente limpia. registration-20261011-175255-789352aa.json confirmó su registro; phase4-final06-off-launch.json confirmó PID36356. Sus contratos y medidas son históricos propios de .8.

| Hash SHA-256 de final06 | Valor |
|---|---|
| MSIX de desarrollo | 1D9347C8FA9CF47E7FF121D15BFCA108F40E49E0B113A166A283CC5425A6702E |
| Files.dll | 9207F935EC1EE18C9CDE0B55FAECE45599DDE46B837808419108906161631BE0 |
| MSIX de entrada build07 | FA86C81A2F6D1B323436C60D7DF969AD02941D88D36655375226BCA216BD9118 |

Final05, versión **4.2.39.7**, fuente **95fe88371b6795d935a71e562db2532a74d8b902**, corresponde a build06: **0 errores, 88 advertencias, 2:08,06**. Su provenance-latency verificó ocho C#, y registration-20261011-173040-04d91c5c.json confirmó el layout correcto. Se preservan sus medidas y trazas propias, sin atribuirlas a .8 o .9.

| Hash SHA-256 de final05 | Valor |
|---|---|
| MSIX de desarrollo | 753547BEF7D0270C47CBA6BCA40875B98BF2F41766F91C21B818B5991A51F870 |
| Files.dll | 7AA38E94DCF2500A6E0600235112E4741A146C573628523D0B5BF9FB59D02238 |
| MSIX de entrada build06 | AE395334668B0696F68FDD5FE2972C609A7606DC04C0D86708FA61D635800BC4 |

## Provenance anterior conservado

Final04 conserva la fuente **e9035aed70a1620ab13c2282e94a7b47facbd2b5**, versión **4.2.39.7**, build phase4-managed-release-05: **0 errores, 89 advertencias, 2:12,76**. Su package-validation.json pasó como content-validated-not-installed, UiTested=false. Una advertencia nueva CsWinRT1034 del cast ScrollViewer se corrigió declarando su metadata en 95fe883; por ello final04 fue sustituido por final05 antes de registrarlo o medirlo. No tiene cifras de UI propias.

| Hash SHA-256 de final04, sustituido | Valor |
|---|---|
| MSIX de desarrollo | 5CB2C1CD8F7FE1E5E8FAF39270C8A566CBDA77423A16403D89333C555984F235 |
| Files.dll, inventario de contenido | 49E6D7DCA450D2053CD254B97E5FBAAAAB368A17F6F7E54014F9B072345F3570 |

Final03 conserva fuente **6e9a7f9b46dcc0d1c818390efb3a891da0268c5d**, versión **4.2.39.6**, build phase4-managed-release-04: **0 errores, 88 advertencias, 2:01,22**. Su layout está en artifacts/development-layout/managed-release-phase4-final03 y contiene 1.141 archivos inventariados más cuatro dependencias. El MSIX no está firmado y se registró mediante el flujo de desarrollo existente.

| Hash SHA-256 de final03 | Valor |
|---|---|
| MSIX | AE80A0719ACFEBA215A6D87B855E61DB76B24E2827D1B8D5EAC7D2464B490167 |
| Files.dll | D615236EE4E44566AFCA5B027B3E4BB6D9A413CF9F4D5B03B4FE900065AB730E |

[phase4-build-provenance-registered.json](phase4-build-provenance-registered.json) contrasta los siete C# de esa revisión con Portable PDB y los assemblies empaquetados; el working tree de producción estaba limpio. registration-20261011-164724-dad02b4f.json confirma su registro sin error. Son comprobaciones del candidato previo, conservadas sin atribuirlas a revisiones posteriores.

Final02 permanece como evidencia del ajuste be301a0. Su MSIX tiene SHA-256 **7651BF5527AE271FDF5CD431ABDF44D3A4998003A5BAAC87015E060B2795B0B6** y Files.dll **BE8354FCADF386D1D7833A4A5849F64304BA5978FC350E605EAC7FE31CBD6594**. Su provenance pasó, pero registration-20261011-164136-5efae3f8.json registra estado failed y «Registration did not select the requested layout». La guarda detectó final01 como layout efectivo. Final03 utiliza la versión superior 4.2.39.6; se conservan ambas revisiones anteriores.

Final01 es una evidencia previa y recuperable, no el binario de la fuente final. Su fuente es **1f03789693a4a515a496e67d5d34ee5ed9faa8f6**, su build phase4-managed-release-02 y su paquete contiene 1.141 archivos de layout y cuatro dependencias inventariadas. Se verificaron manifiesto, servidor, auxiliares y hashes; los siete C# modificados coincidieron con los checksums de Portable PDB, su identidad y los assemblies empaquetados.

| Hash SHA-256 de final01 | Valor |
|---|---|
| MSIX | 6F8CB10186CD5A7F174082E6FCCF1A1F95C6F7170FBAFF7524AB276FAB97448C |
| Files.dll | BE30E530BE585CB88AA642C3A5C88FA37C8F270729C6FF002844253F95E44C2C |

[phase4-build-provenance-core.json](phase4-build-provenance-core.json) conserva la comprobación. registration-20261011-163530-6221e11e.json confirma su registro real. El histórico UiTested=false de package-validation describe la finalización de contenido; las ejecuciones posteriores se documentan por separado.

## Requisitos y alcance

El layout declara Microsoft.NETCore.App 10.0.0; el servidor admite rollForward LatestMinor. El manifiesto requiere Microsoft.WindowsAppRuntime.2 desde 2.5.1.0. Esta máquina ya tenía componentes para ejecutar la identidad experimental. Dependencies conserva un paquete; el script no lo instala.

La .9 ejecutó **327/327 contratos**, cero fallos: [168 focalizados](phase4-focused-contracts-firstvisit-ready-index.json) y [159 regresiones](phase4-regression-contracts-firstvisit-ready.json), sobre bf4390ee limpio. Los focalizados incluyen 21 de scheduling, 60 de miniaturas/STA/decode/handoff, 24 de publicación, 24 de cache de iconos, 26 de integración, ocho de fechas y cinco de recursos de búsqueda. WinUI, Shell y decoder están controlados en parte; STA y parte de NTFS son reales. No acreditan todos los proveedores o presentación física.

Las miniaturas OFF .9 completaron diez aperturas de **52/52 imágenes** y diez de **3/3 vídeos**, sin timeout. Primera imagen p50/p95 **524,18/631,03 ms**, todas **659,79/733,35 ms**. Filas superior→inferior 10/10, con empate entre varias filas del primer sondeo. Vídeo primera/todas **233,58/284,60 ms**: mejor que III y peor que .8. Tres primeras visitas a rutas nuevas completaron 52/52 en **1.259,18/1.025,67/1.099,55 ms**, sin baseline fría equivalente.

La navegación .9 completó **40 muestras**, diez por cada 1k/10k/50k/100k, cero timeouts. Contador 100k p50/p95 **1.156,09/1.272,99 ms** y primera fila **231,95/282,27 ms**. Frente a III el contador mejora en mediana en 10k/50k/100k, pero empeora en 1k (231,83 frente a 202,86 ms).

La .9 completa **60 movimientos UIA**, **20 ciclos rápidos/140 solicitudes/20 confirmaciones finales**, cero timeouts. [phase4-app-interactions-final07.json](phase4-app-interactions-final07.json) acredita siete operaciones reales Passed: selección, multiselección, copiar/pegar, cortar/pegar, renombrar, atrás/adelante y toggle de preview. Crear carpeta, nueva pestaña, orden y layout siguen Pending por cobertura incompleta, sin afirmar fallo. Los ciclos verifican estado final tras reposo, y el toggle de preview no acredita todo su contenido. No son FPS físicos o aceptación exhaustiva.

Los perfiles ON .9 sin capturas permanecen separados: primera aplicación **590,26/226,14 ms** en dos aperturas; ruta nueva **558,62 ms**, última **1.398,67 ms** desde Requested. Un decode alcanza **23,10 ms**. El reloj interno, falta de RowId/modo y cobertura incompleta de frames impiden inferir cadena causal por fila, bloqueo síncrono o mejora uniforme ON. La entrega vuelve a proceso OFF 38424; solo falta verificar el tag local previsto después del snapshot documental.

La referencia .8 ejecutó **289/289 contratos**: 130 focalizados y 159 regresiones, cero fallos. Tiene 40 navegaciones, 60 movimientos UIA, 20 ciclos rápidos/140 solicitudes, diez aperturas de 52 imágenes, diez de tres vídeos y tres primeras visitas, sin timeouts en esos escenarios. Siete operaciones reales de fixtures pasaron; crear carpeta, nueva pestaña, orden y layout quedaron Pending por cobertura incompleta. Los contratos sustituyen parte de Shell/WinUI/decoder y no prueban todos los proveedores o presentación física.

Las aperturas repetidas .8 completaron 52 imágenes con p50/p95 **708,90/796,27 ms**, primera **541,54/589,82 ms**, frente a III **1.320,87/1.383,10** y **702,66/753,80 ms**. Las filas finalizaron superior→inferior 10/10; el primer sondeo contenía varias filas y no ordena los primeros cuatro eventos físicos. La primera visita fue más lenta, 1,19–1,39 s para completar el viewport, sin baseline fría equivalente. Esos datos motivaron .9 y quedan separados.

Navegación .8 tiene resultados mixtos frente a III, con medianas mayores en 1k y 100k. Explorer completó 40 navegaciones propias, pero COM/UIA y viewport interno distintos impiden calcular una superioridad entre aplicaciones. No hay FPS físicos, RAM comparable en reposo o miniaturas Explorer. [FINAL_EXPLORER_COMPARISON.md](FINAL_EXPLORER_COMPARISON.md) y [MORNING_REPORT_PHASE4_FINAL.md](../MORNING_REPORT_PHASE4_FINAL.md) conservarán esas limitaciones junto a la nueva evidencia.
