# Phase IV — estabilidad final

El candidato actual es **final 07 4.2.39.9**, fuente `bf4390ee0a2eed6cece0e99d1a021ce7fd255574`. Build 08: **0 errores, 88 advertencias, 1:52,50**. Pasan **327/327 contratos actuales**. Diez aperturas de 52 imágenes, diez de 3 vídeos, tres primeras visitas de 52 imágenes y cuarenta navegaciones OFF completan sin timeout. Sesenta incrementos OFF, veinte ciclos rápidos y control ON .9 están completos en su alcance; operaciones UI tienen siete Passed y cuatro Pending. Los snapshots .6/.7/.8 permanecen históricos, sin sumar sus pruebas ni trasladar sus resultados a .9.

Miniaturas repetidas .9: primera p50/p95 **524,18/631,03 ms**, todas 52 **659,79/733,35 ms**, frente a **702,66/753,80** y **1.320,87/1.383,10 ms en III**. Vídeo .9 mejora frente a III/.7 pero empeora frente a .8. Navegación 1.000 tiene mediana de contador 14,28% mayor que III; 10.000–100.000 tienen medianas menores. No se afirma carga instantánea, equivalencia con Explorer, mejora universal o aceptación exhaustiva.

## Compilación y paquete

| Snapshot | Resultado | Evidencia |
|---|---|---|
| Original | 0 errores, 88 advertencias, 2:57,41 | [Log](<../../../../../reports/phase4-original-managed-release-01-final-build.log>) |
| IV build 04, .6 final 03 histórico | 0 errores, 88 advertencias, 2:01,22 | [Log](<../../../../../reports/phase4-managed-release-04-final-build.log>) |
| IV build 05, .7 final 04 superseded | 0 errores, 89 advertencias, 2:12,76 | [Log](<../../../../../reports/phase4-managed-release-05-final-build.log>) |
| IV build 06, .7 final 05 superseded | 0 errores, 88 advertencias, 2:08,06 | [Log](<../../../../../reports/phase4-managed-release-06-final-build.log>) |
| IV build 07, .8 final 06 histórico | 0 errores, 88 advertencias, 1:57,01 | [Log](<../../../../../reports/phase4-managed-release-07-final-build.log>) |
| **IV build 08, .9 final 07 actual** | **0 errores, 88 advertencias, 1:52,50** | [Log](<../../../../../reports/phase4-managed-release-08-final-build.log>) |

[La provenance .9](<../workspace-snapshot/payload/reports/phase4-build-provenance-firstvisit.json>) confirma fuente limpia en `bf4390ee`, checksums Portable PDB de nueve documentos C# modificados, identidad CodeView y coincidencia de ensamblados build 08 con `managed-release-phase4-final07`. Manifiesto 4.2.39.9 y XAML tienen hashes separados. [La validación de contenido](<../workspace-snapshot/payload/artifacts/development-layout/managed-release-phase4-final07/package-validation.json>) identifica rutas, Server, arquitectura y dependencias. El MSIX de desarrollo es unsigned; content-validated-not-installed describe esa validación, no la ejecución posterior.

[La apertura OFF](<../workspace-snapshot/payload/reports/phase4-final07-off-launch.json>) confirma install location exacto y PID 28760; las sesiones conservan comienzo de proceso, una instancia experimental y ausencia de builds concurrentes. La apertura acredita ventana/control preparados, sin medir arranque ni scanout. Build y UI se coordinan secuencialmente conforme a source/AGENTS.md.

Build 05 añadió `CsWinRT1034` por cast ScrollViewer, corregido mediante metadata en `95fe8837`; build 06/07/08 vuelven a 88 advertencias. El registro final 02 previo mantuvo final 01 por compartir versión .5; la guarda rechazó medir ruta distinta y .6 resolvió el conflicto. Final 04.7 quedó superseded/excluido. Se conservan inventarios/logs. [El análisis intermedio de advertencias](<../workspace-snapshot/payload/reports/phase4-build-warning-review.json>) no es comparación final de builds limpios ni acredita ausencia de nuevos tipos en build 08.

## Contratos actuales

| Grupo | Resultado | Alcance y límite |
|---|---:|---|
| Scheduling | 21/21 | Cola real: capacidad, prioridad, cancelación, generaciones, dispatcher y comienzo superior. [Resultado](<../workspace-snapshot/payload/reports/phase4-viewport-contracts-firstvisit-ready.json>). |
| Miniaturas/STA/decode | 60/60 | Métodos/STA reales; Shell/GDI, dispatcher y codec controlados. Cache/fallback, DPI, decode acotado, overlay separado y referencia genérica vigente. [Resultado](<../workspace-snapshot/payload/reports/phase4-thumbnail-contracts-firstvisit-ready.json>). |
| Publicación | 24/24 | Colección real/métodos literales: igualdad, tail, fallback, filtros, cancelación y semáforo. Consumidor controlado. [Resultado](<../workspace-snapshot/payload/reports/phase4-publication-contracts-firstvisit-ready.json>). |
| Caché de iconos | 24/24 | Servicio completo/métodos BitmapHelper literales: DPI, límites, coalescing 32, Clear/generaciones, null/fault y acceso fuera de UI. Codec/native/dispatcher controlados. [Resultado](<../workspace-snapshot/payload/reports/phase4-icon-cache-contracts-firstvisit-ready.json>). |
| Integración fase 0/scroll/handoff | 26/26 | Métodos literales/cola real: prioridades, offsets/zoom, geometría, fin de gesto, recarga/recycling/disposal. [Resultado](<../workspace-snapshot/payload/reports/phase4-viewport-integration-contracts-firstvisit-ready.json>). |
| Fechas | 8/8 | Métodos/colección/formateador reales, recursos/containers controlados; 120 muestras administradas. [Resultado](<../workspace-snapshot/payload/reports/phase4-date-contracts-and-benchmark-firstvisit-ready.json>). |
| Recursos de búsqueda | 5/5 | Helper/bloque real: cancelación, excepción, timeout real 5 s y carreras; provider controlado. [Resultado](<../workspace-snapshot/payload/reports/phase4-search-resource-contracts-firstvisit-ready.json>). |
| Regresiones navegación/búsqueda | 159/159 | Path 37, directory 41, Shell 22, adaptive 17, nativeNTFS 28, recursiva/junction 3, sugerencias 11. [Índice](<../workspace-snapshot/payload/reports/phase4-regression-contracts-firstvisit-ready.json>). |

[El índice focalizado](<../workspace-snapshot/payload/reports/phase4-focused-contracts-firstvisit-ready-index.json>) suma 168; regresiones 159: **327/327**, cero fallos, rebuilds Release secuenciales sobre `bf4390ee`. Ambos registran interop build 08 SHA-256 `6C603E1118659393C9B17B722FE538CF83B2BEA51B9F0F5B7E35C17E8A6E4717`. No se suman 233 .6, 255 .7 ni 289 .8 históricos. Búsqueda nativa/recursiva usa fixture NTFS nuevo y junction controlada. Los índices conservan fuentes, DLL y logs; no se inventa un total ausente de advertencias de harnesses ni se presentan dobles como WinUI real.

## Invariantes

La publicación sin agrupación y con referencias/orden idénticos evita Reset. Tail de hasta 128 conserva filas mediante Add individual; reorder/remove/replacement/grouping/tail mayor mantiene bulk Reset. Fuente/filtro se capturan tras semáforo, se rechazan resultados obsoletos/cancelados y se conserva estado de carpeta. No se introduce AddRange incompatible con controles.

BitmapHelper y caché genérica solo seleccionan SetSource síncrono con opt-in/proporción, destino Physical 1–256, PNG/header reconocido de origen 1–256 por eje y hasta 256 KiB. Otros casos siguen SetSourceAsync. La cabecera no valida integridad PNG completa y fallo mantiene fallback. Se quita Phase 1 solo de dos PictureBorder, conservando x:Load, fases restantes y paneles virtualizantes.

El resultado inicial .9 conserva referencia al icono compartido ya aplicado; omite segunda aplicación Low solo con guardas vigentes y ReferenceEquals FileImage. Nulo/reemplazo/borrado/tamaño/DPI/ruta/modificación/generación/item/token/preferencia conserva reaplicación. No se añade otra caché de bytes o bitmap. Caché compartida mantiene coalescing, aislamiento al Clear y restricción de bitmap al hilo UI.

Viewport libera slot de imagen antes de overlay, atendido en metadata Low. Cancelación durante nativo/decode rechaza overlay/shield tardíos; rutas directas mantienen comportamiento. Handoff completado no repite imagen ante metadata expulsada. Fallos persistentes tienen un retry inmediato y después esperan interacción.

Fase 0/Normal solicita por índice ascendente; fase 3 refuerza y metadatos Low. Rango ausente también ordena realizados. Geometría inmóvil no prolonga settle; movimiento real conserva pausa, y final activo con offset repetido cierra gesto. Cuatro providers garantizan prioridad de inicio, sin imponer finalización. No se añadió retención por parpadeo que el usuario aclaró que no sucede en uso normal.

Navegación/disposal cancela y limpia antes de liberar resto; nativo iniciado conserva ocupación hasta retorno y pendiente cancelado no ejecuta. Cuatro slots corresponden al viewport, sin afirmar límite global Shell. Handle de búsqueda se entrega al caller a tiempo o se descarta una vez tras retorno tardío; cancelación no cierra mientras worker lo usa. Fechas realizan guards de rebinding/reentrada; formato/tab mantiene recorrido completo.

## Evidencia UI .9

| Escenario | Resultado | Evidencia y límite |
|---|---|---|
| Navegación OFF | 40 muestras, diez por tamaño, cero timeouts | [Sesión](<../workspace-snapshot/payload/benchmarks/results/phase4/navigation-IV-off-final07-raw/session.json>), [resumen](<../workspace-snapshot/payload/benchmarks/results/phase4/navigation-final07-summary/summary.json>). Texto/contador UIA, no pixels/final de layout. |
| Imágenes OFF repetidas | 10×52/52, cero timeouts | [Resumen](<../workspace-snapshot/payload/benchmarks/results/phase4/thumbnails-final07-images-summary/summary.json>). Captura PrintWindow/observador incluido. |
| Vídeos OFF repetidos | 10×3/3, cero timeouts | [Resumen](<../workspace-snapshot/payload/benchmarks/results/phase4/thumbnails-final07-videos-summary/summary.json>). Miniatura sintética, no reproducción/scanout. |
| Primera visita OFF | 3×52/52, cero timeouts | [Resumen](<../workspace-snapshot/payload/benchmarks/results/phase4/thumbnails-firstvisit-final07-summary/summary.json>). n=1 por clone, sin baseline frío equivalente. |
| Scroll OFF | 60 movimientos, cero timeouts | [Informe/CSV](<PHASE4_SCROLL.md>). Llamada ScrollPattern y observación posterior, sin FPS físico. |
| Ciclos rápidos OFF | 20×7 solicitudes, 20 confirmaciones finales, cero timeouts | [Sesión](<../workspace-snapshot/payload/benchmarks/results/phase4/rapid-IV-off-final07-20/session.json>). No acredita cada imagen durante la ráfaga. |
| Operaciones .9 | 7 Passed, 4 Pending | [Resultado](<../workspace-snapshot/payload/reports/phase4-app-interactions-final07.json>). Acciones y fixtures confirmados, sin aceptación exhaustiva. |
| Controles ON .9 | Dos aperturas hit y una primera visita miss | [Control](<../workspace-snapshot/payload/reports/phase4-latency-diagnosis-final07-no-capture.json>), [primera visita](<../workspace-snapshot/payload/reports/phase4-latency-diagnosis-final07-firstvisit.json>). Hitos internos/método real, sin scanout. |

Las primeras visitas .9 detectan primera imagen **617,52/491,41/566,58 ms** y todas **1.259,18/1.025,67/1.099,55 ms**. Medianas de tres rutas **566,58/1.099,55 ms** frente .8 **722,26/1.286,59 ms**. Un n pequeño, sin cache global fría garantizada, impide garantía de colas o extrapolación. No se combina con aperturas repetidas ni demuestra equivalencia Explorer.

[El orden .9](<../workspace-snapshot/payload/benchmarks/results/phase4/thumbnails-final07-images-summary/order.json>) observa filas completas superior→inferior 10/10 y cada fila superior antes/en misma captura que primera de la siguiente. Primer sondeo ya contiene varias filas y ties en las diez aperturas: no identifica primeros cuatro resultados individuales ni orden dentro de captura. La identidad de archivos sintéticos iguales tiene guardas independientes.

## Operaciones y control ON actuales .9

[Las operaciones .9](<../workspace-snapshot/payload/reports/phase4-app-interactions-final07.json>) pasan selección/multiselección, copy/paste, cut/paste, rename, back/forward y toggle de preview restaurado; archivos y hashes corroboran copy/cut/rename. Toggle verifica estado, sin contenido decodificado. Create_folder, new_tab, sort y layout quedan Pending porque no se inspeccionó una ruta UIA completa para confirmarlas. No se incluyen como aprobadas.

[Las veinte ráfagas .9](<../workspace-snapshot/payload/benchmarks/results/phase4/rapid-IV-off-final07-20/session.json>) completan 140 solicitudes y veinte confirmaciones finales; privada **469,27→358,61 MiB**, máximo **478,60 MiB**. La observación finita no prueba ausencia de fuga ni estado de cada imagen intermedia.

[ON .9 operaciones 3/5](<../workspace-snapshot/payload/reports/phase4-latency-diagnosis-final07-no-capture.json>): 52 hits/aplicaciones cada una, primera aplicación **590,26/226,14 ms**, última **772,70/549,61 ms** desde Requested; decode p50 **0,222/0,189 ms**, máximo **2,709/0,4038 ms**. Cache máximo 345,58/4,25 ms; pasada inicial espera 198,58/188,17 ms. Son resultados mixtos y no se afirma mejora uniforme frente a .8.

[Primera visita ON .9 operación 7](<../workspace-snapshot/payload/reports/phase4-latency-diagnosis-final07-firstvisit.json>): 52 miss/native/aplicaciones, primera **558,62 ms**, última **1.398,67 ms**; provider+conversión p50/p95/máximo **25,79/305,06/389,96 ms**. Decode p50/p95 **0,226/0,387 ms**, máximo **23,0963 ms**. Sin RowId ni decodeMode no se atribuye ese máximo a bloqueo UI ni se forman cadenas individuales de los primeros eventos. Probes ausentes en operaciones 5/7 no prueban ausencia de render/ViewChanged. n=1, frío global no garantizado; no se combina con capturas OFF.

[La apertura final OFF](<../workspace-snapshot/payload/reports/phase4-final07-off-delivery-launch.json>) confirma **PID 38424**, paquete .9 exacto, ventana/control preparados. No mide arranque ni presentación física.
## Evidencia histórica .8

[Las veinte ráfagas .8](<../workspace-snapshot/payload/benchmarks/results/phase4/rapid-IV-off-final06-20/session.json>) completan 140 solicitudes y 20 confirmaciones finales; memoria privada 396,13→350,78 MiB, sin probar fuga o cada imagen durante ráfaga. [Sesenta incrementos .8](<PHASE4_SCROLL.md>) observan movimiento sin timeout. No acreditan .9.

[Las operaciones .8](<../workspace-snapshot/payload/reports/phase4-app-interactions-final06.json>) tienen siete Passed: selección/multiselección, copy/paste, cut/paste, rename, back/forward y toggle de preview restaurado; archivos/hash corroboran copy/cut/rename. Toggle no acredita contenido decodificado. Cuatro Pending: create_folder,new_tab,sort,layout, por no tener ruta UIA completa inspeccionada. Se mantienen históricas; la repetición .9 tiene su evidencia propia arriba.

[El control ON caliente .8](<../workspace-snapshot/payload/reports/phase4-latency-diagnosis-final06-no-capture.json>) incluye operaciones 7/9 de una sesión exitosa, sin captura/readiness polling. 104 retornos de decode 0,1316–0,4871 ms, total 19,7117 ms. Primera aplicación 215,90/196,93 ms, última 424,19/439,09 ms; espera pasada 177,54/164,75 ms. Son duración de método real, sin scanout o continuidad. Startup 1/export fallido 3/5 quedan excluidos; lectura concurrente se corrigió con FileShare.ReadWrite antes del snapshot válido.

[Primera visita ON .8 operación 11](<../workspace-snapshot/payload/reports/phase4-latency-diagnosis-final06-firstvisit.json>), n=1,52 miss/52 native: provider+conversión p50/p95 **24,94/52,32 ms**, máximo 58,34; decode p50 0,225/máximo 0,390 ms; primera aplicación 443,16 y última 1.014,14 ms. Primer cache-complete 264,00 y native-start 402,47 ms difieren 138,47 sin RowId para causalidad individual. Recorrido de código y esta espera motivan .9, cuyo control propio se presenta arriba.

[Las trazas .7](<../workspace-snapshot/payload/reports/phase4-latency-diagnosis-final05-no-capture.json>) y [.6](<../workspace-snapshot/payload/reports/phase4-latency-diagnosis-final03.json>) siguen históricas. [Render ON común .6](<../workspace-snapshot/payload/reports/phase4-render-common-increments.json>) tiene p95 XAML menor pero intervalos de callbacks sin mejora; no acredita continuidad .9.

## Límites de aceptación

La entrega permanece experimental con recuperación a III. No hay medida física de FPS/scanout, input latency ni equivalencia de miniaturas Explorer. Grouping/sort/layout, carpeta nueva, tabs/ventanas, providers preview, UI de búsqueda, drag/drop, nube/red, comprimidos, permisos y variantes completas de operaciones siguen sin aceptación exhaustiva. EXIF/alpha/WebP/PDF/corruptos y cambios DPI/tamaño requieren observación real de providers. Fixtures y contratos no garantizan ausencia total de regresiones, pérdida de datos o fallos UI.
