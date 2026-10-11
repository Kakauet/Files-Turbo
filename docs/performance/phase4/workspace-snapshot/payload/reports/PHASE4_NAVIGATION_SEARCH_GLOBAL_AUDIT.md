# Phase IV final — navegación, búsqueda y trabajo periódico

Estado final: **4.2.39.9**, fuente **bf4390ee0a2eed6cece0e99d1a021ce7fd255574**, build08 Release x64 con cero errores y 88 advertencias, entrega **managed-release-phase4-final07**. La [proveniencia del paquete](<C:/Users/guill/Documents/ChatGPT/Files Fast/reports/phase4-build-provenance-firstvisit.json>) relaciona las fuentes compiladas, sus checksums PDB y los ensamblados de la entrega; no constituye una medición de ejecución.

La versión final limita el trabajo periódico de fechas a las filas realizadas, libera resultados nativos tardíos en búsqueda por etiquetas y evita publicaciones redundantes bajo condiciones verificadas. Conserva el debounce de sugerencias, los proveedores, el comparador natural y la ordenación estable de búsqueda. La publicación sí cambia en los casos de igualdad y prefijo descritos abajo.

## Primera publicación: investigación y comportamiento final

La investigación inicial utilizó la captura integrada final06 de Phase III, resumida en [PHASE3_REAL_WORLD_PERFORMANCE.md](<C:/Users/guill/Documents/ChatGPT/Files Fast/reports/PHASE3_REAL_WORLD_PERFORMANCE.md>). En dos navegaciones de 100.000 archivos, envío UIA→Requested tardó 105,02/84,70 ms; Requested→primera publicación de 64 entradas, 30,56/28,62 ms; publicación→callback XAML visible, 75,67/82,97 ms. Ningún trabajo de metadatos de esas operaciones comenzó antes de ese callback. Esa evidencia inicial no atribuía el retraso a metadatos ni justificaba alterar indiscriminadamente los Reset; los hitos tampoco acreditaban presentación física.

La revisión posterior incorporó **TryPublishFilesAndFoldersWithoutReset** en [ShellViewModel.cs](<C:/Users/guill/Documents/ChatGPT/Files Fast/source/src/Files.App/ViewModels/ShellViewModel.cs:1255>). La comparación es lineal y exige las mismas referencias de objetos en el mismo orden; nombres o rutas iguales no bastan. Solo se aplica si la colección no está agrupada y DirectoryGroupOption es None:

- Mismo tamaño y referencias: conserva la colección y evita Clear/Add/Reset, incluido el caso vacío y colecciones grandes.
- Colección actual no vacía que es prefijo exacto del nuevo snapshot, con una cola de hasta 128 entradas: añade solo esa cola mediante Add individuales fuera de una operación bulk. Cada evento contiene una entrada; el límite acota la ráfaga de eventos de UI.
- Reordenación, eliminación, sustitución de referencias, agrupación, publicación inicial no vacía o cola mayor de 128: conserva la ruta bulk anterior de Clear/AddRange/EndBulkOperation con Reset y ordenación de grupos cuando procede.

Las comprobaciones de token/filtro, snapshot tras obtener el semáforo, cancelación y liberación del semáforo se conservan. Las tres rutas mantienen las actualizaciones comunes de información del directorio. Los marcadores opt-in DataPublicationUnchanged, DataPublicationAppended y DataPublicationReset permiten distinguirlas sin registrar rutas.

[Publication firstvisit-ready](<C:/Users/guill/Documents/ChatGPT/Files Fast/reports/phase4-publication-contracts-firstvisit-ready.json>) confirma **24/24 contratos** con los métodos literales y las colecciones bulk/concurrent/grouped reales. Comprueba igualdad, 64→99 con 35 Add individuales, límites 128/129, reordenación, eliminación, reemplazo, agrupación y rechazo de filtros/cancelaciones obsoletos. La conservación de filas, imágenes y selección se prueba en un consumidor controlado sensible a Reset; no es una prueba de todos los suscriptores WinUI ni de píxeles presentados.

## Navegación UIA final

El [resumen navigation-final07](<C:/Users/guill/Documents/ChatGPT/Files Fast/benchmarks/results/phase4/navigation-final07-summary/summary.json>) compara las sesiones OFF de Phase III y de la entrega .9, con diez observaciones por escenario y variante. La tabla muestra **StatusFullCountMs**, desde antes de la búsqueda del setter/SetValue hasta observar el recuento completo por UIA. Los percentiles de este resumen usan interpolación lineal sobre las observaciones ordenadas.

| Entradas | Phase III p50 / p95 ms | Phase IV .9 p50 / p95 ms |
|---:|---:|---:|
| 1.000 | 202,86 / 282,94 | 231,83 / 271,11 |
| 10.000 | 292,28 / 503,70 | 226,90 / 289,14 |
| 100.000 | 1.201,27 / 1.639,20 | 1.156,09 / 1.272,99 |

El resultado es mixto: baja la mediana del recuento completo para 10.000 y 100.000 entradas y sube para 1.000. En 100.000, la mediana de AccessibleNamesMs pasa de 248,17 a 231,95 ms; es disponibilidad de texto del viewport en el proveedor UIA. El recuento completo indica finalización de colección/proveedor, no finalización del layout. El coste del observador y los estados de caché sin controlar impiden atribuir toda diferencia a una única optimización. Diez muestras no establecen garantías de cola. Estas métricas no miden presentación física, FPS ni ratios frente a Explorer.

## Trabajo de fechas en reposo

El timer de BaseShellPage ejecuta una vez por segundo con formato Application. Phase III recorre todos los elementos mediante BlockingListEnumerator y sus locks; vuelve a formatear las fechas que ya tienen etiqueta cacheada para detectar cambios. Reducir simplemente la frecuencia alteraría las etiquetas relativas.

La captura inicial [analysis-segmented.json](<C:/Users/guill/Documents/ChatGPT/Files Fast/benchmarks/results/phase4/baseline-III-profile/analysis-segmented.json>) contenía 36 muestras UI Idle cuya pila pasaba por Monitor.Enter_Slowpath → UpdateDateDisplay → DispatcherQueueTimer, entre 6.393 muestras UI de esa ventana. Identificaba trabajo periódico evitable; incluía esperas y no demostraba un porcentaje de CPU ni un cuello dominante.

La versión final mantiene el refresco completo para cambio de formato y activación de tab. El timer habitual usa los contenedores realizados de BaseLayoutPage, copia ese diccionario y comprueba que cada contenedor conserva el mismo item, navegación y estado de vida. Una notificación puede reciclar otro contenedor sin invalidar el recorrido. La fase 0 refresca la etiqueta cacheada; una fila que vuelve al viewport no conserva una etiqueta antigua. Se mantiene la actualización relativa por segundo de las filas realizadas.

### Contratos y banco administrado de fechas

[Date firstvisit-ready](<C:/Users/guill/Documents/ChatGPT/Files Fast/reports/phase4-date-contracts-and-benchmark-firstvisit-ready.json>) es la evidencia autoritativa final: **8/8 contratos y 120 muestras administradas**, Release con cero errores y advertencias. La preparación extrae literalmente UpdateDateDisplay actual y Phase III, propiedades de fecha/IsRealChanges/UpdateReal de ListedItem, el refresco de BaseLayoutPage, su guarda de contenedor y el bloque de fecha de realización. Enlaza ConcurrentCollection, BlockingListEnumerator y ApplicationDateTimeFormatter reales; sustituye app/servicios, recursos localizados y contenedores. Los recursos controlados usan en-US. No mide WinUI, bindings, render o todas las localizaciones.

Los contratos cubren etiquetas lazy, ausencia de notificación para etiquetas estables, filas explícitas y viewport vacío, realización fase 0, reciclaje, rebinding, disposal, navegación ausente y reciclaje por reentrada. El banco mantiene 100.000 entradas y 100 realizadas, dos condiciones de etiquetas cacheadas, calentamiento previo y 20 muestras por variante/condición en orden alternado. Las tres variantes son recorrido III completo, IV con 100 filas explícitas y IV a través de BaseLayoutPage con 100 contenedores realizados.

El JSON conserva muestras crudas, sin percentiles precalculados. Para esta tabla, p50 es la media de los dos valores centrales y p95 usa **nearest-rank: posición ceil(0,95 × 20) = 19**, sin interpolación. Las asignaciones son medianas por tick, no valores garantizados.

| Etiquetas cacheadas | III completo p50 / p95 ms | IV filas explícitas100 p50 / p95 ms | IV BaseLayout realizado100 p50 / p95 ms | Mediana bytes/tick III → IV realizado |
|---:|---:|---:|---:|---:|
| 100 | 3,0732 / 3,3533 | 0,0493 / 0,0983 | 0,0526 / 0,0677 | 20.944 → 22.600 |
| 100.000 | 22,0956 / 63,2866 | 0,0330 / 0,0709 | 0,0389 / 0,0886 | 11.200.144 → 13.000 |

La primera condición añade una copia de contenedores y elimina el recorrido completo; no reduce las asignaciones cuando solo 100 etiquetas estaban cacheadas. La segunda representa una retención controlada de etiquetas de toda la carpeta; no se afirma que ocurra siempre. En esa condición, las asignaciones III variaron entre 11.200.144 y 20.800.144 bytes y las IV realizadas entre 13.000 y 22.600. Son tiempos y asignaciones de métodos del harness; no equivalen a porcentajes de mejora de la aplicación.

## Cancelación y recursos de búsquedas por etiquetas

FolderSearch.SearchTagsAsync esperaba la apertura FindFirstFileEx mediante WithTimeoutAsync(5s). Un handle devuelto después del timeout podía quedar sin owner que lo liberase; cancelar tampoco interrumpía inmediatamente la espera.

La fuente final utiliza **StorageEnumerationTask.AwaitAsync**, el helper ya existente en navegación, con el timeout de cinco segundos. La cancelación interrumpe la espera del caller; la operación nativa puede continuar y conserva su recurso mientras lo usa. Si pierde su owner, la limpieza libera el resultado tardío al terminar. Un resultado puntual exitoso sigue siendo propiedad del caller. Esta corrección no añade proveedores, cachés, P/Invoke ni cambios de semántica de búsqueda.

[Search-resource firstvisit-ready](<C:/Users/guill/Documents/ChatGPT/Files Fast/reports/phase4-search-resource-contracts-firstvisit-ready.json>) confirma **5/5 contratos**, Release con cero errores y advertencias. Usa el bloque Task.Run/await literal y el helper real, con apertura nativa controlada: ownership/data de éxito, cancelación antes de completar el nativo, timeout de cinco segundos, fallo nativo y 20 cancelaciones repetidas con liberación única del handle tardío. No consulta una base real de etiquetas ni simula un proveedor COM.

## Regresión final y alcance de exactitud

La ejecución final identifica la misma fuente bf4390ee en ambos índices: [168 contratos enfocados](<C:/Users/guill/Documents/ChatGPT/Files Fast/reports/phase4-focused-contracts-firstvisit-ready-index.json>) y [159 contratos de regresión](<C:/Users/guill/Documents/ChatGPT/Files Fast/reports/phase4-regression-contracts-firstvisit-ready.json>), **327/327 pasados**. Son rebuilds Release nuevos y secuenciales, con hashes de fuentes, extracciones, informes y referencia interop. Los contratos de publicación24, fechas8 y recursos5 citados arriba ya forman parte de los 168; no se suman de nuevo.

Las siete familias de regresión contienen navegación de rutas37, working directory/breadcrumb41, navegación Shell/enumeración controlada22, layout adaptativo17, búsqueda nativa por nombre28, recursión/junction3 y planificación de sugerencias11. Las 42 comprobaciones específicamente de búsqueda pertenecen a las tres últimas familias; las de nombre y recursión usan una fixture NTFS sintética nueva, con ciclo de junction controlado. La semántica de consulta, resultados, ordenación estable y cancelación queda cubierta dentro de esos escenarios. Esto no transforma los 159 contratos en pruebas de búsqueda real sobre todos los proveedores o carpetas del usuario.

Se conserva el logging reducido de UpdateDateDisplay. AppMemoryHelper mantiene su política anterior de actividad y coalescing, compacción/working-set trim al cerrar ventana y GC no bloqueante; este banco no establece su impacto en pausas. Los candidatos de lectura de desktop.ini y Git HEAD por lote siguen sin cambios ni mejoras atribuidas aquí.

## Reproducción

Desde la raíz del workspace, con una fuente de producción limpia, la referencia interop Release disponible y sin otra compilación activa, utiliza un sufijo nuevo. Sustituye rerunNN por un identificador que no tenga informes existentes:

~~~powershell
./tooling/Test-Phase4FocusedContracts.ps1 -RunSuffix rerunNN
./tooling/Test-Phase4FinalContracts.ps1 -RunSuffix rerunNN -ExpectedSourceHead bf4390ee0a2eed6cece0e99d1a021ce7fd255574
~~~

Ambos launchers cargan Enter-WorkspaceEnvironment, preparan las extracciones necesarias, reconstruyen y ejecutan secuencialmente, comprueban cada código de salida y conservan las evidencias anteriores. El launcher enfocado obtiene el TargetFramework del XML de cada proyecto para resolver su DLL; evita asumir una ruta net10.0 o Windows que corresponda a otro harness. La reproducción usa fuentes y colecciones reales donde se indica y dobles controlados donde se declara; no ejecuta mediciones de UI.
