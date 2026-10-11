# Files Turbo Phase IV — miniaturas e iconos

El candidato actual es **final 07 4.2.39.9**, fuente `bf4390ee0a2eed6cece0e99d1a021ce7fd255574`. Build 08: **0 errores, 88 advertencias, 1:52,50**. [Provenance](<../workspace-snapshot/payload/reports/phase4-build-provenance-firstvisit.json>), [contenido validado](<../workspace-snapshot/payload/artifacts/development-layout/managed-release-phase4-final07/package-validation.json>) y [apertura OFF](<../workspace-snapshot/payload/reports/phase4-final07-off-launch.json>), PID 28760, identifican el paquete. Sus **327/327 contratos**, diez aperturas de 52/52 imágenes, diez de 3/3 vídeos y tres primeras visitas de 52/52 están completos sin timeout. Primera imagen p50 **524,18 ms** y 52 visibles **659,79 ms**, frente a **702,66/1.320,87 ms en III**. No se afirma instantaneidad ni equivalencia con Explorer.

Los candidatos .6/.7/.8 y sus datos se conservan históricos. La aplicación de FileImage, la captura PrintWindow y la presentación física son alcances distintos. Vídeo .9 mejora frente a III/.7 y empeora frente a .8. El control ON .9 de primera visita está completo y conserva esperas/máximos relevantes; la primera visita OFF tiene solo una observación por ruta nueva y no acredita frío global.
## Recorrido de imagen y metadatos

Phase III podía consultar y decodificar la misma miniatura cacheada en primera pintura y después al cargar propiedades; además habilitaba extracción completa aunque el cache aportara datos. Es un recuento del código, sin equiparar tres consultas a tres ejecuciones costosas del provider Shell. Cuatro trabajos completos compartían imágenes, overlays, metadatos y Git, de modo que una propiedad lenta podía retrasar la imagen de otra fila visible.

Phase IV conserva cuatro trabajos de imagen y separa otros cuatro de metadatos. La extracción fría solo ocurre ante cache miss o decode cacheado fallido. La imagen se aplica antes de overlays. La revisión .8 libera el slot del viewport después de la imagen y entrega el overlay a la etapa Low de metadatos, manteniendo los guards de cancelación; las rutas directas anteriores siguen esperando su overlay.

| Etapa | Comportamiento actual |
|---|---|
| Realización | Comprueba contenedor/item/navegación. Fase 0 solicita pasada Normal coalescida; fase 3 refuerza. |
| Cola de imágenes | Cuatro activos, hasta 128 pendientes, índices visibles ascendentes y FIFO en empates. |
| Cache | Una consulta y un decode; el resultado vigente se entrega a metadatos sin repetir la imagen. |
| Generación fría | Cache miss o decode fallido conserva generación completa, fallback y retry. El trabajo espera la aplicación de imagen. |
| Overlay .8 | Se atiende en metadatos Low después de la imagen del viewport, sin retener su slot. |
| Cola de metadatos | Cuatro activos, hasta 128 pendientes; propiedades, tags, cloud y Git conservados. |

`BaseLayoutPage` usa `LoadViewportThumbnailAsync`. El handoff contiene item, ruta, modificación, tamaño lógico, DPI, preferencia, token y estado completado; no introduce otro cache de bytes/bitmaps. Reutilizar exige que todos esos valores coincidan y el token siga vigente. Cambio de tamaño/DPI, modificación, renombrado, navegación, preferencias o objeto invalida el resultado. Rechazo/expulsión de metadatos no obliga a repetir una imagen ya válida.

Los iconos genéricos mantienen la caché compartida existente; .9 extiende a ella el decode físico proporcional y acotado, sin crear otra caché. Ejecutables, shortcuts e iconos embebidos conservan la consulta por archivo. Fallo de decode cacheado mantiene generación completa. La recuperación conserva timer de dos segundos y retry por modificación. Cada fallo persistente admite un retry inmediato y luego espera otra interacción; expulsar una petición pendiente no consume intento.

## Decode pequeño acotado y publicación sin reconstrucción

Se solicita `ceil(tamaño lógico × DPI)` con DecodePixelType.Physical. Para PNG, IHDR determina el lado mayor que se limita; el otro queda libre y se conserva proporción. Los callers anteriores conservan sus valores por defecto. Por encima del bucket Shell de 256 se solicita el tamaño físico redondeado hacia arriba: 256 DIP a escala 2 pide 512; 256,5 pide 257. No se garantiza que cada provider suministre esa resolución.

En .8, `SetSource` síncrono solo se selecciona si el caller activa explícitamente la ruta y proporción, el tipo es Physical, el destino es de **1–256 píxeles**, el payload es de **hasta 256 KiB**, y firma/IHDR PNG reconocidos informan dimensiones de origen **1–256 en ambos ejes**. Un destino pequeño no hace síncrono un raster de origen grande. Los demás casos usan SetSourceAsync. La cabecera no valida CRC, chunks ni integridad completa; un fallo de decoder conserva el retorno nulo/fallback. Los contratos prueban selección y límites mediante dobles, sin acreditar latencia real, aspecto renderizado ni continuidad de frames.

La publicación .8 sin agrupación omite Reset si el snapshot contiene las mismas referencias en el mismo orden. Para un prefijo existente más cola de hasta 128, emite Add individuales, conservando filas y miniaturas ya realizadas. Otras reordenaciones, retiradas, referencias, agrupaciones y colas mayores mantienen bulk Reset. La publicación inicial desde vacío también mantiene Reset. Se elimina únicamente `x:Phase="1"` de los dos PictureBorder Grid/Cards; se conservan x:Load y las fases restantes.

En .9, InitialThumbnailLoadResult conserva solo la referencia al icono genérico ya aplicado. La segunda aplicación Low se omite únicamente si el resultado inicial sigue vigente y FileImage es exactamente esa referencia. Icono nulo, reemplazado, borrado, objeto/token/ruta/modificación/tamaño/DPI/preferencia cambiados conserva reaplicación. La generación fría puede comenzar después del icono inicial sin esperar una segunda aplicación del mismo bitmap. La caché compartida mantiene coalescing por extensión/tamaño, aislamiento al Clear y rechazo de bitmap fuera del hilo UI.
## Prioridad superior y desplazamiento

El índice, no el orden casual del callback, decide la solicitud. Si WinUI informa rango `(-1,-1)`, se ordenan contenedores realizados vigentes por índice. Los contratos con callbacks inversos o mezclados bloquean cuatro providers y exigen índices 0–3 primero, seguidos en orden ascendente al liberarlos.

El scroll real pausa nuevo trabajo, reprioriza y cancela solicitudes alejadas, conservando las filas aún visibles. Geometría inmóvil no reinicia el settle de 200 ms; movimiento horizontal, vertical o zoom sí. El evento final de un gesto activo sigue cerrándolo aunque repita el offset. Reciclaje invalida token/imagen. Después de la aclaración del usuario de que no hay parpadeo en su uso normal, no se añadió retención de imágenes para ese supuesto.

Se garantiza prioridad de **inicio** desde arriba. Cuatro providers concurrentes pueden acabar en otro orden; no se retiene una miniatura preparada esperando a otra fila.

[El análisis espacial de capturas](<../workspace-snapshot/payload/benchmarks/results/phase4/thumbnails-final07-images-summary/order.json>) agrupa cuatro filas de 13 posiciones. En .9 las diez aperturas completan las filas de arriba hacia abajo; cada fila superior está completa antes o en la misma captura que el primer resultado de la siguiente. En las diez aperturas la primera captura detectada ya incluye varias filas, con empate que atraviesa el cuarto resultado: no permite identificar los primeros cuatro individuales ni el orden dentro de una captura. En III ninguna de las diez aperturas tiene esos dos órdenes de filas; .6/.7/.8 sí los tienen 10/10. El análisis de CSV no demuestra por sí solo todas las imágenes esperadas ni identidad única; esas condiciones tienen la evidencia independiente de sesión y contratos.

## Finalización nativa y presupuesto

`STATask.RunPooledUntilCompleted` reutiliza el pool persistente. Una cancelación pendiente termina pronto y evita ejecutar el delegado. Desde el inicio atómico, la cancelación descarta el resultado pero espera el retorno nativo antes de liberar ocupación. `RunPooled` mantiene su contrato anterior de cancelación temprana. No se interrumpe un provider ni se trasladan objetos COM entre threads.

| Recurso | Presupuesto |
|---|---:|
| Imagen por viewport/página | 4 activos / 128 pendientes |
| Metadatos por viewport/página | 4 activos / 128 pendientes |
| Semáforo de generación existente | 8 |
| Pool STA compartido existente | 12 hilos |

Cuatro es el límite de la cola de imagen de viewport, sin presentarlo como límite global de todas las rutas Shell o retries.

## Contratos actuales

[Miniaturas/STA/decode .9](<../workspace-snapshot/payload/reports/phase4-thumbnail-contracts-firstvisit-ready.json>) pasa **60/60**. Enlaza BitmapHelper, FileThumbnailHelper, STATask y ViewportWorkQueue reales; extrae literalmente métodos Shell. Los casos cubren cache/fallback, DPI/proporción/header, invalidación, cuatro ocupaciones nativas, retry, diagnóstico opt-in, selección síncrona acotada y overlay separado. Dos cancelaciones tardías comprueban rechazo de overlay y shield tras llamada nativa/decode. Catorce casos añadidos comprueban referencia genérica exacta, ausencia de segunda aplicación Low y reaplicación conservadora tras nulidad/reemplazo/invalidación. Shell/GDI, WinUI dispatcher y decoder son dobles; headers PNG sintéticos no son rasters completos.

[Publicación .9](<../workspace-snapshot/payload/reports/phase4-publication-contracts-firstvisit-ready.json>) pasa **24/24** con colección real y métodos literales: snapshots iguales, prefijo 64→99, límite 128/129, reorder/remove/replacement/grouping, filtros, cancelaciones, semáforo y diagnóstico opt-in. El consumidor de filas es controlado, sin acreditar ausencia de reciclaje real WinUI.

[El índice focalizado](<../workspace-snapshot/payload/reports/phase4-focused-contracts-firstvisit-ready-index.json>) suma **168/168**: scheduling 21, miniaturas 60, publicación 24, caché de iconos 24, integración 26, fechas 8 y recursos 5. [Las regresiones reconstruidas](<../workspace-snapshot/payload/reports/phase4-regression-contracts-firstvisit-ready.json>) suman **159/159**. Total **327/327**, fuente `bf4390ee`, cero fallos y referencia interop build 08. No se suman las 233 pruebas .6,255 .7 ni 289 .8 históricas. Los [contratos de integración actuales](<../workspace-snapshot/payload/reports/phase4-viewport-integration-contracts-firstvisit-ready.json>) cubren fase 0 sin fase 3, Normal/Low, offsets/zoom, geometría inmóvil, cierre de gesto, disposal, recarga, handoff e índices superiores.

[Caché de iconos .9](<../workspace-snapshot/payload/reports/phase4-icon-cache-contracts-firstvisit-ready.json>) pasa **24/24**, enlazando IconCacheService completo y métodos BitmapHelper literales. Comprueba tamaño/DPI, opt-in y límites síncronos, 32 solicitudes compartidas de bytes/bitmap, Clear con cargas pendientes, null/fallo y acceso fuera del hilo UI. Codec/dispatcher/native provider son dobles; no acredita latencia real ni rendering WinUI.
El contrato de ocupación bloquea cuatro delegados STA reales, cancela viewport y encola reemplazos; no admite nuevas extracciones hasta liberar los delegados y comprueba máximo cuatro. El contrato de viewport exige completar imagen sin iniciar overlay. La ruta directa mantiene el contrato de imagen aplicada antes de acabar overlay. No se ejecutan builds ni contratos durante las comparaciones UI primarias.

## Capturas OFF comparables

Cada serie tiene diez aperturas, diagnóstico OFF, ningún build concurrente y misma ventana/detector. Todas completan 52/52 imágenes o 3/3 vídeos y cero timeouts. Percentiles interpolados:

| Snapshot | Primera imagen p50 / p95 ms | Las 52 p50 / p95 ms | Primera/todas vídeo p50 / p95 ms |
|---|---:|---:|---:|
| III 4.2.39.4 | 702,66 / 753,80 | 1.320,87 / 1.383,10 | Primera 305,46 / 349,68; todas 307,28 / 355,56 |
| IV .6 final 03, histórico | 753,55 / 876,11 | 1.247,09 / 1.587,00 | 271,86 / 314,23 |
| IV .7 final 05, superseded | 650,14 / 786,14 | 1.219,10 / 1.382,24 | 243,06 / 280,06 |
| IV .8 final 06, histórico | 541,54 / 589,82 | 708,90 / 796,27 | 197,32 / 225,74 |
| **IV .9 final 07, actual** | **524,18 / 631,03** | **659,79 / 733,35** | **233,58 / 284,60** |

[Resumen común de imágenes](<../workspace-snapshot/payload/benchmarks/results/phase4/thumbnails-final07-images-summary/summary.json>), [resumen de vídeos](<../workspace-snapshot/payload/benchmarks/results/phase4/thumbnails-final07-videos-summary/summary.json>), [sesión actual de imágenes](<../workspace-snapshot/payload/benchmarks/results/phase4/thumbnails-IV-off-final07-primary52/session.json>) y [vídeos](<../workspace-snapshot/payload/benchmarks/results/phase4/thumbnails-IV-off-final07-primary-videos/session.json>). III permanece intacta. La primera serie .6 con solo 17 rectángulos calibrados se conserva [excluida](<../workspace-snapshot/payload/benchmarks/results/phase4/thumbnails-IV-off-final03-images/exclusion.json>); no acredita viewport completo.

Frente a III, .9 reduce las medianas de primera imagen **178,48 ms (25,40%)** y todas **661,08 ms (50,05%)**. Frente a .7, baja **19,37%/45,88%**. Frente a .8, primera baja 3,20% y todas 6,93%, pero el p95 inicial sube 589,82→631,03 ms. Vídeo .9 tiene mediana **18,38% mayor que .8**, aunque menor que III/.7. Las diferencias observadas entre bloques no aíslan causalidad ni garantizan colas con n=10. La primera imagen de unos 524 ms sigue teniendo espera visible.

[El observador](<../workspace-snapshot/payload/tooling/Measure-Phase4Thumbnails.ps1>) verifica PID, ruta instalada y comienzo de proceso. Ventana **1.440×753**, rectángulos **56×56**, crop solo FileList. Reconoce patrón de cuatro colores con al menos 80% de 144 puntos y tolerancia RGB 55. No confunde icono genérico con imagen. La calibración espera cinco segundos tras recuento completo, fuera del tiempo medido, para obtener todos los bounds.

El reloj empieza antes de ValuePattern.SetValue y termina al recibir el buffer que coincide. UIA, PrintWindow, detección, polling y guardado permanecen incluidos. PrintWindow puede inducir render; no mide scanout. En .9 los intervalos medianos de primera/todas imágenes son **140,75/141,08 ms**; vídeo **91,01 ms**. Captura mediana de imágenes **20,85 ms**, vídeo **17,25 ms**. Los 25 ms solicitados de polling no son su resolución efectiva; no se resta estimación de overhead.

Caché **caliente/no controlada**: no se vacían cachés del sistema/app. Primera visita a una ruta no demuestra frío; INCACHEONLY miss acredita solo esa consulta. Patrones iguales no prueban identidad de archivo tras reciclaje. No existe serie de miniaturas Explorer con observador equivalente y no se afirma equivalencia con su rapidez.

[Las primeras visitas OFF .9](<../workspace-snapshot/payload/benchmarks/results/phase4/thumbnails-firstvisit-final07-summary/summary.json>) completan 52/52 en tres clones nuevos, cero timeouts: primera **617,52/491,41/566,58 ms**, todas **1.259,18/1.025,67/1.099,55 ms**. Las medianas de tres rutas son **566,58/1.099,55 ms**, frente a 722,26/1.286,59 ms en .8. Son diferencias observadas de 21,55%/14,54%, con una muestra por clone, sin baseline III/Explorer equivalente ni garantía de frío global. No hay percentiles de cola fiables ni mezcla con aperturas repetidas.
## Control ON actual .9

[El control sin captura ni readiness polling](<../workspace-snapshot/payload/reports/phase4-latency-diagnosis-final07-no-capture.json>) incluye operaciones 3/5, dos aperturas con 52 cache hits y 52 aplicaciones cada una. Primera aplicación desde Requested **590,26/226,14 ms**, última **772,70/549,61 ms**; espera inicial de pasada **198,58/188,17 ms**. Cache p50 **3,461/3,099 ms**, con máximo **345,58 ms** en la primera apertura y **4,25 ms** en la segunda. Decode p50 **0,222/0,189 ms**, máximo **2,709/0,4038 ms**. El resultado es mixto; no confirma que todos los decodes sean inferiores a 0,5 ms ni una mejora uniforme frente al control .8.

[La primera visita ON .9, operación 7](<../workspace-snapshot/payload/reports/phase4-latency-diagnosis-final07-firstvisit.json>), n=1 a rutas sintéticas nuevas, registra **52 cache misses, 52 generaciones y 52 aplicaciones**. Primera aplicación **558,62 ms**, última **1.398,67 ms**; pasada inicial espera **251,20 ms**. Provider nativo más conversión a bytes p50/p95/máximo **25,79/305,06/389,96 ms**. Decode p50/p95 **0,226/0,387 ms**, pero máximo **23,0963 ms**. El método registra duración hasta retorno; sin RowId ni decodeMode no identifica una cadena de la misma fila ni permite atribuir ese máximo a bloqueo síncrono del hilo UI. La caché global sigue sin controlarse; no se resta este reloj interno del observador OFF.

Los probes de render no aparecen en operaciones 5/7, lo que no demuestra ausencia de rendering o ViewChanged. Este control no mide presentación física, continuidad ni latencia de input. Los marcadores paralelos comparten OperationId; primeros hitos no se emparejan como una imagen individual. El control .9 propio sustituye el estado pendiente; los controles .6/.7/.8 se conservan históricos.
## Diagnóstico ON histórico y límites

[La traza .7 sin capturas](<../workspace-snapshot/payload/reports/phase4-latency-diagnosis-final05-no-capture.json>) contiene dos aperturas de medios: la pasada espera **190,94/226,50 ms**, cache inicial unos **3 ms** y primer decode **139,18/63,79 ms**. Publicación 64→99 con Reset y espera de SetSourceAsync motivaron .8. Son hitos de operación sin RowId; primeros eventos paralelos no se unen como una misma imagen. El control ON .8 y la primera visita se presentan a continuación.

[El control ON .8](<../workspace-snapshot/payload/reports/phase4-latency-diagnosis-final06-no-capture.json>) usa solo operaciones 7/9 de dos aperturas calientes en [la sesión exitosa](<../workspace-snapshot/payload/benchmarks/results/phase4/profile-IV-final06-latency-ready/session.json>), sin capturas ni readiness polling. Excluye startup 1 y operaciones 3/5 de un export fallido; la lectura concurrente se corrigió con FileShare.ReadWrite antes del snapshot válido. Los 104 decodes retornan entre **0,1316 y 0,4871 ms**, total **19,7117 ms** registrado. Este coste real de retorno WinUI para pequeñas miniaturas es breve, sin demostrar presentación de pixels en ese tiempo ni ausencia de bloqueo con otros providers/tamaños.

Se observan dos Reset iniciales y dos Appended de 35. En operaciones 7/9, primera aplicación desde Requested **215,90/196,93 ms**, última **424,19/439,09 ms**; espera inicial de pasada **177,54/164,75 ms**. La cola inicial aún domina parte importante de la espera. No se emparejan primeros eventos paralelos sin RowId ni se restan estos relojes internos de las capturas OFF.

[Las primeras visitas OFF](<../workspace-snapshot/payload/benchmarks/results/phase4/thumbnails-speed-final06-firstvisit-summary/summary.json>) a tres clones sintéticos completan **52/52** y cero timeouts. Primera imagen **753,79/722,26/576,63 ms**; todas **1.286,59/1.385,82/1.186,56 ms**. Es una muestra por ruta, sin baseline equivalente ni garantía de cache global fría. No se mezcla este n con aperturas repetidas ni se afirma instantaneidad para primera visita.

[La primera visita ON, operación 11](<../workspace-snapshot/payload/reports/phase4-latency-diagnosis-final06-firstvisit.json>), n=1 sin capturas/polling, registra 52 cache misses y 52 generaciones. Native provider más conversión a bytes p50/p95 **24,94/52,32 ms**, máximo **58,34 ms**; decode p50 **0,225 ms**, máximo **0,390 ms**. Primera aplicación **443,16 ms**, última **1.014,14 ms** desde Requested. Primer cache-complete **264,00 ms** y primer native-start **402,47 ms** difieren 138,47 ms. Sin RowId, esos primeros eventos no acreditan causalidad de una fila; junto con el recorrido de código motivan revisar el icono genérico anterior a la generación. La revisión .9 ya tiene build/327 contratos, capturas OFF y control ON propio descritos arriba; estos resultados .8 son históricos.

[El diagnóstico histórico .6](<../workspace-snapshot/payload/reports/phase4-latency-diagnosis-final03.json>) tiene dos aperturas de preparación/calibración y una asociada al CSV ON: realización→primer job **377,70–433,18 ms**. [Sus etapas](<../workspace-snapshot/payload/benchmarks/results/phase4/profile-IV-final03-thumbnail-stages-summary/summary.json>) registran 156 cache hits/decodes/aplicaciones, 52 por apertura. Cache p50/p95 **2,609/3,738 ms**, decode **11,446/40,861 ms**, overlay **1,790/2,645 ms**. Las etapas se solapan; no se suman como tiempo de carpeta ni se toman 156 eventos como 156 repeticiones. Diagnóstico .6 no acredita .9.

Los [fixtures edge](<../workspace-snapshot/payload/benchmarks/media-phase4-edge-manifest.json>) tienen EXIF 6, alpha, WebP, PDF y datos truncados/unsupported; se verificaron estructura y referencias sintéticas, sin probar sus providers en una serie UI comparable. También siguen sin serie gráfica comparable cambios de DPI/tamaño y recuperación por modificación. Contratos no prueban aspecto WinUI, input latency, continuidad de frames o presentación física. Se mantiene alcance experimental y no se afirma carga instantánea ni aceptación exhaustiva.

[La apertura final OFF](<../workspace-snapshot/payload/reports/phase4-final07-off-delivery-launch.json>) confirma paquete .9 exacto y PID **38424**, con ventana/control preparados; no mide tiempo de arranque.
