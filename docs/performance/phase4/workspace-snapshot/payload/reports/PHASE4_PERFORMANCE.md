# Phase IV — rendimiento

El candidato medido actual es **final 07, versión 4.2.39.9**, fuente `bf4390ee0a2eed6cece0e99d1a021ce7fd255574`. Build 08 terminó con **0 errores, 88 advertencias y 1:52,50**. [Provenance](phase4-build-provenance-firstvisit.json), [contenido validado](../artifacts/development-layout/managed-release-phase4-final07/package-validation.json) y [apertura OFF exacta](phase4-final07-off-launch.json), PID 28760, identifican el paquete. Pasan **327/327 contratos actuales**. Las 52 imágenes repetidas pasan de **1.320,87 ms en III a 659,79 ms de mediana**, y la primera de **702,66 a 524,18 ms**. Se mantiene el inicio desde arriba; no se afirma carga instantánea, equivalencia con Explorer ni mejora global de todas las condiciones.

Las series actuales de imágenes, vídeo, primeras visitas, navegación, scroll y ciclos rápidos están completas. Operaciones UI: siete Passed y cuatro Pending; el control ON .9 está completo en su alcance y tiene resultados mixtos. Los snapshots .6/.7/.8 y sus evidencias se conservan históricos. Vídeos .9 mejoran frente a III y .7, pero empeoran frente al bloque .8; navegación de 1.000 entradas tiene mediana mayor que III. Esas diferencias se documentan, sin atribuirles causalidad exclusiva.
## Cambios que reducen la espera

Phase IV inicia las solicitudes por índice visible ascendente, con cuatro trabajos de imagen y cuatro de metadatos en colas separadas, hasta 128 pendientes por etapa. La fase 0 solicita una pasada coalescida Normal; fase 3 queda como refuerzo. Imágenes usan Normal y metadatos Low. El fallback ordena también los contenedores realizados cuando WinUI todavía no informa rango visible. Una extracción STA iniciada conserva su plaza hasta el retorno nativo, aunque se cancele la espera de navegación.

La revisión .8 conserva los elementos ya publicados si un snapshot sin agrupación contiene exactamente las mismas referencias en el mismo orden. Si solo añade una cola de hasta 128 elementos a un prefijo existente, publica cada nueva fila mediante un Add individual. Reordenaciones, retiradas, referencias distintas, agrupación y colas mayores mantienen el Reset anterior. No se emiten notificaciones AddRange que GridView/DataGrid no admiten. Se preservan filtrado, semáforo, cancelación y actualización del estado de carpeta.

El decode al tamaño físico mantiene proporción. .8 selecciona `SetSource` síncrono únicamente con opt-in explícito, proporción activada, destino Physical de 1–256 píxeles, PNG con cabecera reconocida, dimensiones de origen de 1–256 en ambos ejes y payload de hasta 256 KiB. Los demás casos mantienen `SetSourceAsync`. La cabecera no valida integridad completa del PNG; los contratos comprueban selección y límites, sin medir el coste real WinUI.

También se eliminan las dos compuertas `x:Phase="1"` del PictureBorder de Grid/Cards y se desplaza el overlay del viewport a la cola Low de metadatos. Así el slot de imagen termina tras aplicar la miniatura y puede atender otra fila visible. Las rutas directas anteriores mantienen su overlay; las guardas de cancelación e identidad siguen vigentes.

La revisión .9 extiende ese decode físico acotado a la caché compartida de iconos genéricos. Además, el resultado inicial conserva una referencia al icono ya aplicado: se omite la segunda aplicación Low solo si el resultado sigue vigente y FileImage es exactamente esa referencia. Un icono nulo, reemplazado, borrado o invalidado conserva la reaplicación anterior. Así la generación fría puede empezar sin esperar otro decode/despacho del mismo icono. No se introduce otra caché de bitmaps ni se alteran providers por archivo.
## Miniaturas observadas con diagnóstico OFF

Diez repeticiones por variante y fixture, ventana de 1.440×753 y la misma geometría/detector. Cada apertura completa **52/52 imágenes** o **3/3 vídeos**, cero timeouts y ningún build concurrente. La referencia III usa 4.2.39.4; .6 y .7 son candidatos anteriores. Percentiles interpolados:

| Snapshot | Primera imagen p50 / p95 ms | 52 imágenes p50 / p95 ms | Primera/todas vídeo p50 / p95 ms |
|---|---:|---:|---:|
| III | 702,66 / 753,80 | 1.320,87 / 1.383,10 | Primera 305,46 / 349,68; todas 307,28 / 355,56 |
| IV .6 final 03, histórico | 753,55 / 876,11 | 1.247,09 / 1.587,00 | 271,86 / 314,23 |
| IV .7 final 05, superseded | 650,14 / 786,14 | 1.219,10 / 1.382,24 | 243,06 / 280,06 |
| IV .8 final 06, histórico | 541,54 / 589,82 | 708,90 / 796,27 | 197,32 / 225,74 |
| **IV .9 final 07, actual** | **524,18 / 631,03** | **659,79 / 733,35** | **233,58 / 284,60** |

Fuentes: [resumen de imágenes](../benchmarks/results/phase4/thumbnails-final07-images-summary/summary.json), [resumen de vídeos](../benchmarks/results/phase4/thumbnails-final07-videos-summary/summary.json), [sesión .9 imágenes](../benchmarks/results/phase4/thumbnails-IV-off-final07-primary52/session.json) y [sesión .9 vídeos](../benchmarks/results/phase4/thumbnails-IV-off-final07-primary-videos/session.json).

Frente a III, las medianas observadas .9 disminuyen **25,40% al inicio** y **50,05% al completar las 52 visibles**. Frente a .7, disminuyen **19,37% y 45,88%**. Frente a .8, la primera baja 3,20% y todas 6,93%, pero su p95 inicial sube de 589,82 a 631,03 ms. Vídeo tiene mediana **18,38% mayor que .8**, aunque menor que III/.7. Son diferencias entre bloques con cachés/historia distintas, sin aislar la contribución de cada cambio. La primera imagen de unos 524 ms no es instantánea.

El detector reconoce cuatro colores sintéticos en el buffer PrintWindow del FileList; un icono genérico no cuenta como miniatura. UIA, captura, detector, sondeo y guardado permanecen incluidos. PrintWindow puede inducir render y no es presentación física. En .9 el intervalo mediano que encierra la primera detección de imágenes es **140,75 ms** y el de todas **141,08 ms**; vídeo **91,01 ms**. No se resta ese overhead. La caché es caliente/no controlada, sin vaciarla globalmente. Los patrones compartidos no demuestran identidad única de archivo; esa protección tiene contratos independientes.

El [análisis espacial .9](../benchmarks/results/phase4/thumbnails-final07-images-summary/order.json) observa finalización de filas en orden superior→inferior en 10/10 repeticiones, y cada fila superior completa antes o en la misma captura que el primer resultado de la siguiente. El primer sondeo ya contiene varias filas en las diez aperturas: los empates impiden identificar los primeros cuatro resultados individuales. El scheduler sí prueba el orden de inicio superior; no se impone a los cuatro providers un orden artificial de finalización.

## Control ON actual .9

[El control sin captura ni readiness polling](phase4-latency-diagnosis-final07-no-capture.json) incluye operaciones 3/5, dos aperturas con 52 cache hits y 52 aplicaciones cada una. Primera aplicación desde Requested **590,26/226,14 ms**, última **772,70/549,61 ms**; espera inicial de pasada **198,58/188,17 ms**. Cache p50 **3,461/3,099 ms**, con máximo **345,58 ms** en la primera apertura y **4,25 ms** en la segunda. Decode p50 **0,222/0,189 ms**, máximo **2,709/0,4038 ms**. El resultado es mixto; no confirma que todos los decodes sean inferiores a 0,5 ms ni una mejora uniforme frente al control .8.

[La primera visita ON .9, operación 7](phase4-latency-diagnosis-final07-firstvisit.json), n=1 a rutas sintéticas nuevas, registra **52 cache misses, 52 generaciones y 52 aplicaciones**. Primera aplicación **558,62 ms**, última **1.398,67 ms**; pasada inicial espera **251,20 ms**. Provider nativo más conversión a bytes p50/p95/máximo **25,79/305,06/389,96 ms**. Decode p50/p95 **0,226/0,387 ms**, pero máximo **23,0963 ms**. El método registra duración hasta retorno; sin RowId ni decodeMode no identifica una cadena de la misma fila ni permite atribuir ese máximo a bloqueo síncrono del hilo UI. La caché global sigue sin controlarse; no se resta este reloj interno del observador OFF.

Los probes de render no aparecen en operaciones 5/7, lo que no demuestra ausencia de rendering o ViewChanged. Este control no mide presentación física, continuidad ni latencia de input. Los marcadores paralelos comparten OperationId; primeros hitos no se emparejan como una imagen individual. El control .9 propio sustituye el estado pendiente; los controles .6/.7/.8 se conservan históricos.
## Diagnóstico de los candidatos anteriores

[La traza .7 sin capturas](phase4-latency-diagnosis-final05-no-capture.json) observa dos entradas de medios, con cache nativo inicial de unos 3 ms. La pasada solicitada en fase 0 espera **190,94 y 226,50 ms**; el primer decode dura **139,18 y 63,79 ms**. La publicación de 64 filas seguida de Reset a 99 y la reanudación asíncrona de decode motivaron la revisión .8. Los marcadores comparten OperationId sin RowId: no forman una cadena de la misma imagen por tomar el primero de cada etapa. La comprobación ON específica .8 se presenta a continuación.

[El diagnóstico histórico .6](phase4-latency-diagnosis-final03.json) muestra 377,70–433,18 ms de realización a primer job en tres aperturas; dos son preparación/calibración y solo una tiene sample ON asociado. .6 esperaba fase 3 y despachos Low. Los contadores cache/decode no explicaban solos toda la latencia OFF. Estas trazas son diagnóstico, no repeticiones primarias adicionales.

## Control ON .8 histórico y primera visita

[El control ON sin capturas](phase4-latency-diagnosis-final06-no-capture.json) incluye solo operaciones 7/9, dos aperturas calientes de la sesión exitosa, sin readiness polling. Los 104 decodes retornan en **0,1316–0,4871 ms**, total **19,7117 ms** de duración registrada. Dos Appended de 35 y dos Reset iniciales corroboran el recorrido incremental. Primera aplicación desde Requested **215,90/196,93 ms**, última **424,19/439,09 ms**; espera inicial de pasada **177,54/164,75 ms**, todavía relevante. Los primeros hitos no tienen RowId y no se emparejan como una misma fila. El coste de retornar del método real WinUI no mide scanout ni continuidad de frames. Startup 1 y los intentos 3/5 de un export fallido se excluyen; la lectura concurrente se corrigió con FileShare.ReadWrite antes de exportar la sesión válida.

[Las tres primeras visitas OFF](../benchmarks/results/phase4/thumbnails-speed-final06-firstvisit-summary/summary.json) a clones de rutas sintéticas alcanzan 52/52 y cero timeouts: primera **753,79/722,26/576,63 ms**, todas **1.286,59/1.385,82/1.186,56 ms**. Una muestra por ruta, sin baseline equivalente ni garantía de cache global fría. No se combinan con las diez aperturas repetidas ni acreditan mejora de todas las condiciones.

[La primera visita ON, operación 11](phase4-latency-diagnosis-final06-firstvisit.json) registra 52 cache misses y 52 generaciones nativas, n=1 y sin captura/polling. Provider más conversión nativa a bytes p50/p95 **24,94/52,32 ms**, máximo **58,34 ms**; decode p50 **0,225 ms**, máximo **0,390 ms**. Primera aplicación **443,16 ms**, última **1.014,14 ms** desde Requested. El primer cache-complete está a 264,00 ms y el primer native-start a 402,47 ms, diferencia 138,47 ms sin identidad de fila compartida: motiva revisar espera de icono genérico antes de generar, sin atribuir esa diferencia causalmente a un elemento. La revisión .9 compilada tiene capturas OFF y control ON propio presentados en este informe; los resultados históricos .8 no se trasladan a ella.

[Las tres primeras visitas OFF .9](../benchmarks/results/phase4/thumbnails-firstvisit-final07-summary/summary.json), una por clone nuevo, completan 52/52 sin timeout. Primera imagen **617,52/491,41/566,58 ms** y todas **1.259,18/1.025,67/1.099,55 ms**. Medianas de tres rutas: .8 **722,26/1.286,59 ms**→.9 **566,58/1.099,55 ms**, diferencias observadas de **21,55%/14,54%**. No hay baseline III/Explorer equivalente, cache global fría garantizada ni n suficiente para colas. No se combinan con las diez aperturas repetidas.
## Navegación con observador común

Original tiene cinco repeticiones por tamaño; III y .9, diez. Ventana de 1.440×753, cultura es-ES, diagnóstico OFF, instancia exacta y ningún build concurrente. Las 20/40/40 muestras completan sin timeout. [El resumen común actual](../benchmarks/results/phase4/navigation-final07-summary/summary.json) usa interpolación lineal en todas las variantes; no mezcla el p95 de rango más cercano de resúmenes históricos. Valores p50 / p95 ms:

| Entradas | Original: primer nombre | III: primer nombre | IV .9: primer nombre |
|---|---:|---:|---:|
| 1.000 | 337,23 / 465,50 | 198,94 / 254,78 | 230,54 / 270,10 |
| 10.000 | 255,33 / 469,47 | 268,73 / 483,55 | 226,59 / 287,43 |
| 50.000 | 242,21 / 512,16 | 270,48 / 425,81 | 238,02 / 288,82 |
| 100.000 | 254,06 / 539,73 | 248,17 / 605,80 | 231,95 / 282,27 |

| Entradas | Original: contador completo | III: contador completo | IV .9: contador completo |
|---|---:|---:|---:|
| 1.000 | 370,90 / 471,76 | 202,86 / 282,94 | 231,83 / 271,11 |
| 10.000 | 458,95 / 1.082,56 | 292,28 / 503,70 | 226,90 / 289,14 |
| 50.000 | 2.028,53 / 2.372,98 | 727,24 / 906,50 | 722,77 / 812,65 |
| 100.000 | 3.548,98 / 3.619,72 | 1.201,27 / 1.639,20 | 1.156,09 / 1.272,99 |

Frente a III, .9 mejora las medianas de primera fila y contador en 10.000–100.000. El contador de 100.000 baja 3,76% y el de 10.000 un 22,37%. En 1.000 la mediana del contador **empeora 28,97 ms (14,28%)** y la del primer nombre aumenta 31,60 ms. El p95 contador 1.000 es menor en este bloque, sin garantía de cola con n=10. No se afirma ausencia de regresión ni superioridad global.

Fuentes: [Original](../benchmarks/results/phase4/navigation-Original-off-raw-a/session.json), [III](../benchmarks/results/phase4/navigation-III-off-raw-verified01/session.json), [IV .9](../benchmarks/results/phase4/navigation-IV-off-final07-raw/session.json) y [CSV común](../benchmarks/results/phase4/navigation-final07-summary/summary.csv). Primer nombre es del proveedor de accesibilidad y contador es estado publicado; no acreditan pixels ni final de layout. UIA queda dentro del tiempo y puede costar cientos de ms. Bloques no alternados y distinta historia impiden aislar causalidad.

[Las cuarenta navegaciones .8](../benchmarks/results/phase4/navigation-IV-off-final06-raw/session.json) y [.6](../benchmarks/results/phase4/navigation-IV-off-final03-raw/session.json) se conservan históricas. .8 tenía contador 100.000 p50 1.307,59 ms y 1.000 de 270,41 ms, mayores que III; .6 contador 100.000 de 1.141,71 ms. No sustituyen las cifras .9. Los intentos con selector equivocado/PID no verificado permanecen excluidos.

La navegación Explorer y sus límites se documentan en [FINAL_EXPLORER_COMPARISON.md](FINAL_EXPLORER_COMPARISON.md). No hay observador de miniaturas Explorer equivalente; no se publica cociente de previews frente a Explorer.
## Contratos de navegación, publicación y recursos

[El índice focalizado actual](phase4-focused-contracts-firstvisit-ready-index.json) suma **168/168**: scheduling 21, miniaturas/STA/decode 60, publicación 24, caché de iconos 24, integración 26, fechas 8 y recursos de búsqueda 5. [Las regresiones actuales](phase4-regression-contracts-firstvisit-ready.json) suman **159/159**: path/toolbar 37, working directory/breadcrumb 41, Shell/enumeración 22, adaptive 17, búsqueda nativa NTFS 28, recursiva/junction 3 y sugerencias 11. Ambos índices corresponden a `bf4390ee`, rebuilds Release secuenciales y la referencia interop de build 08: **327/327**, cero fallos.

Los 24 contratos de publicación enlazan colección real y métodos literales. Comprueban ausencia de Reset ante referencias/orden iguales, Add individual 64→99 y cola de 128, fallback para 129/reordenación/retirada/reemplazo/grouping, filtros, cancelación y semáforo. Shell/GDI, decoder, dispatcher y contenedores de otros harnesses son dobles. Los contratos no se presentan como operaciones WinUI completas.

`SearchTagsAsync` mantiene timeout de cinco segundos mediante el helper existente. La espera se cancela pronto; si el lookup nativo termina tarde, se cierra su handle una vez. Si llega a tiempo, el caller mantiene ownership. [Cinco contratos actuales](phase4-search-resource-contracts-firstvisit-ready.json) cubren éxito, excepción, cancelación, timeout real y veinte carreras, con provider sustituido. Se preservan ranking, proveedores y debounce.

## Fechas, reposo y memoria

El timer de fechas relativas Application actualiza una copia acotada de contenedores realizados, comprobando item/navegación antes de cada fila. Fase 0 refresca la etiqueta antes de mostrarla. Cambio de formato y activación de tab conservan recorrido completo. Se elimina logging interpolado por tick; no se cambia `AppMemoryHelper`, desktop.ini ni Git HEAD sin coste real atribuible.

[La ejecución administrada .9](phase4-date-contracts-and-benchmark-firstvisit-ready.json) tiene ocho contratos y **120 muestras**: 100.000 entradas, 100 realizadas, calentamiento y veinte muestras alternadas por variante/condición. Percentiles interpolados:

| Etiquetas cacheadas | III completo p50 / p95 ms | IV realizado 100 p50 / p95 ms | Asignación mediana III→IV por tick |
|---|---:|---:|---:|
| 100 | 3,073 / 3,354 | 0,053 / 0,068 | 20.944→22.600 bytes |
| 100.000 | 22,096 / 63,413 | 0,039 / 0,089 | 11.200.144→13.000 bytes |

Con 100.000 cacheadas, los máximos de asignación son 20.800.144 bytes III y 22.600 IV; la tabla muestra medianas. La copia añade asignaciones con solo 100 etiquetas cacheadas. Son cifras de métodos administrados con recursos/app sustituidos y en-US, sin extrapolación a CPU, GC, memoria retenida o render de toda la aplicación.

[El perfil III histórico](../benchmarks/results/phase4/baseline-III-profile/analysis-segmented.json) tiene 36 muestras UI de `UpdateDateDisplay` entre 6.393 de reposo; incluyen esperas y no prueban coste dominante. El reposo realmente observado dura 12,676 s, hasta el final de la traza, no los 85,479 s solicitados. Los snapshots de memoria históricos .6 eran mayores que III con historias distintas; no demostraban mejora ni fuga. No se trasladan a .9.

## Scroll y cambios rápidos

La .9 completa **sesenta movimientos OFF**: diez SmallIncrement y diez LargeIncrement en 10.000/50.000/100.000 entradas, cero timeouts. [PHASE4_SCROLL.md](PHASE4_SCROLL.md) conserva cifras actuales y distingue llamada UIA de render. No se midió presentación física ni wheel/arrastre real.

[Los veinte ciclos rápidos .9](../benchmarks/results/phase4/rapid-IV-off-final07-20/session.json) completan **140 solicitudes, veinte confirmaciones finales de carpeta/contador y cero timeouts**. Memoria privada **469,27→358,61 MiB**, máximo **478,60 MiB**: observación finita, sin prueba de ausencia de fuga. No acredita cada imagen durante la ráfaga. [Las operaciones UI .9](phase4-app-interactions-final07.json) tienen siete Passed y cuatro Pending: selección/multiselección, copiar/cortar/pegar, rename, atrás/adelante y toggle de preview restaurado; toggle no verifica contenido decodificado. [FINAL_STABILITY.md](FINAL_STABILITY.md) concreta el alcance. Las series .6/.8 se conservan históricas.

[La segmentación ON común .6](phase4-render-common-increments.json) muestra duración XAML p95 menor, pero intervalos entre callbacks sin mejora y máximo mayor. No valida continuidad .9. El control ON .9 está completo en el alcance de métodos e hitos, con las colas/máximos descritos arriba. No se afirma FPS físico, carga instantánea ni aceptación exhaustiva.

[La apertura final OFF](phase4-final07-off-delivery-launch.json) conserva el paquete .9 exacto, PID **38424**, ventana/control preparados; no acredita tiempo de arranque ni presentación física.