# Phase III: medición de la interfaz real

11 de octubre de 2026. Los resultados históricos de Phase II se mantienen intactos. La identidad medida es exclusivamente `FilesTurbo.Experimental`, instalada dentro del workspace. La automatización valida identidad, ubicación, PID y ventana antes de acceder a sus controles; solamente navega a fixtures de `benchmarks`.

## Qué significa cada medida

| Medida | Evidencia | Límite |
| --- | --- | --- |
| Requested / EnumerationStarted / FirstItemAvailable | Stopwatch en el pipeline real | Todavía no implica datos publicados ni píxeles |
| DataPublished / PublicationQueueDelay | Actualización real en el dispatcher y su espera | Colección publicada, todavía sin demostrar layout |
| ContainerRealized / RealizedContainerCallbacks | Primer callback phase0 no reciclado y conteo acumulado desde el handler real del layout | Son callbacks observados por operación, no conteo de contenedores únicos vivos; pueden preceder al binding |
| RenderCallbackWithVisibleContainer | `CompositionTarget.Rendered`, mismo operation ID, publicación no vacía previa, ItemName real coincide con el modelo y cruza el viewport | Observación del árbol XAML en el callback; no certifica presentación física ni ausencia de oclusión |
| PointerInputDelivered / KeyInputDelivered | Primer callback de entrada real posterior a la primera fila comprobada | Momento de interacción observada; no tiempo mínimo en que hubiera sido posible interactuar |
| UIA_FirstViewportName | Proveedor UIA expone ItemName con área positiva en la ventana, tras verificar la ruta | Incluye coste de observación; no es clic físico hasta píxeles |
| UIA StatusFullCountMs | Proveedor de la barra de estado muestra el conteo final esperado | No demuestra finalización del layout ni de metadatos/miniaturas |
| ScrollXamlFrameDuration | `RenderedEventArgs.FrameDuration` durante un episodio de scroll | Duración de render de XAML, no present-to-present ni FPS físicos |
| NavigationXamlFrameDuration | Duración del render XAML tras primera publicación no vacía, hasta 2s después de ListingCompleted más un callback tardío final | Captura diagnóstico acotada; no presentación física ni FPS |
| ScrollRenderCallbackGap | Separación entre callbacks durante scroll | Se informa aparte; nunca se convierte automáticamente en FPS |
| UIA_ScrollPatternCall | Duración de la llamada y porcentaje accesible posterior | No mide gesto, animación completa ni latencia física |
| DispatcherQueueDelay | Una sonda de prioridad baja, con una sola petición pendiente | Espera en cola; no duración exacta de cada tarea UI |
| GCCollectionCount / ManagedHeapBytes / AllocatedBytesTotal | Contadores públicos de .NET sin GC forzado | No identifica pausas individuales de GC ni demuestra fuga |

El registro SessionStarted definitivo incluye UtcTicks y un SessionMs correspondiente al punto medio de dos lecturas Stopwatch que rodean la lectura UTC. DurationMs conserva el intervalo de muestreo del ancla; el reloj UTC del sistema y sus posibles ajustes siguen teniendo incertidumbre. Las latencias se calculan siempre con Stopwatch. El resumen exporta RequestedUtc sólo si existe ese ancla; no utiliza la creación del archivo para inventar un origen exacto.

El probe se conecta sólo con diagnóstico opt-in, se desuscribe al descargar el layout y dispone de su timer. La escritura JSONL se realiza fuera del hilo UI; la cola admite 2.048 eventos, conserva como máximo 256 operaciones, corta a 100.000 eventos y comprueba el presupuesto de 32 MiB después de vaciar y hacer flush de cada ráfaga; una última ráfaga puede exceder ese presupuesto. Contabiliza los descartes. No serializa nombres, rutas ni consultas. Los callbacks añaden trabajo al modo diagnóstico: una comparación principal de latencia debe ejecutarse también con el diagnóstico desactivado.

## Automatización portable

```powershell
# La aplicación experimental debe estar abierta; el script no registra paquetes.
./tooling/Measure-Phase3UiSession.ps1 -Action Navigation -Variant PhaseIII -ProcessId <pid> -Repetitions 5 -RestoreWindow
./tooling/Measure-Phase3UiSession.ps1 -Action Scroll -Variant PhaseIII -ProcessId <pid> -Scenario 100000 -Repetitions 5
./tooling/Measure-Phase3UiSession.ps1 -Action Selection -Variant PhaseIII -ProcessId <pid> -Scenario 100000 -Repetitions 5
./tooling/Measure-Phase3UiSession.ps1 -Action SelectionDuringLoad -Variant PhaseIII -ProcessId <pid> -Scenario 100000 -Repetitions 2
./tooling/Measure-Phase3UiSession.ps1 -Action RapidNavigation -Variant PhaseIII -ProcessId <pid> -Repetitions 5
./tooling/Summarize-Phase3Measurements.ps1 -SessionDirectory <directorio-PhaseII>,<directorio-PhaseIII>
```

Navegación utiliza `CurrentPathSet.ValuePattern`, sin enviar teclas globales. Entre muestras espera a que una carpeta vacía controlada muestre cero nombres. Los elementos de ruta y conteo se obtienen mientras la carpeta está vacía y se reutilizan durante la medición. Encontrar la barra de estado mediante `FindFirst(Descendants)` después de poblar una lista enorme puede recorrer o realizar peers de accesibilidad; por eso se evita y se conserva el máximo coste de cada consulta. La versión definitiva cachea también los ValuePattern de setter/getter en la transición vacía. `PatternQueryMs` conserva el coste de adquisición inicial (o acceso al patrón cacheado), y Stopwatch se inicia antes del descubrimiento del setter. En las primeras series, la consulta GetCurrentPattern del setter ocurría antes de Stopwatch; un intervalo vacío exterior de 2,36 s con sólo 86,69 ms interiores identificó esa frontera excluida. Las series anteriores conservan esa definición y no se combinan con las rondas pareadas del script definitivo. La muestra espera al conteo final esperado antes de navegar otra vez. El observador sigue teniendo coste: sus máximos permiten interpretarlo, no descontarlo matemáticamente del tiempo observado.

La lista de detalles declara `AccessibilityView=Raw`; para `ScrollPattern` se asciende por `RawViewWalker`, exclusivamente dentro del PID validado. El script puede restaurar sólo esa ventana con `-RestoreWindow`; rechaza una minimización durante la medida. El script definitivo confirma el endpoint opuesto antes de cada salto, y después de la llamada espera hasta 2 s al porcentaje esperado (sondeo 5 ms). La llamada y la confirmación accesible se conservan por separado con Moved/TimedOut y marcas UTC. Una lectura inmediata puede seguir exponiendo el porcentaje anterior; `ui-PhaseIII-20261011-134410-3cf49bb6` conserva ese control. La repetición corregida `ui-PhaseIII-20261011-134548-9245e258` confirmó 20/20 movimientos. Los renders del modo instrumentado también pueden incluir la preparación no cronometrada, lo que impide atribuir todos los frames sólo a la llamada medida.

Los proveedores WinUI pueden declarar `IsOffscreen=false` en ventanas minimizadas y exponer coordenadas negativas: no se aceptan como primera fila visible. No se inspeccionan otras ventanas ni se usan teclas globales.

Los resultados CSV conservan las muestras y `session.json` describe identidad, método, cultura numérica y límites. La validación `summary-validation-input/output` confirmó cultura es-ES (12,5/17,5→p50=15), separación de una publicación vacía 5 ms y primera no vacía 20 ms, y correlación UTC exacta de un ancla conocida. El resumen calcula p50/p95/p99 por interpolación lineal, mínimo/máximo, desviación e IQR. Con pocas muestras, p95 y p99 describen esa muestra; no establecen el comportamiento de cola a largo plazo. La caché es caliente/no controlada; no se vacía la caché global de Windows.

`Selection` quita la selección de la primera fila realizada, la selecciona mediante `SelectionItemPattern.Select` y confirma `IsSelected`. Demuestra una acción accesible dentro de la ventana; no se presenta como latencia de clic físico. El baseline confirmó cinco selecciones en `ui-PhaseII-20261011-125805-3005c269`.

`SelectionDuringLoad` empieza desde la carpeta vacía con patrones/conteo cacheados. Después de la primera fila accesible de 100k, solicita Select y confirma IsSelected, conservando tiempo desde navegación y conteo accesible anterior/posterior. Sólo marca ConfirmedBeforeFullCount cuando ambos conteos son positivos y menores que 100k. Espera después al conteo final y conserva timeouts. Demuestra interacción mediante proveedor sobre una colección parcialmente publicada, sin afirmar latencia de clic físico; sus métricas se resumen aparte.

`RapidNavigation` envía siete ubicaciones sintéticas por ciclo (100k/10k/50k/vacía/100k/vacía/1k), con 20 ms entre llamadas. Comprueba ruta/conteo final de 1k y, desde la revisión del script posterior al fallo de 4.2.39.2, vuelve a comprobarlos después de un segundo de reposo. Esa revisión conserva requests.csv con etiqueta numérica del fixture, orden y UTC de cada setter; la exportación se hace después de la ráfaga para no espaciarla con escritura CSV. También conserva session.json con estado Incomplete en un fallo. Las series anteriores no tienen marcas por setter y no se inventan retrospectivamente. Observa RAM/CPU después del reposo. Las llamadas UIA tienen su propio coste; no se interpretan 20 ms como ritmo efectivo garantizado. Esto comprueba el estado final accesible de la GUI, sin afirmar que todas las tareas nativas hayan terminado o que no exista una fuga.

## Sesión manual instrumentada

```powershell
./tooling/Measure-Phase3ManualSession.ps1 -Action Enable
# Cerrar y volver a abrir Files Turbo experimental para leer la configuración.
./tooling/Measure-Phase3ManualSession.ps1 -Action Capture -Seconds 60 -ProcessId <pid>
./tooling/Summarize-Phase3Measurements.ps1 -SessionDirectory <directorio-capturado>
./tooling/Measure-Phase3ManualSession.ps1 -Action Disable
# La desactivación se aplica al siguiente proceso; los logs se conservan.
```

`Enable` escribe sólo `LocalState/phase3-performance.json` de la identidad experimental; los registros usan `LocalState/Phase3Performance`. `Capture` toma CPU, working set, bytes privados y handles del proceso verificado y copia su último JSONL al workspace. No fuerza GC, cambia prioridades ni modifica ajustes del sistema. Para memoria, registrar 20 ciclos de navegación y 10 segundos de reposo; comparar bytes privados iniciales/finales y el heap administrado sin afirmar que toda diferencia es una fuga.

Pruebas manuales: carpeta 1k/10k/50k/100k; scroll lento y rápido; saltos a inicio/final; selección de filas mientras enumera; búsquedas exacta/parcial/Unicode/wildcard/sin resultados/muchas coincidencias; reemplazo rápido de consultas; cambio de carpeta durante búsqueda; ida/vuelta de pestañas y cierre/reapertura. Todas las operaciones con escritura se limitan a fixtures desechables del workspace.

## Estado de las primeras observaciones

Las sesiones `ui-PhaseII-20261011-123609-4d75b2c0` y `ui-PhaseII-20261011-124450-44c10ad2` se conservan como exploración. La primera utilizaba `FindAll(ItemName)`. La segunda buscaba repetidamente la barra de estado tras publicar miles de filas: sus tiempos de conteo de varios segundos están contaminados por trabajo del observador y no acreditan esa espera de usuario. Las sesiones interrumpidas o con ventana minimizada tampoco constituyen comparación válida. `baseline-ui-summary` resume esa exploración, no una aceleración de Phase III.

El scroll `ui-PhaseII-20261011-124643-435de771` comprobó 20 llamadas a ScrollPattern y porcentajes posteriores; no midió fotogramas. Las series comparativas definitivas y los resultados del build/paquete se documentan en el informe general de Phase III.

**Baseline corregido:** `ui-PhaseII-20261011-125519-5c9093c6`, dos repeticiones por cada tamaño, resumen `baseline-ui-final-summary`. Obtiene ruta/conteo en la carpeta vacía; deja de consultar ItemName después de observar la primera fila; conserva coste máximo de consulta y bounds positivos `26;26;1440;753`. En 100k, las dos primeras observaciones UIA fueron 197,24/187,51 ms; el conteo accesible final fue 2.757,08/3.320,57 ms. Los máximos de lectura de conteo fueron 0,72/57,04 ms y los de nombres 66,88/61,24 ms. Se conservan las muestras, sin descontar costes de observación ni interpretar el conteo como finalización física. El tamaño de muestra es insuficiente para una garantía de p95. La serie anterior con getters cacheados pero consultas repetidas de ItemName (`ui-PhaseII-20261011-125328-f0d9230e`) se conserva como control del observador.

**Búsqueda por UIA:** el botón de modo permite `InvokePattern` y la caja permite `ValuePattern.SetValue`, pero el control Omnibar original sólo clasifica como UserInput los cambios precedidos por KeyDown. El setter UIA genera un cambio con motivo None, que el toolbar ignora. Mostrar ese texto no significa que se haya ejecutado una búsqueda. Las pruebas acotadas con `SendMessage` a un HWND de bridge WinUI perteneciente al mismo PID tampoco produjeron entrada XAML; no se usaron teclas globales. La búsqueda se mide mediante sesión manual instrumentada, salvo que una corrección legítima de accesibilidad se valide por separado. No se publica un benchmark de escritura simulado como búsqueda visible. El control tampoco ofrece un botón Go/Submit: sólo Enter y ItemClick real de una sugerencia llaman a SubmitQuery. `CurrentPathSet` no parsea search-ms como búsqueda interna; una URI desconocida puede llegar al Launcher del sistema, por lo que no se utiliza esa vía que abriría un handler externo.

El probe de pantalla usa las declaraciones aprobadas de CsWin32 (`MonitorFromWindow`, `GetMonitorInfo`, `EnumDisplaySettings`) para la ventana experimental. `MonitorReportedRefreshRateHz` identifica la frecuencia actual comunicada por Windows para ese monitor; no es una medición óptica ni una frecuencia dinámica garantizada. Mantener el monitor fijo durante una serie. Puede pasarse ese valor al resumen con `-RefreshRateHz` para contar renders XAML que exceden `1000 / Hz`; el resultado no es una tasa de FPS física.

## Comparación preliminar 4.2.39.1 con diagnóstico desactivado

Phase II: `ui-PhaseII-20261011-125519-5c9093c6` (4.2.39.0). Phase III: `ui-PhaseIII-20261011-131256-d66ef1ad` (4.2.39.1, layout `managed-release-phase3-final`). Ambas series usaron el mismo script, dos repeticiones por tamaño, caché caliente/no controlada y ventana `26;26;1440;753`; ninguna terminó por timeout. Resumen reproducible: `ui-primary-comparison-summary`.

| Entradas | Primera fila accesible II p50 ms | III p50 ms | Conteo final II p50 ms | III p50 ms |
| --- | ---: | ---: | ---: | ---: |
| 1.000 | 192,91 | 161,89 | 201,53 | 175,11 |
| 10.000 | 169,59 | 165,25 | 364,54 | 410,74 |
| 50.000 | 175,62 | 179,88 | 1.495,92 | 1.307,10 |
| 100.000 | 192,38 | 152,40 | 3.038,83 | 2.423,65 |

En 100k, la mediana observada mejora un 20,8% para la primera fila accesible y un 20,2% para el conteo final. La serie de 10k mostró un conteo final un 12,7% más lento; en 50k la primera fila fue un 2,4% más lenta. Con n=2, estas diferencias no establecen causalidad ni una garantía de p95; no se ocultan las regresiones de la muestra. Los máximos de lectura de ruta de III alcanzaron 146,58 ms, de nombres 81,25 ms y del conteo 57,47 ms: el observador puede esperar al dispatcher. No se restan estos tiempos de la latencia total.

Selección III: `ui-PhaseIII-20261011-131339-ffa9d18b`, cinco selecciones confirmadas; p50 de llamada 19,34 ms y de confirmación 21,71 ms, frente a 25,09/27,40 ms de II. Scroll III: `ui-PhaseIII-20261011-131344-064d7e37`, cinco llamadas de cada modo (incremento pequeño/grande, final/inicio), veinte confirmaciones de proveedor en total. Los resúmenes conservan cada modo separado y sus outliers; estas medidas no representan la duración completa de una animación ni fotogramas presentados.

## Perfil preliminar 4.2.39.1 instrumentado y estabilidad

Sesión `manual-20261011-131442-711ba367`, PID 19100, paquete 4.2.39.1: CPU/RAM entre 13:14:42,936Z y 13:17:13,529Z (150,59 s, 579 muestras). `trace.jsonl` es copia del registro del proceso iniciado 13:14:27,891Z, y por tanto incluye actividad anterior al intervalo de CPU. Resume `ui-instrumented-summary`:8.422 registros, 73 operaciones Requested reales, 0 descartes y frecuencia de modo del monitor 144 Hz. Los datos del modo instrumentado se presentan aparte de la comparación principal.

| Fase real en 100k (2 operaciones completas) | p50 ms |
| --- | ---: |
| Primer elemento nativo disponible | 10,31 |
| Primera publicación no vacía (64 entradas) | 27,18 |
| Callback Rendered con nombre vinculado en viewport | 77,14 |
| Última publicación 100k | 2.438,64 |
| ListingCompleted | 2.501,16 |

Las publicaciones intermedias incrementaron el conteo aproximadamente cada 500 ms; el último callback de actualización de colección duró 16,85/16,77 ms. En los 12 callbacks de publicación, p50/p95 fue 13,01/16,81 ms y la cola de publicación 1,06/14,54 ms. La duración completa de unos 2,4 s todavía incluye enumeración/pipeline; el tiempo pequeño del callback no demuestra que el layout posterior sea pequeño.

Durante scroll en 100k se registraron 62 renders XAML: p50/p95/máximo0,324/29,10/43,19 ms; 10 superaron el presupuesto de 6,94 ms calculado del modo 144 Hz. La separación de callbacks (60 muestras) tuvo p95=110,06 ms y se conserva como una métrica distinta, sin convertirla en FPS. La cola de baja prioridad (284 muestras asociadas a operaciones100k) tuvo p50/p95 0,29/50,26 ms y máximo 3.125,40 ms. Los dos retardos mayores ocurrieron después de ListingCompleted y antes de la siguiente navegación vacía; no se atribuyen a GC, layout o UIA sin captura adicional. Los callbacks de realización del probe tardío resultaron 0: eso señala una limitación de enganche, y no demuestra cero contenedores realizados. No se observó PointerInputDelivered/KeyInputDelivered porque la selección se realizó mediante UIA, no por entrada física.

`ui-PhaseIII-20261011-131545-c7901604` contiene 20 ciclos de navegación rápida, 140 setters enviados y 20 estados finales de 1k correctos, sin timeouts. El proceso puede coalescer solicitudes antes de entrar en el pipeline: 140 setters no significa 140 enumeraciones ejecutadas. La mediana del envío del ciclo fue 284,91 ms y de confirmación final 460,09 ms. El intervalo nominal de 20 ms entre llamadas tampoco acredita el ritmo efectivo.

Se observaron 2.746 trabajos iniciados, 2.621 completados y 125 cancelados; el conteo activo al iniciar fue 4 en 2.690 muestras, 3 en 30, 2 en 25 y 1 en 1. Pendientes al inicio: p50=47/p95=82/máximo89. Es el límite del trabajo coordinado por el viewport; las miniaturas nativas desacopladas pueden utilizar el pool STA legado de 12 y no se afirma un límite global de 4 para todo el backend.

La captura acumuló 119,31 segundos de CPU multicore. Bytes privados iniciales/finales/pico243,13/320,07/443,89 MB; working set 411,39/506,84 MB. El heap administrado final fue 28,04 MB, con pico 142,90 MB. En los últimos 9,92 s de reposo, CPU 1,594 s, privados 320.081.920→320.073.728 bytes y handles 2.988→2.986. Esto observa retención estable en ese intervalo; no equivale a una prueba de ausencia de fuga. Los contadores de GC final/inicial fueron gen0:153/6, gen1:108/6, gen2:54/6; no se midieron las pausas individuales.

## Comparación pareada 4.2.39.2 con diagnóstico desactivado

Esta ronda corrige la clasificación adaptativa que, en 4.2.39.1, ejecutaba Directory.Exists repetidamente desde el hilo UI. Mantiene la regresión observada en primera fila: **100k pasa de 154,36 a 186,20 ms (+31,84 ms; +20,6%)**. A la vez, el conteo final 100k pasa de 3.015,21 a 1.153,19 ms (61,8% menos). No se presenta la mejora de finalización como mejora de primera respuesta.

Phase III 4.2.39.2, PID 21012: navegación `ui-PhaseIII-20261011-134258-cd846952`, selección `ui-PhaseIII-20261011-134404-643d41cb`, scroll corregido `ui-PhaseIII-20261011-134548-9245e258`. Rollback real Phase II 4.2.39.0, PID 36684: navegación `ui-PhaseII-20261011-134729-950974a7`, selección `ui-PhaseII-20261011-134852-aa3dca5e`, scroll `ui-PhaseII-20261011-134859-81ff907f`. Ambas variantes se verificaron en el paquete registrado, con diagnóstico OFF, el mismo script con ValuePatterns cacheados y bounds `26;26;1440;753`. Cada navegación tiene n=5 por tamaño; las 40 muestras terminaron sin timeout. Orden III→II, caché caliente/no controlada; la muestra no separa causalmente variación de orden/caché. El resumen completo es `ui-final-paired-off-detailed-summary`.

Las celdas muestran **p50 / p95 / desviación poblacional / IQR**, en ms. Delta positivo significa más lento en III.

| Entradas / medida | II | III | Delta p50 |
| --- | ---: | ---: | ---: |
| 1000 / primera fila | 165,93 / 204,18 / 22,17 / 16,93 | 160,44 / 165,05 / 10,08 / 7,74 | -3.3% |
| 1000 / conteo final | 166,14 / 216,60 / 27,84 / 16,82 | 165,97 / 170,57 / 5,43 / 6,29 | -0.1% |
| 10000 / primera fila | 153,53 / 178,91 / 13,91 / 2,50 | 160,17 / 162,80 / 3,59 / 1,26 | +4.3% |
| 10000 / conteo final | 365,32 / 369,10 / 15,53 / 5,16 | 213,19 / 236,18 / 12,66 / 14,05 | -41.6% |
| 50000 / primera fila | 168,81 / 297,55 / 67,95 / 24,27 | 167,70 / 170,29 / 11,21 / 12,65 | -0.7% |
| 50000 / conteo final | 1446,64 / 1694,09 / 125,61 / 226,35 | 602,35 / 685,86 / 65,23 / 18,78 | -58.4% |
| 100000 / primera fila | 154,36 / 171,93 / 12,00 / 12,91 | 186,20 / 275,98 / 45,83 / 4,83 | +20.6% |
| 100000 / conteo final | 3015,21 / 3192,85 / 131,31 / 76,72 | 1153,19 / 1230,67 / 61,60 / 100,73 | -61.8% |

La primera fila de 10k también empeoró un 4,3%; su conteo final mejoró un 41,6%. No se mezclan estas cinco muestras con las dos preliminares para diluir una regresión. El máximo de consulta de ruta II→III fue 107,36→118,89 ms, nombres 178,21→132,48 ms y conteo 90,88→70,86 ms. Son costes del observador y esperas del proveedor; no se restan del resultado.

Las transiciones a vacío anteriores a los targets 100k tuvieron mediana/p95/máximo II 2.599,40/3.287,11/3.337,46 ms, III 104,47/133,43/136,01 ms. El máximo global de SetValue a vacío cayó de 2.687,26 a 3,43 ms; el máximo PatternQuery inicial fue 2,28/2,16 ms. La etiqueta Scenario de transitions.csv identifica el target siguiente; la primera repetición puede venir de un tamaño anterior. Es evidencia de la cola observada al abandonar una carpeta, sin atribuir por sí sola cada espera al layout. EventPipe de la versión .1 había identificado Directory.Exists→clasificación adaptativa en 1.056/1.190 muestras UI durante el intervalo excluido de GetCurrentPattern; sólo tres suspensiones GC sumaron 5,66 ms (máximo 2,05 ms). Esa captura motivó la corrección por pertenencia de extensión conocida, sin acceso al disco.

Se confirmaron 5/5 selecciones y 20/20 movimientos en cada variante. CallMs y ObservationMs son métricas de proveedor, no clic/gesto físico. La comparación conserva también el peor p95 de llamada de incremento grande de III (14,92 frente a 1,79 ms) y el peor p50 de su confirmación (6,08 frente a 3,32 ms).

| Acción / medida (n=5) | II p50 / p95 ms | III p50 / p95 ms |
| --- | ---: | ---: |
| Select llamada | 20,91 / 25,27 | 16,67 / 19,32 |
| Select confirmación | 24,33 / 28,15 | 22,27 / 27,78 |
| Scroll pequeño llamada | 0,99 / 3,39 | 0,65 / 3,61 |
| Scroll pequeño confirmación | 3,34 / 17,23 | 2,30 / 14,75 |
| Scroll grande llamada | 1,09 / 1,79 | 0,78 / 14,92 |
| Scroll grande confirmación | 3,32 / 17,89 | 6,08 / 19,97 |
| Salto final llamada | 0,82 / 1,42 | 0,69 / 0,78 |
| Salto final confirmación | 45,57 / 106,60 | 43,34 / 72,54 |
| Salto inicio llamada | 0,78 / 1,00 | 0,76 / 1,01 |
| Salto inicio confirmación | 98,39 / 508,31 | 70,94 / 82,77 |

## Perfil 4.2.39.2 instrumentado y fallo de estrés conservado

PID 27612, paquete .2. El ancla numérica de trace.jsonl es 13:51:01,8543167Z a SessionMs 10,5771, con intervalo de muestreo 0,0021 ms. La copia completa más reciente está en `manual-20261011-135516-8abb02fb/trace.jsonl`: 11.977 registros, 56 operaciones Requested y 0 descartes. Incluye arranque, cuatro selecciones durante carga, ocho navegaciones ordinarias, scroll y estrés incompleto. `ui-final-instrumented-profile-summary` resume el registro completo; `ui-final04-segmented-profile/profile.json` identifica los grupos por operation ID y evita mezclar sus fases.

La captura EventPipe real de root abarcó 13:51:58→13:52:43Z y sólo solapa las primeras selecciones (operaciones 3/5, `ui-PhaseIII-20261011-135236-e96883c1`), no la navegación ordinaria iniciada 13:52:53Z. El análisis de esa captura diagnosticó 32 pausas GC, total 573,44 ms y máximo 73,97 ms, en el proceso instrumentado. No se usa esa captura como perfil de la navegación posterior ni como pausa GC del modo OFF. La clasificación adaptativa ya no aparece entre las principales pilas UI de la selección; permanecen metadatos/WinRT/tags. Las ventanas de correlación inicialmente rechazadas se conservan como tales.

Las dos navegaciones ordinarias a 100k son exclusivamente operaciones 19/21, `ui-PhaseIII-20261011-135253-77dc1724`, posteriores a EventPipe. Ambas primeras publicaciones tuvieron 64 entradas.

| Fase (n=2) | p50 ms | p95 ms |
| --- | ---: | ---: |
| Primer elemento nativo | 11,14 | 11,62 |
| Primera publicación no vacía | 30,34 | 31,76 |
| Rendered con nombre vinculado visible | 108,80 | 110,00 |
| Última publicación 100k | 1058,10 | 1079,02 |
| ListingCompleted | 1316,30 | 1353,97 |
| AdaptiveLayoutCompleted desde Requested | 1358,61 | 1388,09 |
| Duración de clasificación adaptativa | 2,48 | 2,53 |

En seis publicaciones de esas operaciones, el callback tuvo p50/p95 10,12/14,51 ms, y la cola 17,85/41,64 ms. En el primer Rendered con nombre se habían observado 14 callbacks reales phase0 en cada operación. El contador es acumulado: los callbacks posteriores de reciclado/realización y scroll no equivalen a contenedores únicos vivos. La fase adaptativa dura 2,48 ms de mediana y termina unos 42,30 ms después de ListingCompleted; esa diferencia incluye esperas/código posterior, no se etiqueta automáticamente como duración adaptativa.

En la ventana acotada de esas navegaciones hubo 136 renders XAML, p50/p95/máximo 0,656/43,15/83,82 ms; 18 superaron 6,94 ms. La sonda de baja prioridad en esas operaciones incluye reposo posterior: 908 muestras, p50/p95/máximo 0,30/1,20/288,11 ms. El perfil no prueba 144 FPS sostenidos. La mediana pequeña tampoco elimina los frames lentos.

Las cuatro selecciones se confirmaron con conteo 64 antes y después de Select, antes de 100k. En las dos que solapan EventPipe, confirmación desde navegación 341,10/310,87 ms y llamada Select 50,96/10,18 ms. Las dos adicionales `ui-PhaseIII-20261011-135435-0633e3b9` confirmaron a 336,67/179,36 ms y llamada 10,50/8,62 ms; al conteo final, el proveedor capturado seguía seleccionado y conservaba el mismo nombre en 2/2. El nombre sólo se comparó en memoria, sin registrarlo. Esto demuestra selección accesible durante publicación parcial, sin afirmar respuesta a clic físico. Los grupos EventPipe y posteriores se mantienen separados.

`ui-PhaseIII-20261011-135447-0d6112a3` confirmó 20/20 movimientos de scroll, 5 por modo, sin timeout. El trace registró 147 renders XAML de scroll: p50/p95/máximo 0,595/59,27/90,36 ms; 50 excedieron 6,94 ms. Incluye preparaciones de saltos no cronometradas. Los 145 gaps de callback tuvieron p50/p95/máximo 10,53/114,65/182,69 ms, sin conversión a FPS. Las llamadas de proveedor se conservan en CSV por modo; no hay medición de presentación física.

En el registro completo hubo 2.429 trabajos iniciados, 2.331 completados y 98 cancelados; activo=4 en 2.370 inicios, 3 en 43, 2 en 14 y 1 en 2. Pendientes p50/p95/máximo 47/82/88. Sigue siendo el límite del trabajo coordinado y no del pool STA nativo legado.

**El estrés de .2 falló y motivó una corrección adicional.** `ui-PhaseIII-20261011-135535-32b6640d` conserva diez ciclos correctos y un undécimo con timeout (índice 10). El ciclo envió la secuencia fija 100k/10k/50k/vacía/100k/vacía/1k en 342,73 ms; el target final 1k no se estabilizó en 30 s. Se comprobó después que la ventana seguía viva y accesible en 100k. El trace termina en operación 56, Requested 13:55:50,8644195Z, primera publicación 64 a 13:55:50,8853049Z, publicación final 100k a 13:55:51,8868564Z y ListingCompleted a 13:55:52,1754546Z; no existe Requested posterior. Las rutas se omiten por diseño: MaximumPublishedCount permite observar lo publicado, no reconstruir todos los targets cancelados. La revisión antigua del script no guardaba cada setter ni el UTC inicial del ciclo; se conserva esa limitación y no se inventan timestamps. La auditoría del flujo detectó resoluciones asíncronas de rutas con posibilidad de cometer una intención antigua o descartar la última por un breadcrumb todavía antiguo. Root/scroll preparan gates por revisión de envío y working-directory/breadcrumb para una versión posterior; este resultado .2 no acredita veinte ciclos estables.

Capturas de proceso, separadas porque sus intervalos se solapan unos cinco segundos:

| Captura | Muestras / duración | CPU total multicore | Privados inicial / final / pico MB |
| --- | ---: | ---: | ---: |
| manual-20261011-135221-57a29359 | 689 / 180,58 s | 89,41 s | 241,88 / 438,94 / 470,13 |
| manual-20261011-135516-8abb02fb | 289 / 75,55 s | 41,27 s | 438,94 / 399,97 / 454,67 |

No sumar su CPU ni duplicar sus trazas completas. Working set inicial/final 410,15→622,89 MB y 622,89→585,27 MB respectivamente. Heap administrado final/pico del registro completo 100,51/117,08 MB, sin GC forzado. Los últimos 9,93 s de la segunda captura son reposo posterior al fallo en la carpeta 100k: CPU 1,56 s, privados -69.632 bytes y handles 2.957→2.956. Es observación de retención en ese reposo; no equivale al protocolo exitoso de 20 ciclos ni a ausencia de fuga.

## Comparación manual con Explorer y versiones anteriores

Usar la misma carpeta sintética, vista de detalles, orden por nombre ascendente, tamaño de ventana, escala y monitor. Abrir primero una carpeta vacía, después la carpeta destino. Repetir cinco veces por tamaño en orden alternado Phase II/III; reiniciar cada variante entre rondas. Explorer original se utiliza mediante sus controles normales: no modificarlo ni registrar asociaciones. Grabar con una cámara externa de velocidad conocida permite comparar clic hasta primera fila sin alterar el pipeline del escritorio; registrar la incertidumbre de un fotograma de cámara. Anotar finalización de carga y primera selección por separado.

El helper definitivo añade medición de frames de navegación y recibe phase0 directamente desde el layout; las sesiones anteriores de 4.2.39.1 conservan su instrumentación original y el contador tardío 0, sin reinterpretarlo. `transitions.csv` conserva para futuras rondas el coste de setter/ruta/nombres de cada transición vacía con marcas UTC, para estudiar la cola posterior al conteo. La ventana de frames de navegación incluye el primer callback que termina después de 2 s, de modo que no oculta un frame que haya bloqueado al final.

El original no dispone de los hooks de Phase III. Las medidas UIA de proveedor, los eventos XAML y la observación manual constituyen conjuntos diferentes; no mezclar porcentajes. Tampoco comparar un componente interno de Phase II con una apertura gráfica de Phase III.

## Documentación oficial consultada

- [Optimización ListView/GridView para WinUI](https://learn.microsoft.com/en-us/windows/apps/develop/performance/optimize-gridview-and-listview): paneles virtualizados, coste de creación/binding/layout y actualización progresiva.
- [CompositionTarget.Rendered](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.compositiontarget.rendered): callback tras el render de XAML, ejecución en el hilo UI y desuscripción cuando no se usa.
- [RenderedEventArgs.FrameDuration](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.renderedeventargs.frameduration): duración del render del frame más reciente.
- [UI Automation Overview](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-overview): modelo de clientes/proveedores y patrones de controles.
- [EnumDisplaySettingsW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaysettingsw): frecuencia de modo de pantalla comunicada por Windows, distinta de una medición óptica de refresco dinámico.
- [Windows Performance Recorder](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/windows-performance-recorder): ETW y análisis con WPA. No se ha iniciado una captura global/elevada ni se atribuyen pausas de GC sin eventos correspondientes.
## Ronda final 4.2.39.4: controles de instancia y regresiones conservadas

La primera ronda .4, PID 31824 (`ui-PhaseIII-20261011-142434-1b506ed8` y acciones posteriores), se realizó mientras otro proceso experimental del mismo layout, PID 14756, permanecía abierto en el fixture 1k. Root identificó dos activaciones de protocolo durante la preparación. Se conserva como **ronda con dos instancias, excluida de la comparación principal**. Su éxito funcional de 30 ciclos rápidos sigue siendo evidencia por PID, sin utilizar sus latencias para una comparación justa. No se agrupan esas muestras con la ronda controlada para diluir regresiones.

El intento posterior de diagnóstico, PID 8352, produjo UIA y CPU/RAM, pero **ningún JSONL**: `manual-20261011-143253-3b2b5136`, 120,19 s, 456 muestras, TraceFound=false. Root detectó una confusión de generación de proceso al interpretar PID no monótonos después de las activaciones anteriores: se había conservado una instancia creada con diagnóstico OFF. Esta es una explicación de preparación, no evidencia de un fallo del logger. No se atribuyen a este proceso tiempos internos de enumeración, GC o frames. Su resumen `ui-final06-single-instrumented-summary` conserva el nombre histórico del directorio; los archivos vacíos operations.csv/trace-summary.csv y TraceFound=false son la autoridad. Las selecciones durante carga 2/2 y rapid20/20 son comprobaciones UIA de ese proceso, separadas de un perfil instrumentado verificado.

La ronda controlada se ejecutó después de cerrar las instancias anteriores y lanzar una sola instancia nueva con diagnóstico OFF: PID 36832, paquete 4.2.39.4, layout `managed-release-phase3-stable06`, bounds `26;26;1440;753`. Cada sesión registró ExperimentalInstanceCountAtStart=1 y ExperimentalInstanceCountAtEnd=1, con PID 36832 en ambos extremos; el filtro es igualdad exacta con el Files.exe del paquete registrado. Navegación: `ui-PhaseIII-20261011-143626-9f809a92` (14:36:26,916–14:37:01,898Z), selección `ui-PhaseIII-20261011-143702-00ccada5`, scroll `ui-PhaseIII-20261011-143703-9d6681df`, rapid30 `ui-PhaseIII-20261011-143708-ba6153b1`. El resumen `ui-final06-single-off-summary` compara exclusivamente estas muestras con la ronda Phase II n=5 conservada de 13:47Z. Hay separación temporal y caché caliente/no controlada; no es un experimento causal aleatorio.

Las celdas son p50 / p95 / desviación poblacional / IQR, ms, con n=5 por tamaño. Las 20 navegaciones .4 terminaron sin timeout.

| Entradas / medida | Phase II | .4 una instancia | Delta p50 |
| --- | ---: | ---: | ---: |
| 1000 / primera fila | 165,93 / 204,18 / 22,17 / 16,93 | 163,06 / 201,37 / 25,40 / 37,28 | -1,7% |
| 1000 / conteo final | 166,14 / 216,60 / 27,84 / 16,82 | 163,36 / 207,86 / 27,35 / 37,40 | -1,7% |
| 10000 / primera fila | 153,53 / 178,91 / 13,91 / 2,50 | 203,45 / 351,34 / 77,72 / 72,26 | +32,5% |
| 10000 / conteo final | 365,32 / 369,10 / 15,53 / 5,16 | 244,28 / 358,13 / 59,46 / 57,21 | -33,1% |
| 50000 / primera fila | 168,81 / 297,55 / 67,95 / 24,27 | 170,32 / 300,39 / 64,05 / 34,57 | +0,9% |
| 50000 / conteo final | 1446,64 / 1694,09 / 125,61 / 226,35 | 609,18 / 739,64 / 73,55 / 115,45 | -57,9% |
| 100000 / primera fila | 154,36 / 171,93 / 12,00 / 12,91 | 185,33 / 197,74 / 11,94 / 11,35 | +20,1% |
| 100000 / conteo final | 3015,21 / 3192,85 / 131,31 / 76,72 | 1129,94 / 1229,85 / 49,51 / 24,91 | -62,5% |

La regresión de primera fila permanece en 10k (+49,92 ms) y 100k (+30,97 ms), aunque la finalización sea más rápida. En 50k el p95 de primera fila también es ligeramente peor (300,39 frente a 297,55 ms); la muestra pequeña y la variación no acreditan equivalencia. El observador esperó hasta 109,01 ms en lectura de ruta, 202,60 ms en nombres y 65,00 ms en conteo; el coste máximo de acceso al ValuePattern cacheado del target fue 0,164 ms. Las transiciones vacías previas a 100k fueron p50/p95/máximo 116,85/191,29/205,60 ms, frente a 2599,40/3287,11/3337,46 de II. No se restan costes del proveedor ni se atribuye la primera fila a una única fase sin trace válido.

Se verificaron 5/5 selecciones, 20/20 movimientos y 30/30 ciclos rápidos. Selección: llamada p50/p95 16,57/18,98 ms; confirmación 19,59/22,35 ms. Los ciclos registran 210 setters individuales, cada uno con UTC, y el target final 1k siguió correcto después de 1 s de reposo en todos los ciclos. Envío del ciclo p50/p95 327,31/476,03 ms; conteo final p50/p95 546,19/866,02 ms. No se afirma que los 210 setters produzcan 210 enumeraciones ni que estas pruebas sustituyan entrada física.

| Scroll, n=5 por modo | II llamada p50/p95 | .4 llamada p50/p95 | II observación p50/p95 | .4 observación p50/p95 |
| --- | ---: | ---: | ---: | ---: |
| Pequeño | 0,99 / 3,39 | 0,80 / 4,56 | 3,34 / 17,23 | 16,36 / 34,64 |
| Grande | 1,09 / 1,79 | 0,76 / 1,39 | 3,32 / 17,89 | 2,83 / 15,42 |
| Final | 0,82 / 1,42 | 0,74 / 1,40 | 45,57 / 106,60 | 45,52 / 111,36 |
| Inicio | 0,78 / 1,00 | 1,07 / 1,25 | 98,39 / 508,31 | 74,04 / 107,01 |

También se conservan los retrocesos de incremento pequeño (observación y p95 de llamada), p95 de observación de salto al final y llamada de salto al inicio. Son métricas del proveedor, no FPS ni latencia óptica.

## Controles añadidos al protocolo final

Measure-Phase3UiSession valida que sólo exista un proceso con ruta exactamente igual al ejecutable experimental verificado y registra los PID/conteos al comienzo y al final, fuera de Stopwatch. Nunca cierra automáticamente procesos. StartedUtc del target se toma inmediatamente antes de SetValue y SetValueCompletedUtc después; StopwatchStartedUtc mantiene el límite histórico anterior al descubrimiento del setter. Las marcas UTC permiten separar setter→Requested (validación de toolbar, raíces y SetWorkingDirectory/Git previos), Requested→publicación y publicación→Rendered, con la incertidumbre del reloj UTC. No se añade una consulta UIA por esa marca.

Capture admite -RequireTrace: antes de empezar exige un JSONL de ese PID, creado después de StartTime menos una tolerancia de 2 s, cuya primera línea sea SessionStarted con UtcTicks positivo. Activar config sólo afecta al siguiente proceso: cerrar el anterior, verificar cero instancias propias, activar, lanzar una sola vez y confirmar StartTime/log antes de medir. Capture registra también conteos de instancias y PID; process.csv cubre el intervalo y JSONL conserva todo el proceso. Este control evita tratar un proceso OFF preexistente como instrumentado.
## Perfil final .4 verificado, separado del modo OFF

El arranque se verificó por generación y ruta exacta: PID 28748, StartTime 14:46:33,3647067Z, configuración activa antes de lanzar una sola vez, JSONL SessionStarted 14:46:35,4201784Z (ancla SessionMs 11,6209; bracket 0,002 ms). Las dos capturas exigieron -RequireTrace y registraron una instancia al principio y al final. Primera: `manual-20261011-144717-025b2d24`, 14:47:17,155–14:49:17,716Z, 120,56 s, 456 muestras. Segunda, sin solapamiento: `manual-20261011-144943-7c81486f`, 14:49:43,107–14:50:58,487Z, 75,38 s, 286 muestras. El último JSONL es una copia de **todo el mismo proceso**, 12.900 registros, 85 operaciones Requested, cero descartes y modo de monitor comunicado de 144 Hz. No sumar ni duplicar las dos copias del trace.

Navegación ordinaria n=2 por tamaño: `ui-PhaseIII-20261011-144723-f03d0c41`. Correlación reproducible `ui-final06-verified-correlation/correlation.csv`, 8/8 matches inequívocos por PID, conteo realmente publicado y Requested dentro del intervalo UTC del setter. RequestedUtc reconstruye el origen de la operación restando ElapsedMs del marcador a SessionMs; RequestedMarkerUtc conserva el tiempo del marcador. Las fases relativas usan el mismo origen Stopwatch. La marca Requested comienza después de validación del toolbar y SetWorkingDirectory/Git, no antes de todo el trabajo de navegación.

| Tamaño (n=2) | Setter→Requested p50 ms | Primer nativo desde Requested | Publicación no vacía desde Requested | Primer Rendered visible desde Requested | Publicación→Rendered | Setter→Rendered |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1k | 69,34 | 4,27 | 26,83 | 115,08 | 88,25 | 184,41 |
| 10k | 69,98 | 5,19 | 28,57 | 79,11 | 50,54 | 149,10 |
| 50k | 101,39 | 8,28 | 32,64 | 124,06 | 91,42 | 225,45 |
| 100k | 94,86 | 11,06 | 29,59 | 108,91 | 79,32 | 203,77 |

En 100k las dos operaciones ordinarias son 19/21. Setter→Requested fue 105,02/84,70 ms; primera publicación 64 a 30,56/28,62 ms; nombre vinculado visible a 106,23/111,59 ms. Última publicación 100k a 1048,48/1149,06 ms; ListingCompleted a 1116,43/1231,95 ms; AdaptiveLayoutCompleted a 1158,27/1273,04 ms, con clasificación real de 2,40/2,81 ms. Siete callbacks de publicación: p50/p95/máximo 10,46/15,76/16,48 ms; espera de publicación 7,40/21,61/22,59 ms. Los primeros renders XAML medidos ocurrieron a 105,95/111,33 ms y duraron 4,10/3,91 ms; en el primer Rendered visible había 14 callbacks phase0 en cada operación.

No se registró ItemJobStarted antes del primer Rendered visible en ninguna de las ocho operaciones ordinarias; en las dos 100k tampoco hubo inicios de trabajos de otras operaciones dentro de Requested→Rendered. Esto no respalda la hipótesis de que nuevos trabajos de metadata en esa ventana expliquen esta captura. No excluye tareas anteriores ya activas ni todo el trabajo UI. Se decidió no añadir una barrera de render sin esa evidencia. La regresión OFF de primera fila sigue sin explicación causal porque Phase II no posee estas fases internas; investigar validación previa y espera publicación→render exige otra comparación controlada. Un callback XAML visible tampoco prueba presentación física.

`ui-final06-verified-segmented-profile/profile.json` conserva distribuciones y límites de ventana. El gate de navegación de operaciones19/21 incluye 173 frames y solapa 36 con el comienzo del scroll; esa distribución no se presenta como navegación pura. Cortando antes de la primera sesión de scroll (14:47:38,1796713Z), quedan 137 frames: p50/p95/máximo 0,675/38,33/112,07 ms, 18 por encima de 6,94 ms. Las 1.281 sondas dispatcher etiquetadas con esas operaciones siguen incluyendo scroll y reposo posteriores mientras 100k es la operación actual: p50/p95/máximo 0,390/6,241/328,878 ms. No se atribuyen a enumeración pura.

Selección durante carga `ui-PhaseIII-20261011-144717-2acd3f5c`: 2/2 confirmaciones a 477,66/145,78 ms desde el setter, con conteo 64 antes y después, anterior al conteo final 100k. Call Select 114,81/9,81 ms; el mismo proveedor conservó selección y nombre al conteo final en ambas. La variación, incluido el outlier de la primera llamada, se conserva; no es latencia de clic físico.

**Scroll instrumentado incompleto:** el intento `ui-PhaseIII-20261011-144738-0e6c3952` se detuvo durante preparación de un salto y su revisión antigua del script sólo escribió metadata inicial, por lo que no se reconstruye un CSV perdido. Una única repetición fresca `ui-PhaseIII-20261011-144841-a16f38f2` conservó 19 movimientos confirmados (5 pequeños, 5 grandes, 5 final, 4 inicio) y terminó Incomplete al preparar el quinto inicio: proveedor en 99,9952438479173% durante el límite de 2 s, frente a la tolerancia estricta 0,001% de 100. No se relaja retrospectivamente la tolerancia ni se interpreta ese casi-final como bloqueo físico de 2 s. failure.json conserva porcentaje/UTC 14:49:00,6231774Z. El script ahora escribe CSV parciales y metadata Incomplete ante excepciones. Las 20 confirmaciones OFF siguen siendo una serie distinta válida.

El trace completo de ambos intentos contiene 289 renders XAML de scroll: p50/p95/máximo 0,626/59,35/101,34 ms, 94 por encima de 6,94 ms. Incluye preparaciones no cronometradas. Sus 284 gaps p50/p95/máximo 11,66/110,37/188,99 ms no son FPS. No se afirma una tasa de 144 FPS sostenida.

Rapid instrumentado `ui-PhaseIII-20261011-144943-bea50648`: 20/20 ciclos, 140 setters individuales registrados y 20 targets finales 1k correctos después de 1 s de reposo. Tras las acciones se esperaron al menos 10 s adicionales. El trace completo registra 3.049 trabajos iniciados, 2.967 completados y 82 cancelados. Activos al inicio: 4 en 2.964, 3 en 57, 2 en 20, 1 en 8; pendientes p50/p95/máximo 41/79/87. El pool STA nativo desacoplado continúa fuera de ese límite de cuatro trabajos coordinados.

| Intervalo de proceso, sin solapamiento | CPU multicore s | Privados inicial / final / pico MB | Working set inicial / final MB |
| --- | ---: | ---: | ---: |
| Captura 120,56 s | 62,94 | 231,53 / 400,18 / 412,19 | 398,87 / 579,47 |
| Captura 75,38 s | 63,55 | 399,87 / 337,33 / 422,89 | 579,21 / 519,46 |

Últimos 9,92 s de reposo: CPU 3,328 s, privados 337.334.272 bytes y handles 2.878 estables entre extremos. Heap administrado final/pico 26,15/111,25 MB; contadores gen0 5→166, gen1 5→143, gen2 5→59. No se forzó GC ni se midieron pausas individuales; estabilidad breve de retención no acredita ausencia de fuga ni CPU ociosa cero. Root dejó después una sola instancia .4 con diagnóstico OFF para uso normal.

Los contratos de rutas/working-directory posteriores al fallo .2 se verificaron además con métodos reales extraídos literalmente: `reports/phase3-working-directory-contracts-final-01.json`, 41/41, SHA completos/métodos/generated en tests-extra/Phase3WorkingDirectory/source-hashes.json. La prueba gráfica OFF confirma 30 ciclos y la instrumentada 20, pero no sustituyen los contratos de root tardío, ABA, disposal y callbacks reentrantes. Los resultados anteriores permanecen intactos.