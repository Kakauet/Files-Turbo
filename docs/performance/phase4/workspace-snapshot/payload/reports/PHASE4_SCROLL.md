# Phase IV — desplazamiento, XAML y trabajo diferido

El candidato actual **final07 4.2.39.9**, fuente `bf4390ee0a2eed6cece0e99d1a021ce7fd255574`, conserva virtualización: Detalles usa ItemsStackPanel y cuadrícula ItemsWrapGrid. Build 08: **0 errores, 88 advertencias, 1:52,50**; [provenance](phase4-build-provenance-firstvisit.json) y [apertura OFF exacta](phase4-final07-off-launch.json), PID 28760, identifican la ejecución primaria. Pasan **327/327 contratos** y **60/60 incrementos OFF actuales** observan movimiento, cero timeouts. Estas llamadas no acreditan FPS físico ni fluidez de wheel/arrastre real. Los controles ON .9 están completos en su alcance y tienen resultados mixtos; los snapshots .6/.7/.8 son históricos.

## Arquitectura y cambio

Phase III compartía imágenes, overlays y metadatos en cuatro trabajos por página. El orden de callbacks XAML y la preferencia por peticiones recientes podían iniciar filas inferiores. El scroll cancelaba la cola entera, incluso filas todavía visibles, y cancelar la espera STA liberaba ocupación lógica antes del retorno nativo.

Phase IV separa cuatro trabajos de imagen y cuatro de metadatos, cada etapa con hasta 128 pendientes; conserva el pool STA de doce hilos. Una extracción iniciada mantiene ocupación hasta terminar. El viewport conserva filas aún visibles y cancela las alejadas; durante el gesto pausa nuevos trabajos. Movimiento horizontal/vertical/zoom mantiene settle de 200 ms, mientras geometría inmóvil no lo inicia ni prolonga. Un final de gesto activo con offset repetido sigue cerrándolo.

Fase 0 solicita pasada coalescida Normal y fase 3 refuerza. Imágenes y aplicación usan Normal; metadatos Low. Rango visible y fallback de realizados se ordenan por índice ascendente, con FIFO en empates. Recarga pausa ambas etapas incluso antes de crear las colas. Cuatro providers concurrentes pueden completar en otro orden: se garantiza prioridad de inicio, sin retener una imagen preparada artificialmente.

Desde .8 se mantienen snapshots idénticos sin Reset y se publica un tail de hasta 128 filas mediante Add individuales, solo con referencias y prefijo sin agrupación exactamente iguales. Reorder/remove/replacement/grouping/tail mayor conserva Reset. Se eliminan dos compuertas Phase 1 de PictureBorder Grid/Cards y se desplaza el overlay a metadatos Low para liberar slots de imagen. Se conservan x:Load, otros elementos por fases y paneles virtualizantes.

El decode síncrono solo se permite con opt-in, proporción, destino Physical 1–256, PNG de origen 1–256 en ambos ejes y hasta 256 KiB; otros casos siguen asíncronos. .9 extiende el decode físico acotado a iconos genéricos compartidos y omite su segunda aplicación Low únicamente si el resultado inicial sigue vigente y FileImage es la misma referencia. Nulidad/reemplazo/invalidez conserva reaplicación. No crea otra caché. La cota reduce el trabajo admitido, sin garantizar que un método nunca bloquee un frame.

Guardas de item, ruta, modificación, DPI, tamaño, token y generación rechazan resultados de otra fila. Reciclaje y navegación cancelan su vida. Cada fallo persistente tiene un retry inmediato y luego espera otra interacción. No se añadió retención por parpadeo después de aclarar el usuario que no sucede en uso normal.

## Incrementos OFF actuales .9

[Las sesiones de 10.000](../benchmarks/results/phase4/scroll-IV-off-final07-increments-10000/session.json), [50.000](../benchmarks/results/phase4/scroll-IV-off-final07-increments-50000/session.json) y [100.000 entradas](../benchmarks/results/phase4/scroll-IV-off-final07-increments-100000/session.json) están Completed, diagnóstico OFF, PID 28760 y paquete .9 exactos, sin build concurrente. Cada una ejecuta diez SmallIncrement y diez LargeIncrement; los sesenta registran Moved=true y cero timeouts. Percentiles con interpolación lineal desde CSV:

| Entradas | SmallIncrement llamada p50 / p95 / máximo ms | LargeIncrement llamada p50 / p95 / máximo ms |
|---|---:|---:|
| 10.000 | 0,730 / 3,069 / 4,140 | 0,723 / 2,073 / 2,653 |
| 50.000 | 0,769 / 1,092 / 1,108 | 0,829 / 1,012 / 1,036 |
| 100.000 | 0,640 / 0,930 / 0,969 | 0,618 / 0,826 / 0,918 |

CSV: [10.000](../benchmarks/results/phase4/scroll-IV-off-final07-increments-10000/samples.csv), [50.000](../benchmarks/results/phase4/scroll-IV-off-final07-increments-50000/samples.csv), [100.000](../benchmarks/results/phase4/scroll-IV-off-final07-increments-100000/samples.csv). CallMs mide la llamada al proveedor ScrollPattern; lectura posterior de posición y ObservationMs se conservan aparte. Una llamada inferior al presupuesto orientativo de un frame no demuestra render ni presentación en ese tiempo. El escenario es listado, sin wheel físico, arrastre de scrollbar, saltos extremos ni scroll de medios.

La .9 completa [cuarenta navegaciones OFF](../benchmarks/results/phase4/navigation-IV-off-final07-raw/session.json), veinte aperturas repetidas de medios y tres primeras visitas de imágenes. [PHASE4_PERFORMANCE.md](PHASE4_PERFORMANCE.md) documenta las diferencias mixtas: contador 1.000 tiene mediana 14,28% mayor que III, mientras 10.000–100.000 tienen medianas menores. El [orden espacial .9](../benchmarks/results/phase4/thumbnails-final07-images-summary/order.json) observa filas completas superior→inferior 10/10, con empates de captura que impiden ordenar resultados individuales.

[Los veinte ciclos rápidos .9](../benchmarks/results/phase4/rapid-IV-off-final07-20/session.json) completan 140 solicitudes y 20 confirmaciones finales, cero timeouts. Privada 469,27→358,61 MiB, máximo 478,60 MiB: observación finita, sin prueba de ausencia de fuga o de cada imagen intermedia. [La apertura final OFF](phase4-final07-off-delivery-launch.json), PID 38424, confirma ventana/control preparados y paquete .9 exacto, sin medir arranque.

## Contratos actuales

[Scheduling 21/21](phase4-viewport-contracts-firstvisit-ready.json), [integración 26/26](phase4-viewport-integration-contracts-firstvisit-ready.json), [miniaturas/STA/decode 60/60](phase4-thumbnail-contracts-firstvisit-ready.json), [publicación 24/24](phase4-publication-contracts-firstvisit-ready.json) y [caché de iconos 24/24](phase4-icon-cache-contracts-firstvisit-ready.json) se reconstruyeron secuencialmente para .9 y build 08. Enlazan cola, colección, servicio y métodos de producción: ocupación nativa, capacidad, reciclaje, recarga, handoff, retry, fase 0 sin fase 3, prioridades Normal/Low, geometría inmóvil, offsets/zoom, final de gesto, referencia genérica vigente y disposal. Callbacks inversos/mezclados con rango ausente comienzan por índices 0–3. Publicación conserva referencias consumidoras ante tail y mantiene fallback.

[El índice focalizado .9](phase4-focused-contracts-firstvisit-ready-index.json) tiene 168 aprobados; [regresiones](phase4-regression-contracts-firstvisit-ready.json), 159: **327/327**, cero fallos. Son invariantes con Shell/GDI, dispatcher/decoder y contenedores controlados, sin prueba de render WinUI ni input real. Se preservan paneles virtualizantes y fases restantes conforme a [la guía oficial](https://learn.microsoft.com/en-us/windows/apps/develop/performance/optimize-gridview-and-listview). Las consultas Shell permanecen fuera del hilo UI; pueden acceder a disco incluso al pedir cache: [IShellItemImageFactory.GetImage](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitemimagefactory-getimage).

## Control ON actual y alcance gráfico

[ON .9 operaciones 3/5](phase4-latency-diagnosis-final07-no-capture.json), sin capturas/readiness polling: 52 cache hits/aplicaciones cada una, primera aplicación 590,26/226,14 ms y última 772,70/549,61 ms desde Requested. Decode p50 0,222/0,189 ms, máximos 2,709/0,4038 ms; pasada inicial espera 198,58/188,17 ms. [Primera visita ON .9 operación 7](phase4-latency-diagnosis-final07-firstvisit.json), n=1: 52 miss/generaciones/aplicaciones, primera 558,62 y última 1.398,67 ms; provider+conversión p50/p95/máximo 25,79/305,06/389,96 ms; decode p50/p95 0,226/0,387 ms, máximo 23,0963 ms.

Son resultados mixtos. Sin RowId ni decodeMode no se forman cadenas individuales ni se atribuye el máximo a bloqueo UI síncrono. Probes de render ausentes en operaciones 5/7 no prueban ausencia de render/ViewChanged. Método/hitos no miden continuidad, scanout ni latencia de input. No se confirma que todos los decodes .9 sean inferiores a 0,5 ms. [PHASE4_THUMBNAILS.md](PHASE4_THUMBNAILS.md) detalla sus límites.

El inventario Intel informa 1920×1080 a 144 Hz, presupuesto orientativo **6,94 ms**. No acredita monitor exacto ni frecuencia física de presentación. No se inició captura global/elevada WPR; se conservan EventPipe y callbacks opt-in. RenderedEventArgs.FrameDuration describe render XAML. Intervalos entre callbacks, espera de dispatcher, llamadas UIA y pausas GC se analizan separados y no se convierten en FPS.

## Datos históricos

[El perfil III, PID 37640](../benchmarks/results/phase4/baseline-III-profile/analysis-pid37640) dura 55,132 s: 377.959 muestras de pilas, 21 suspensiones emparejadas, 19 GC, cero eventos perdidos/truncados. Parte del hilo UI acaba en frontera nativa IApplicationStatics.Start; no se cuenta como CPU ocupada ni se atribuye a una función administrada. [La segmentación](../benchmarks/results/phase4/baseline-III-profile/analysis-segmented.json) solo observa reposo de 12,676 s hasta el final; no usa 85,479 s solicitados. Las 36 muestras de UpdateDateDisplay entre 6.393 de reposo no prueban coste dominante; pilas con esperas no son porcentajes de CPU.

[La comparación ON común .6](phase4-render-common-increments.json) usa ocho llamadas Small/Large por variante; ventanas UIA petición→readiness más 250 ms, sin preparaciones y con solapamientos deduplicados. Una sesión III y una .6, con costes de diagnóstico/UIA/profiler:

| Métrica | III n; p50 / p95 / máximo ms | IV .6 n; p50 / p95 / máximo ms |
|---|---:|---:|
| Render XAML | 34; 0,252 / 20,302 / 34,279 | 36; 0,305 / 10,017 / 23,743 |
| Intervalo entre callbacks | 33; 6,930 / 115,225 / 128,159 | 35; 7,003 / 115,859 / 169,126 |
| Espera probe dispatcher | 11; 0,132 / 17,690 / 26,963 | 11; 0,470 / 9,961 / 18,842 |

Tres duraciones XAML superan 6,94 ms en cada variante. Aunque p95 XAML/dispatcher disminuye, intervalos entre callbacks son mayores y máximo empeora. No respaldan mejora de continuidad ni acreditan .9. Los n son callbacks, no repeticiones independientes.

[Incrementos OFF .8](../benchmarks/results/phase4/scroll-IV-off-final06-increments-100000/session.json) y [.6](../benchmarks/results/phase4/scroll-IV-off-final03-increments-100000/session.json) siguen históricos. [ON .8 caliente](phase4-latency-diagnosis-final06-no-capture.json) observa 104 retornos de decode 0,1316–0,4871 ms, sin continuidad física. [Primera visita ON .8](phase4-latency-diagnosis-final06-firstvisit.json) motivó la revisión .9, sin constituir su validación. Los saltos anteriores [Original 100.000](../benchmarks/results/phase4/scroll-Original-off-raw-c-100000/session.json) y [III 10.000](../benchmarks/results/phase4/scroll-III-off-raw-verified01-10000/session.json) quedaron incompletos al exigir extremos exactos; se conservan excluidos, sin atribuir el fallo a código.
