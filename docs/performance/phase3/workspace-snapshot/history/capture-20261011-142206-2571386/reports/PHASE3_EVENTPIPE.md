# Phase III — EventPipe del proceso experimental 03

La captura encuentra un bloqueo concreto del hilo de interfaz durante el final de una carpeta de 100.000 elementos: la clasificación del diseño adaptativo llama repetidamente a `Directory.Exists` con extensiones ya conocidas. También contiene pausas reales de GC. Las muestras no son medidas de CPU y esta sesión instrumentada se conserva separada de las comparaciones UIA normales.

## Evidencia y alcance

- Captura: `benchmarks/results/phase3/eventpipe03-20261011/navigation.nettrace`, PID **19100**, perfiles `dotnet-common,dotnet-sampled-thread-time`, herramienta local `dotnet-trace` **10.0.750501**.
- Intervalo del trace: **2026-10-11 13:26:06.128Z → 13:26:51.2958838Z**, 45.1679 s; 717.648 eventos y 618.878 muestras de pila. TraceEvent informa cero eventos perdidos, sin truncamiento, sin suspensiones sin pareja.
- SHA-256 del nettrace: `DCEA7A8855872C41C345D56516AF921E108CE8B56220FED5ED08EE2CC7290318`.
- Navegación UIA correlacionada: `ui-PhaseIII-20261011-132634-edd412b9/session.json`, **13:26:34.7444031Z → 13:26:44.4862703Z**, dos repeticiones de 100.000 archivos sintéticos.
- Resultados completos acotados: `top-inclusive.txt`, `top-exclusive.txt` y `managed-analysis.json` (271 KB) junto al trace. El script `benchmarks/Analyze-EventPipe.ps1` enlaza solamente la biblioteca TraceEvent local mediante `Add-Type`, analiza el archivo offline y no modifica la aplicación ni activa capturas globales.

`dotnet-trace collect` en Windows no incluye pilas nativas completas ni eventos del kernel. Las muestras de tiempo de hilo incluyen espera: una frontera WinRT no permite distinguir por sí sola trabajo nativo, espera o presentación física. [Documentación oficial de dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace).

## Espera observable y pila del hilo de interfaz

El ranking exclusivo global está dominado por `Task.InternalWaitCore` (**41,05%**), `LowLevelLifoSemaphore.WaitForSignal` (**26,06%**), el poller de completado IO (**6,86%**) y `WaitOneNoCheck` (**6,84%**). Son porcentajes de tiempo muestreado agregado de hilos, no porcentajes de CPU. El ranking inclusivo de `Program.Main` es **3,43%** del agregado; `IApplicationStaticsMethods.Start` termina muchas pilas en la frontera WinUI.

La transición vacía de repetición 1 tiene UTC externo **13:26:38.4945912Z → 13:26:40.8500146Z**: **2.355,4234 ms**. Su cronómetro interno registra solamente **86,6946 ms**, porque `GetCurrentPattern` se ejecuta antes de iniciarlo. No se atribuye toda la diferencia a ese método cliente sin una pila de UIA; sí se observa qué estaba haciendo el servidor de la interfaz en ese mismo intervalo.

El hilo principal **23812**, identificado por la pila `Files.App.Program.Main`, tiene **1.190 muestras** en el intervalo externo: **1.056 (88,7%)** terminan en `Directory.Exists` y **61** en normalización del path. Las tres pilas principales tienen 360/349/347 muestras y comparten:

```text
Directory.Exists
FileExtensionHelpers.HasExtension
IsVideoFile / IsImageFile / IsAudioFile
AdaptiveLayoutHelpers.GetContentLayout
AdaptiveLayoutHelpers.GetAdaptiveLayout
AdaptiveLayoutHelpers.ApplyAdaptativeLayout
ShellViewModel.RapidAddItemsToCollectionAsync.MoveNext
DispatcherQueueSynchronizationContext.Post
WinUI Application.Start
Program.Main
```

El código pasaba `ListedItem.FileExtension`, por ejemplo `.txt`, al helper general de paths. Para un archivo no multimedia, los tres clasificadores consultaban `Directory.Exists(".txt")`; la extensión se interpretaba como un path relativo y se repetía para toda la colección en el hilo de interfaz. La enumeración de la lista ya estaba publicada, pero el hilo seguía ocupado clasificando el diseño. La coincidencia temporal y las pilas identifican este trabajo como candidato directo para el retraso observado; no convierten las muestras en un coste exacto de CPU ni miden la duración individual de cada llamada nativa.

El subintervalo previo al cronómetro se incluye en JSON con nombre `EmptyRep1PreStopwatchApprox`; su final se **infiere** restando el tiempo interno a la finalización externa. El intervalo externo anterior es la correlación autoritativa. El JSONL antiguo de instrumentación tiene un reloj Stopwatch sin anclaje UTC exacto, por lo que no se inventa una equivalencia absoluta por operación.

## Pausas reales de GC

Se emparejan `GC/SuspendEEStart` y `GC/RestartEEStop` por hilo e instancia CLR, incluyendo solamente `SuspendForGC` y `SuspendForGCPrep`. `SuspendEEStop` permite separar la adquisición de la suspensión del tiempo plenamente suspendido. Esta medida no confunde el intervalo `GCStart → GCStop`, que en GC de fondo puede incluir trabajo concurrente sin detener la aplicación. [Explicación de Microsoft sobre duración y pausa de GC](https://devblogs.microsoft.com/dotnet/work-flow-of-diagnosing-memory-performance-issues-part-2/), [emparejado de eventos en TraceEvent](https://github.com/microsoft/perfview/blob/main/src/TraceEvent/Computers/TraceManagedProcess.cs).

| Intervalo | Pausas GC | Tiempo suspendido acumulado | Pausa máxima |
| --- | ---: | ---: | ---: |
| Trace completo, 45,1679 s | 44 | 675,9067 ms | 78,2677 ms |
| Sesión UIA, 9,7419 s | 40 | 654,4846 ms | 78,2677 ms |
| Transición vacía repetición 1, 2,3554 s | 3 | 5,6616 ms | 2,0535 ms |

Las tres pausas de la transición empiezan a **13:26:39.0597153Z**, **39.7999768Z** y **40.5214997Z**, con 2,0535/1,7936/1,8145 ms. Este GC no explica los aproximadamente 2,26 s previos al cronómetro.

En el trace completo hay 41 suspensiones `SuspendForGC` (646,3150 ms) y tres de preparación de GC de fondo (29,5917 ms). La máxima empieza a **13:26:41.5041442Z**: adquisición 0,0079 ms y suspensión completa 78,2598 ms. Doce pausas superan 16,6667 ms; esto es una duración de suspensión administrada, **no** un conteo de frames perdidos. Las colecciones observadas son 22 gen0, 18 gen1, tres gen2 de fondo y una gen2 inducida. Los snapshots de heap pasan de 33,6 MB al primer GC observado a unos 115,0 MB al último; no son private bytes, working set ni un límite de memoria de la app.

Otros **22.327** pares `SuspendOther`, emitidos durante el muestreo, se agregan por separado y se excluyen de todas las cifras de GC. La sesión tiene sobrecoste de diagnóstico; sus pausas no se trasladan como estimación a una sesión sin el perfil.

## Corrección derivada y validación

`AdaptiveLayoutHelpers.IsMediaExtension` ahora consulta un conjunto inmutable con las **33 extensiones únicas existentes**, comparadas con `OrdinalIgnoreCase`. No se cambia `FileExtensionHelpers.HasExtension` global ni sus comprobaciones de carpetas. Se conservan la clasificación por extensión almacenada, el umbral flotante estrictamente superior al 60%, la muestra predictiva de 200 entradas, la prioridad de `desktop.ini` y los ajustes de diseño fijo/compartido/desactivado.

`tests-extra/Adaptive/AdaptiveContracts.csproj` enlaza **la clase completa de producción**, el helper compartido y los modelos de enum/INI; sustituye únicamente los servicios y el modelo de UI para ejecutar el comportamiento público. **17/17 contratos pasan**, compilación sin errores ni advertencias, en `reports/phase3-adaptive-contracts-final.json`. Comprueban todas las extensiones, mayúsculas, 60/61%, vacío, extensiones nulas/desconocidas, carpetas/extensiones almacenadas, shortcuts por su extensión propia y no por su destino, ZIP con atributo de carpeta, INI, ajustes del usuario, predicción y 100.000 elementos sin acceder a `ItemPath`. Una carpeta sintética relativa llamada `.jpg` prueba que la clasificación conocida ya no depende del directorio de trabajo, mientras el helper general mantiene su comportamiento anterior.

Fuente corregido SHA-256: `78E9727467F4E74CD9B87BFFF713B45E6575EB6620311DB51861EC454F1364AA`. Los 100.000 elementos se clasificaron en 1,935 ms en el proceso del contrato; ese número **no** mide WinUI ni una mejora gráfica. La captura 03 precede a la corrección. La nueva versión necesita su propia captura y el marcador `AdaptiveLayoutCompleted` para validar el tiempo final de la interfaz.

Reproducción offline:

```powershell
. ./tooling/Enter-WorkspaceEnvironment.ps1
& ./tooling/diagnostics/dotnet-trace.exe report benchmarks/results/phase3/eventpipe03-20261011/navigation.nettrace topN -n 30 --inclusive -v
& ./tooling/diagnostics/dotnet-trace.exe report benchmarks/results/phase3/eventpipe03-20261011/navigation.nettrace topN -n 30 -v
./benchmarks/Analyze-EventPipe.ps1 -TracePath benchmarks/results/phase3/eventpipe03-20261011/navigation.nettrace -OutputPath benchmarks/results/phase3/eventpipe03-20261011/managed-analysis-rerun.json
```
