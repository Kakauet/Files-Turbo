# Files Turbo Phase III — rendimiento en la aplicación real

11 de octubre de 2026. Continuación de Phase II `b18efc91ed3033081691f407a2bb7b5ee3d7d21d`. Rama de trabajo `codex/files-turbo-phase3`; referencia de recuperación `files-turbo-phase3-start`. Se conservan las entregas, informes y resultados anteriores.

## Problemas y decisiones

El listado rápido de Phase II todavía esperaba una consulta de icono genérico para cada entrada antes de publicar. Incluso un acierto de caché añadía trabajo y continuaciones por archivo. Win32 y Universal ahora crean los datos esenciales y dejan iconos y miniaturas para los contenedores realizados. La primera publicación nativa llega a 64 entradas o 25 ms de recorrido, lo que ocurra antes; las siguientes conservan la cadencia de 500 ms. No se inventan filas ni se limita el listado completo.

Las listas ya usan los paneles virtualizantes `ItemsStackPanel` y `ItemsWrapGrid`. El problema encontrado estaba en el trabajo asociado a sus filas: solicitudes sin límite compartido, callbacks que podían sobrevivir al reciclaje, recarga de iconos con consultas de contenedor para todos los archivos y fan-out Git para filas invisibles. Se conserva el control y su diseño. Una cola por página atiende primero las filas visibles, con cuatro trabajos completos activos y 128 pendientes. Las llamadas nativas ya iniciadas conservan su plaza hasta terminar; la cancelación descarta resultados obsoletos.

La barra de búsqueda esperaba 200 ms y completaba los iconos antes de mostrar nombres. Ahora usa debounce de 80/160/200 ms según consulta y coste reciente del proveedor y publica nombres antes de enriquecerlos con imágenes. Se mantienen proveedores, búsqueda recursiva, AQS, Unicode y wildcards. No se reutiliza el listado de un solo nivel para responder una consulta recursiva. No se instala Everything ni un servicio de indexación.

La búsqueda principal deja de precargar iconos de todos sus resultados. Cada tick publica como máximo 64 filas individuales; la publicación final conserva todos los resultados ordenados. La cancelación de una búsqueda antigua no puede apagar el estado de carga de una navegación nueva.

Cambiar de carpeta con 100 entradas solicitaba una limpieza de memoria que podía terminar vaciando el working set visible. La solicitud por navegación se limita ahora a 10.000 entradas; los barridos repetidos y el vaciado del working set quedan reservados a la ventana cerrada. Se comunica actividad durante carga/publicación. No se cambian prioridades, afinidad, caché global ni ajustes de Windows. Un GC no bloqueante todavía puede producir pausas; el diagnóstico integrado registra contadores y la captura EventPipe separada mide suspensiones de GC.

## Corrección y revisión independiente

Se añade cancelación por identidad de `ListedItem`, vinculada a navegación y viewport, con token capturado antes de disponer su fuente. Su vida continúa después de retornar la carga principal para cancelar reintentos de miniaturas. Reciclar, cambiar de vista o navegar cancela esos trabajos. Las mutaciones de imágenes y metadatos comprueban vigencia antes de aplicar.

La revisión entre agentes encontró tres riesgos adicionales y se corrigieron: una carga DisplayName obsoleta podía ordenar/resetear la ubicación nueva; el finally de metadatos podía reagrupar etiquetas de otra navegación; y una operación cancelada de una vista anterior podía borrar el estado de una operación nueva sobre el mismo objeto. La ordenación/reagrupación recibe el token y la finalización elimina únicamente el registro del objeto y token que todavía posee. Los reintentos cloud observan su cancelación esperada.

El estrés gráfico de la versión preliminar 4.2.39.2 encontró una carrera real tras diez ciclos correctos: el siguiente ciclo terminaba solicitando 1k, pero seguía en 100k después de 30 s. El JSONL termina en la operación 56 de 100k y no contiene una petición posterior. Resolver una ruta podía continuar después de otra entrada; una entrada que coincidía con la carpeta todavía actual se descartaba sin invalidar esa resolución anterior. Modern y legacy comparten ahora una revisión de envío avanzada antes de los retornos por igualdad o vacío. Se descartan continuaciones antiguas tras los awaits y antes de acciones posteriores. Los breadcrumbs dejan de decidir si una navegación es redundante.

SetWorkingDirectoryAsync y la publicación de breadcrumbs también reciben una revisión por llamada. Una respuesta antigua de raíz de unidad, Git o componentes de ruta no puede sobrescribir la nueva. La barra y el shell evitan su retorno por carpeta actual mientras está pendiente el cambio efectivo de ubicación, cubriendo el caso A→B→A incluso si B ya inició la navegación y espera la raíz de otra unidad. Las esperas de Git posteriores al cambio de ruta no cuentan como cambio pendiente. Los contratos de respuestas retrasadas y la repetición gráfica de 30 ráfagas del paquete definitivo verifican estos casos en los fixtures; no sustituyen pruebas de todas las unidades y proveedores.

La cola serializa generaciones del mismo objeto dentro de una página. Las columnas Git forman parte de la versión de la solicitud: una nueva columna puede solicitar trabajo, mientras que una respuesta Git ausente o inaccesible no desencadena reintentos continuos.

## Cuello de botella confirmado al volver a perfilar

Una captura EventPipe limitada al PID experimental de la versión preliminar 4.2.39.1 reveló trabajo adicional después de ListingCompleted. En el intervalo 13:26:38,494591–13:26:40,850015Z, el hilo UI tenía 1.190 muestras; 1.056 terminaban en Directory.Exists (88,7 %). La cadena era HasExtension → clasificación audio/vídeo/imagen → AdaptiveLayoutHelpers.GetContentLayout → navegación. Los clasificadores recibían una extensión como `.txt` y la trataban como ruta relativa, repitiendo consultas al sistema de archivos para 100.000 entradas. Tres pausas de GC sumaron 5,66 ms, con máximo 2,05 ms: no explican ese intervalo de unos 2,3 segundos.

Se sustituyeron únicamente esos clasificadores privados por un FrozenSet de extensiones con comparación OrdinalIgnoreCase. Se conservan las listas de tipos, el umbral estricto >60 %, el comportamiento de extensiones almacenadas, el predictor y las preferencias. Diecisiete contratos enlazan la clase de producción completa. El marcador AdaptiveLayoutCompleted separa ahora este trabajo de ListingCompleted. La validación gráfica final determina el efecto; el recorrido de 100k en el contrato no es una latencia de GUI.

También se eliminó Directory.Exists al identificar `.lnk`/`.url` en GetFile: WIN32_FIND_DATA ya establecía que la entrada era un archivo. Se conserva el parseo de enlaces y su rama de symlink. Name evita calcular un nombre sin extensión cuando las extensiones están visibles. El clasificador de comprimidos reutiliza su lista y evita un array/predicado por llamada, conservando incluso la prioridad y los substrings de la ruta. Dieciséis contratos y las asignaciones medidas están en PHASE3_ENTRY_CONSTRUCTION.md.

Los benchmarks históricos de enumeración empleaban ListedItem y formatos simplificados y otro comparador. No medían estas consultas, el constructor completo ni toda la ordenación natural real. Por eso no se extrapola la cifra interna de 159 ms al tiempo de apertura.

La captura posterior 04 identificó otro trabajo de disco en el hilo UI: CheckForSolutionFile copiaba la lista y ejecutaba PLINQ con HasExtension sobre extensiones. Las pilas muestran espera sincronizada de PLINQ y Directory.Exists. Se sustituye por un recorrido protegido de la colección y comparaciones OrdinalIgnoreCase de `.sln`/`.slnx`, con el primer resultado en el orden actual. Se conservan las extensiones almacenadas y se eliminan las consultas al directorio de trabajo, la copia y la espera paralela. Su detección se comprueba con el método real extraído; el efecto gráfico se evalúa en la entrega final.

## Cómo se mide

La automatización actúa exclusivamente sobre la identidad experimental registrada dentro del workspace, su PID y ventana. Navega sólo a carpetas sintéticas. La referencia original está intacta; Explorer no se controla ni se modifica. El estado registrado anterior se conserva en `artifacts/phase3-recovery/registration-before.json`.

UIA mide desde `CurrentPathSet.ValuePattern.SetValue` hasta que el proveedor expone un nombre con área positiva dentro de la ventana. Esto incluye la observación, no es un clic físico hasta píxeles. El conteo final de la barra de estado confirma el listado expuesto por el proveedor; no confirma que hayan terminado el layout ni todas las miniaturas. La selección mediante `SelectionItemPattern` es una comprobación de interacción, distinta de latencia de un gesto real.

El diagnóstico optativo registra solicitud, enumeración, primera entrada, publicación, contenedores y un callback XAML con texto real cruzando el viewport. Registra duración de render XAML durante scroll, separación de callbacks, espera del dispatcher, trabajos de fila, memoria administrada y contadores de GC. No certifica presentación física, FPS ni pausas individuales de GC. Sus logs omiten nombres/rutas/consultas y están limitados a fixtures, 256 operaciones y 100.000 eventos/32 MiB. La comparación principal debe ejecutarse con diagnóstico desactivado; la pasada instrumentada se informa aparte.

Se detectó y corrigió una contaminación importante del observador: buscar la barra de estado recorriendo todos los descendientes UIA después de cargar 100.000 filas creaba trabajo de accesibilidad y aumentaba memoria y tiempo. Ahora los controles de ruta y conteo se capturan en la carpeta vacía, se reutilizan y se deja de consultar nombres tras la primera observación. Los resultados exploratorios de varios segundos se conservan, pero no se presentan como tiempo real de usuario.

La caché es caliente/no controlada. No se vació la caché de Windows. Los percentiles de interfaz usan interpolación lineal; pocos intentos describen la muestra y no acreditan una cola de latencia a largo plazo. Los bancos internos mantienen su propia definición de percentil.

## Búsqueda sobre NTFS, separada de WinUI

El banco emparejado conserva Original → Phase II → Phase III → Phase III → Phase II → Original, cuatro tamaños y seis consultas. Son 432 muestras medidas y 144 calentamientos. Todos los hashes de resultados coinciden. En 100.000 entradas, publicación del callback del harness:

| Consulta | Original p50 ms | Phase II p50 ms | Phase III p50 ms | Phase II p95 ms | Phase III p95 ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| Exacta | 302,26 | 280,88 | 165,59 | 308,53 | 216,79 |
| Parcial | 225,58 | 218,30 | 101,51 | 231,26 | 113,25 |
| Sin resultados | 293,34 | 264,13 | 155,29 | 293,28 | 196,38 |
| `.txt`, diez sugerencias | 218,38 | 228,11 | 94,72 | 232,83 | 96,36 |
| Unicode `東京` | 223,75 | 225,32 | 183,06 | 233,41 | 194,97 |
| Wildcard | 218,19 | 223,72 | 98,45 | 229,10 | 126,74 |

Estas cifras incluyen debounce y recorrido real de archivos, pero excluyen WinRT, iconos, dispatcher y renderizado. La mejora proviene de adelantar la consulta; NativeNameSearch es el mismo en Phase II y III. No son resultados visibles de la GUI. Datos y dispersión en `artifacts/phase3-search-verified/pipeline/`; explicación en `reports/PHASE3_SEARCH.md`.

## Pruebas automatizadas

- 22/22 contratos de navegación enlazan métodos extraídos literalmente de ShellViewModel y enumerador Universal, con dispatcher/almacenamiento controlados. Incluyen navegación/cancelación repetida, ordenación tardía, búsqueda que no espera iconos, fuentes liberadas, reintentos, vida de tokens de viewport y finalización de la generación anterior. Evidencia: `reports/phase3-navigation-shell-contracts-final-04.json`, checksum en `tests-extra/Phase3Navigation/source-hash.txt`.
- 17/17 contratos enlazan la cola de producción completa, con dispatcher y SynchronizationContext controlados. Capacidad, prioridad, reciclaje, generaciones, cancelación, errores, versiones Git, liberación y 100 ráfagas de 1.000 solicitudes. Evidencia: `reports/phase3-viewport-contracts-final.json`.
- La primera ejecución de la cola dio 15/17: el harness no modelaba el SynchronizationContext UI y comprobaba el drenaje antes de terminar continuaciones en thread pool. Se corrigió el harness; la fuente de producción mantuvo exactamente el mismo SHA256. Se preserva el primer fallo en `reports/phase3-viewport-first-failure` y `reports/phase3-viewport-contracts.json`.
- 11/11 contratos de sugerencias enlazan SearchSuggestionPipeline real; 28/28 del proveedor nativo y 3/3 de recorrido recursivo, incluida junction confinada al fixture. Evidencia: `artifacts/phase3-search-verified/`.

- 17/17 contratos enlazan AdaptiveLayoutHelpers completo y comprueban todos los tipos multimedia, casing, porcentajes, preferencias, INI, archivos/carpetas/enlaces y el artefacto de directorio relativo. Evidencia: `reports/phase3-adaptive-contracts-final.json`.
- 16/16 contratos verifican los miembros reales de construcción/clasificación, equivalencia de nombres y detección de soluciones, incluida ausencia de consultas al directorio de trabajo. Evidencia: `artifacts/phase3-entry-construction-final-02/entry-contracts.json`.

37/37 contratos de toolbar y 41/41 de directorio/icono/breadcrumbs enlazan métodos reales extraídos y el coordinador completo. Incluyen respuestas antiguas, A→B→A, cambio de owner, raíz pendiente, reentradas y disposición. Evidencia: `reports/phase3-path-navigation-contracts-final-02.json` y `reports/phase3-working-directory-contracts-final-01.json`; detalles en PHASE3_PATH_NAVIGATION.md.

Total: 192 contratos correctos. Los contratos no ejecutan WinUI ni prueban comportamiento nativo de todas las miniaturas. La compilación de navegación termina con diez advertencias del harness y cero errores; la cola y el banco de sugerencias con cero advertencias y cero errores.

## Funciones que requieren validación adicional

Operaciones destructivas, drag/drop, menús shell/servidor COM, archivos protegidos, unidades externas/red, cloud, bibliotecas solapadas, etiquetas/AQS con índice, códecs, rutas largas y carpetas NTFS sensibles a mayúsculas necesitan pruebas específicas. Las modificaciones conservan esos caminos, pero eso no acredita compatibilidad universal. Todos los ensayos con escritura deben utilizar copias desechables dentro del workspace.

## Comparación manual reproducible con Explorer

Usar detalles, orden por nombre ascendente, misma carpeta sintética, tamaño de ventana, escala y monitor. Abrir una carpeta vacía y después 1k/10k/50k/100k; cinco repeticiones por tamaño y orden alternado. Registrar primera fila, conteo final y primera selección por separado. No cambiar Explorer ni asociaciones. Una cámara externa de velocidad conocida permite observar clic hasta píxeles y declarar incertidumbre de un fotograma. No se afirma que Phase III haya superado a Explorer sin esa comparación.

## Documentación consultada

[Optimización oficial de ListView/GridView](https://learn.microsoft.com/en-us/windows/apps/develop/performance/optimize-gridview-and-listview), [paneles virtualizantes](https://learn.microsoft.com/en-us/windows/uwp/controls-and-patterns/listview-and-gridview), [GC inducido](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/induced), [EmptyWorkingSet](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-emptyworkingset). El procedimiento de medición y las fuentes de XAML están en `reports/PHASE3_MEASUREMENT.md`; arquitectura del scroll en `reports/PHASE3_SCROLL.md`.

## Resultado integrado y entrega

La entrega definitiva es **4.2.39.4 x64 Managed Release**, compilación 06, con diagnóstico desactivado en la comparación principal. El PID 36832 tenía exactamente una instancia cuyo ejecutable correspondía al layout registrado al comienzo y final de las cuatro sesiones. La referencia II es la ronda conservada del mismo observador; su metadata anterior no registraba el número de instancias. Se comparan cinco repeticiones por tamaño, misma ventana de 1440×753 y fixtures NTFS sintéticos de archivos reales, caché caliente/no controlada. La ronda definitiva III se ejecutó posteriormente; no constituye un ensayo aleatorio concurrente.

| Entradas | Primera fila accesible p50 II→III ms | Conteo completo p50 II→III ms | Conteo completo p95 II→III ms |
| --- | ---: | ---: | ---: |
| 1.000 | 165,93→163,06 | 166,14→163,36 | 216,60→207,86 |
| 10.000 | 153,53→203,45 | 365,32→244,28 | 369,10→358,13 |
| 50.000 | 168,81→170,32 | 1.446,64→609,18 | 1.694,09→739,64 |
| 100.000 | 154,36→185,33 | 3.015,21→1.129,94 | 3.192,85→1.229,85 |

El conteo completo mejora un 33,1 %, 57,9 % y 62,5 % en 10k, 50k y 100k respectivamente. La primera fila **no mejora de forma general**: 10k empeora 49,92 ms y 100k 30,97 ms. En 100k, el p95 de primera fila cambia de 171,93 a 197,74 ms; la desviación estándar es 12,00→11,94 ms y el IQR 12,91→11,35 ms. En conteo completo 100k, desviación 131,31→49,51 ms e IQR 76,72→24,91 ms. Son estadísticas de cinco observaciones; no acreditan una cola de latencia estable. Los valores completos de primera fila, conteo, llamadas del observador y transiciones están en `benchmarks/results/phase3/ui-final06-single-off-summary/summary.json` y PHASE3_MEASUREMENT.md.

Se verificaron cinco selecciones y veinte movimientos de scroll de la ronda OFF. La confirmación UIA de selección tiene p50 24,33→19,59 ms. El scroll no muestra una mejora uniforme: el incremento pequeño observado cambia de 3,34 a 16,36 ms (p95 17,23→34,64), el grande de 3,32 a 2,83 ms y el salto al inicio de 98,39 a 74,04 ms. Estos tiempos incluyen consultas del proveedor de accesibilidad, no son frame times ni gestos físicos.

Las treinta ráfagas definitivas tienen 210 envíos individuales registrados. Todas terminaron en 1k con conteo correcto y permanecieron allí tras un segundo de reposo. Evidencia primaria: `ui-PhaseIII-20261011-143626-9f809a92` (navegación), `143702-00ccada5` (selección), `143703-9d6681df` (scroll) y `143708-ba6153b1` (ráfagas), bajo `benchmarks/results/phase3/` con prefijo `ui-PhaseIII-20261011-` para cada sufijo. La prueba acredita ese estado final, no que se haya representado cada una de las ubicaciones intermedias.

## Diagnóstico del paquete final

La captura final se ejecutó en el PID 28748, creado tras habilitar la configuración, con SessionStarted y ancla UTC confirmados antes de medir. Las ventanas `manual-20261011-144717-025b2d24` (120,56 s) y `manual-20261011-144943-7c81486f` (75,38 s) no se solapan. Ambas tienen JSONL requerido y exactamente una instancia experimental al comienzo/final. El último JSONL contiene 12.900 registros, 85 operaciones solicitadas y cero registros descartados. Se informa aparte de la comparación OFF; no existe una traza interna equivalente de II.

Las ocho navegaciones ordinarias se correlacionan sin ambigüedad con sus setters UTC. En las dos de 100k, envío→Requested tarda 105,02/84,70 ms; Requested→primera entrada nativa 9,98/12,15 ms; Requested→primera publicación de 64 entradas 30,56/28,62 ms; Requested→Rendered visible 106,23/111,59 ms. Publicación→Rendered es 75,67/82,97 ms y envío→Rendered 211,25/196,29 ms. El primer callback XAML dura 4,10/3,91 ms. En las ocho navegaciones no empezó trabajo de cola de la operación antes de su primer callback visible; en las dos de 100k se comprobó también que no empezó ningún trabajo de otras operaciones durante ese tramo. La hipótesis de bloquear metadatos de esa cola para mejorar ese tramo no queda apoyada por esta captura. No se atribuye la regresión frente a II a una causa concreta sin perfilar II del mismo modo.

La ventana de navegación 100k se recorta antes del primer scroll, excluyendo 36 callbacks solapados. Sus 137 callbacks XAML tienen duración p50/p95/máximo 0,68/38,33/112,07 ms; 18 superan 6,94 ms, el presupuesto correspondiente al modo de monitor de 144 Hz informado por Windows. En el proceso completo, 289 callbacks de scroll dan 0,63/59,35/101,34 ms y 94 superan ese presupuesto. Los 284 intervalos entre callbacks son otra métrica: 11,66/110,37/188,99 ms. No equivalen a frames presentados físicamente ni demuestran 144 FPS; faltan frames/gestos físicos comparables de II.

Los siete callbacks de publicación 100k duran p50/p95/máximo 10,46/15,76/16,48 ms, con espera de cola 7,40/21,61/22,59 ms. Las 1.281 muestras de espera del dispatcher asignadas a la operación actual incluyen scroll y reposo posteriores: 0,39/6,24/328,88 ms; no se presentan como una ventana pura de navegación. En todo el proceso se registran 3.049 inicios de trabajos, 2.967 completados y 82 cancelados. El máximo observado de activos es cuatro y de pendientes 87, dentro del límite 128. Son eventos de trabajo, no un recuento único de contenedores vivos.

Las dos selecciones parciales ocurrieron con 64 entradas expuestas: se confirmaron en 477,66/145,78 ms y conservaron la misma fila seleccionada tras el conteo completo. Las veinte ráfagas instrumentadas terminaron correctamente en 1k y siguieron allí tras reposo, con 140 setters registrados. El scroll instrumentado conservó dos intentos incompletos: en el reintento hay 19 movimientos confirmados; al preparar el quinto salto al inicio el extremo opuesto informó 99,9952438479 % y no satisfizo el requisito exacto de 100 % en dos segundos. Ese resultado no acredita un bloqueo físico de dos segundos. No se relajó retrospectivamente la condición ni se convirtió el intento en 20/20. Las veinte comprobaciones OFF anteriores siguen siendo la comparación primaria.

La primera ventana acumula 62,94 s de CPU de proceso y private bytes de 231,53→400,18 MB, pico 412,19 MB; la segunda 63,55 s y 399,87→337,33 MB, pico 422,89 MB (MB decimales). La CPU es tiempo sumado entre núcleos, no porcentaje de un núcleo ni coste exclusivo del escenario. El heap administrado final/peak es 26,15/111,25 MB. En el intervalo final muestreado de 9,92 s, CPU 3,328 s, private bytes constantes 337.334.272 y handles constantes 2.878. **No demuestra CPU baja en reposo ni ausencia de fugas**: aún existe trabajo/timers o coste diagnóstico por investigar. No se forzó un GC para fabricar un dato de memoria menor. Los contadores de GC no miden pausas; las capturas EventPipe03/04 siguen siendo evidencia exclusiva de sus versiones anteriores.

Evidencia segmentada: `benchmarks/results/phase3/ui-final06-verified-segmented-profile/profile.json`; correlación temporal en `ui-final06-verified-correlation/`. PHASE3_MEASUREMENT.md conserva ventanas, dispersión, fallos del observador y procedimientos. Tras la medición se deshabilitó el diagnóstico, se cerró ese proceso y se dejó una sola instancia nueva 4.2.39.4 en 1k, registrada en `reports/phase3-final-runtime-state.json`.

## Controles descartados y regresiones

La primera ronda 06 tenía una segunda instancia experimental abierta por un error al comprobar el arranque. Se conserva en `ui-final06-paired-off-summary` como control excluido de la comparación principal. Sus ráfagas siguen siendo evidencia funcional del PID medido. La captura del PID 8352 carece de JSONL: se conservan sólo sus observaciones UIA y de proceso; no aporta mediciones XAML. El proceso diagnóstico final se creó después de habilitar la configuración y se exigió un registro SessionStarted con ancla UTC antes de medir.

Las rondas exploratorias cuyo observador recorría todos los descendientes y la primera lectura de scroll sin esperar actualización del porcentaje se excluyen de las conclusiones de velocidad. En EventPipe04, la ventana inicialmente elegida estaba fuera del trace; se conservó y se corrigió la correlación con la selección que sí se solapa. No se suman intervalos de CPU superpuestos. Estos controles y el fallo A→B→A de 4.2.39.2 permanecen disponibles para auditoría.

Se descartó reemplazar las listas virtualizantes y cargar metadatos/Git de todas las entradas. No se añadió una barrera de primer render especulativa: phase 3 de contenedor y prioridad baja del dispatcher no garantizan haber presentado un frame; la captura definitiva comprueba por separado cuándo empiezan los trabajos de fila. Tampoco se alteró la semántica de entradas programáticas de AutoSuggestBox para conseguir una cifra de búsqueda: UIA SetValue emite Reason=None y los mensajes locales de teclado ensayados no generaron una entrada válida. El resultado visible al escribir queda **sin medir**; PHASE3_MEASUREMENT.md contiene el procedimiento manual con entrada real. No se equiparan los callbacks del harness de búsqueda a ese resultado.

## Release, ejecución y recuperación

El build integrado `reports/phase3-managed-release-06-final-build.log` termina con cero errores y 104 advertencias en 1:07,74. El layout completo está en `artifacts/development-layout/managed-release-phase3-stable06/layout`; junto a él queda `FilesTurbo.Experimental_4.2.39.4_x64_Development_unsigned.msix`. El MSIX es de desarrollo sin firma; la prueba usa registro de layout y no acredita un instalador público. Su inventario conserva app, servidor, CsWin32, dependencias y archivos históricos. El registro de validación de contenido se creó antes de las pruebas GUI y se conserva intacto; las pruebas posteriores tienen su propia evidencia.

Desde la raíz de este workspace, PowerShell 7.2 o posterior:

```powershell
./tooling/Register-Phase3Experimental.ps1
Start-Process ('files-turbo:?folder=' + [Uri]::EscapeDataString((Join-Path $PWD 'benchmarks/synthetic/1000'))) -WindowStyle Hidden
```

Ejecutar el lanzamiento una sola vez; el proceso se llama Files y su identidad experimental está en la ruta verificada. Para recuperar II:

```powershell
./tooling/Register-Phase3Experimental.ps1 -RestorePhase2
Start-Process ('files-turbo:?folder=' + [Uri]::EscapeDataString((Join-Path $PWD 'benchmarks/synthetic/1000'))) -WindowStyle Hidden
```

El script limita el registro a la identidad propia dentro del workspace, verifica el hash del manifest de II y conserva un registro nuevo por operación. La comprobación completa del inventario del layout corresponde a la verificación final. La recuperación a II y su relanzamiento se probaron durante la comparación; el layout II y la referencia `files-turbo-phase3-start` permanecen intactos. Para regresar a III se usa el primer bloque. El diagnóstico queda desactivado para el uso normal.

El código y la copia de scripts/contratos/informes pequeños se conservan en la rama `codex/files-turbo-phase3` y la etiqueta `files-turbo-phase3`. `source/docs/performance/phase3/workspace-snapshot` permite restaurar esos archivos sin sobrescribir otros distintos y preserva los snapshots anteriores. Las dependencias grandes y los fixtures requieren los pasos de reproducción documentados. La procedencia del Release y la verificación final quedan en `reports/phase3-build-provenance.json` y `reports/phase3-final-verification.json`, generadas contra el commit limpio definitivo.

## Qué optimizar después

La mejora demostrada es completar antes carpetas grandes y eliminar dos grupos de sondeos de disco y una carrera de navegación. La primera fila y el incremento pequeño de scroll siguen teniendo regresiones en este ensayo. La captura definitiva separa validación previa, primer lote y callback XAML visible, pero carece de una traza interna equivalente de II. El siguiente paso debe comparar esos tramos bajo las mismas condiciones y perfilar el intervalo de validación de ruta y publicación→render. No se añade una barrera de metadatos/Git cuando los ocho registros actuales no muestran trabajos de cola previos al primer callback visible. También faltan búsqueda visible con entrada real, frames presentados físicamente, comparación justa con Explorer y los escenarios de compatibilidad enumerados arriba. Se conserva esta entrega por la reducción consistente del conteo completo de carpetas grandes y la corrección de navegación, con esas regresiones explícitas. La entrega es una mejora parcial verificada; no promete scroll perfecto, búsqueda instantánea ni superioridad universal.
