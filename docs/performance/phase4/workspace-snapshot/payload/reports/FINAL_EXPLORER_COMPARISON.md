# Comparación de exploradores — Phase IV

11 de octubre de 2026. **IV final07, 4.2.39.9**, fuente **bf4390ee0a2eed6cece0e99d1a021ce7fd255574**, está registrado y medido en navegación, miniaturas, scroll, resistencia e interacciones. Build08, provenance y aperturas OFF están verificados; pasan **327/327 contratos**. Perfil ON se conserva separado con resultados mixtos. La entrega termina en PID 38424 con diagnóstico OFF; el tag local files-turbo-phase4 está previsto y pendiente de verificación. Las series .6/.7/.8 permanecen históricas y no se atribuyen a .9.

## Variantes y condiciones

| Variante | Fuente y ejecución |
|---|---|
| Files original | 1994d9a4269f5144c70bc67d6a48de4a20616912, Managed Release x64; assemblies originales con identidad/protocolo experimental 4.2.39.0. |
| Turbo III | 1b66432ad3baf122aa4e608b67ea47a5401a78ed, managed-release-phase3-stable06, 4.2.39.4. |
| IV .6 final03, histórico | 6e9a7f9; navegación OFF 40 muestras y miniaturas OFF 10 por contenido. |
| IV .7 final05, histórico | 95fe883; miniaturas propias medidas. Final04/e9035aed nunca se registró o midió por advertencia adicional de metadata corregida en final05. |
| **IV .8 final06, referencia medida** | **081841c9, build07, 0 errores, 88 advertencias, 1:57,01**. Provenance de ocho C#, registro y OFF exacto PID 36356. |
| **IV .9 final07, medido** | **bf4390ee, build08, 0 errores, 88 advertencias, 1:52,50**. Provenance de nueve C#, registro y OFF exacto PID 28760. 327 contratos, navegación y miniaturas verificadas. |
| Windows Explorer† | Serie COM/UIA revisada: 40 muestras, diez por tamaño, cero timeouts. El observador inicial fallido queda excluido. |

[phase4-original-package-provenance.json](phase4-original-package-provenance.json) documenta la adaptación del Original. No añade optimizaciones Turbo ni reemplaza Files original instalado. Los helpers administrados de nueva ventana del Original conservan files-dev; esa acción se excluye.

Equipo: Windows 11 Pro 10.0.26200, i7-13620H, 10 núcleos/16 procesadores lógicos y 34.048.823.296 bytes de RAM. El adaptador Intel informa 1920×1080 a 144 Hz; RTX4060 no expone modo activo en el inventario. No se midió el monitor exacto ni presentación física; alimentación y perfil energético no quedaron registrados. No se cambiaron ajustes de sistema ni se vaciaron cachés globales.

Original, III y .9 usan synthetic/1000, 10000, 50000 y 100000, ventana 1440×753 y proceso experimental verificado por ruta/PID, diagnóstico OFF. Original tiene cinco repeticiones por tamaño; III y .9, diez. Bloques separados, sin alternancia equilibrada y con cachés calientes/no controladas. IV midió imágenes y vídeos antes de navegar; la historia del proceso difiere. El preflight y las sesiones registran ausencia de build concurrente, sin afirmar muestreo continuo del sistema.

El observador común Files usa RawView UIA cacheado y ItemName dentro del viewport; incluye discovery del setter, consultas y sondeo. **Primera fila accesible no equivale al primer fotograma físico; contador completo no equivale a layout finalizado.** Percentiles interpolados en posición (n−1)×p, CSV es-ES. Con n=5/10, p95 describe esta muestra y no garantiza colas futuras.

Explorer usa COM Navigate2 y reacquiere peers de su ventana; Files usa ValuePattern y peers cacheados. Se igualan Details y límites externos 26;26;1440;753, pero no viewport/columnas internos píxel a píxel. Explorer exige nombre del fixture, IsOffscreen=false y rectángulo positivo que intersecte la ventana. Sus cifras † son observables de otro método: **no se calculan ratios o superioridad entre aplicaciones**.

## Tabla de observables medidos

Tiempos en ms, **p50 / p95**. IV corresponde a .9 final07; Explorer† tiene método distinto.

| Métrica | Files original | Turbo III | IV .9 final | Explorer† |
|---|---:|---:|---:|---:|
| Primera fila accesible, 10k | 255,33 / 469,47 | 268,73 / 483,55 | 226,59 / 287,43 | 191,74 / 211,98† |
| Primera fila accesible, 100k | 254,06 / 539,73 | 248,17 / 605,80 | 231,95 / 282,27 | 417,88 / 457,27† |
| Contador accesible completo, 10k | 458,95 / 1.082,56 | 292,28 / 503,70 | 226,90 / 289,14 | 184,94 / 203,84† |
| Contador accesible completo, 100k | 3.548,98 / 3.619,72 | 1.201,27 / 1.639,20 | 1.156,09 / 1.272,99 | 411,27 / 450,41† |
| 52 miniaturas de imagen, superficie capturada | No medido | 1.320,87 / 1.383,10 | 659,79 / 733,35 | No medido |
| CPU de navegación 100k, ms del proceso | 8.750,00 / 8.940,62 | 2.578,12 / 3.362,50 | 2.257,81 / 2.525,78 | No medido |
| Scroll p95 de fotogramas físicos | No medido | No medido | No medido | No medido |
| Búsqueda escrita→resultado visible | No medido | No medido | No medido | No medido |
| RAM comparable en reposo | No medido | No medido | No medido | No medido |

CPU es tiempo acumulado del proceso durante la muestra, puede exceder tiempo de pared por varios hilos y no es porcentaje de uso o reposo. † Explorer mide nombre accesible y estado desde COM, incluyendo reacquisición y sondeo.

## Navegación Files con observador común

Valores **p50 / p95 [mínimo–máximo]**, ms. Todas las muestras incluidas completaron sin timeout; .9 tiene 40, diez por tamaño.

| Entradas | Primera fila Original | Primera fila III | Primera fila IV .9 |
|---|---:|---:|---:|
| 1.000 | 337,23 / 465,50 [220,49–465,91] | 198,94 / 254,78 [179,86–255,98] | 230,54 / 270,10 [192,53–279,61] |
| 10.000 | 255,33 / 469,47 [222,35–521,42] | 268,73 / 483,55 [186,03–552,38] | 226,59 / 287,43 [207,62–323,43] |
| 50.000 | 242,21 / 512,16 [205,93–537,26] | 270,48 / 425,81 [214,55–471,07] | 238,02 / 288,82 [180,18–297,08] |
| 100.000 | 254,06 / 539,73 [203,23–564,77] | 248,17 / 605,80 [207,60–638,23] | 231,95 / 282,27 [184,35–294,96] |

| Entradas | Contador completo Original | Contador completo III | Contador completo IV .9 |
|---|---:|---:|---:|
| 1.000 | 370,90 / 471,76 [231,26–473,05] | 202,86 / 282,94 [180,15–305,33] | 231,83 / 271,11 [192,95–280,96] |
| 10.000 | 458,95 / 1.082,56 [362,12–1.201,63] | 292,28 / 503,70 [188,72–552,65] | 226,90 / 289,14 [208,28–323,77] |
| 50.000 | 2.028,53 / 2.372,98 [1.870,59–2.454,48] | 727,24 / 906,50 [660,14–959,75] | 722,77 / 812,65 [569,83–825,14] |
| 100.000 | 3.548,98 / 3.619,72 [3.424,09–3.627,41] | 1.201,27 / 1.639,20 [1.078,82–1.869,97] | 1.156,09 / 1.272,99 [1.066,38–1.277,09] |

**La mejora frente a III no es universal.** La mediana del contador .9 disminuye **22,37% en 10k**, **0,61% en 50k** y **3,76% en 100k**, pero aumenta **14,28% en 1k**. La primera fila 1k también tiene mediana mayor (230,54 frente a 198,94 ms). Frente al Original, la mediana del contador 100k es **67,42% menor**. Son diferencias observadas entre bloques no alternados, sin atribuir causalidad a una optimización aislada o garantizar cada uso futuro.

Memoria del proceso al finalizar navegación 100k, **MiB p50 / p95**:

| Memoria | Original | III | IV .9 |
|---|---:|---:|---:|
| Privada | 559,68 / 582,60 | 412,04 / 459,20 | 440,49 / 469,91 |
| Working set | 507,73 / 530,01 | 589,19 / 634,15 | 635,47 / 664,26 |

La privada y working set .9 superan III en este bloque; la privada sigue por debajo del Original, pero el working set lo supera. Cachés, media previa y accesibilidad difieren: no demuestra reposo, fuga, ahorro global ni un efecto causal del código. Los valores de CPU/memoria se derivan de las diez filas 100k de samples.csv mediante interpolación lineal. Los process-summary.csv vacíos no se utilizan como fuente.

Evidencia: [resumen común .9](../benchmarks/results/phase4/navigation-final07-summary/summary.json), [Original](../benchmarks/results/phase4/navigation-Original-off-raw-a/session.json), [III](../benchmarks/results/phase4/navigation-III-off-raw-verified01/session.json) y [.9](../benchmarks/results/phase4/navigation-IV-off-final07-raw/session.json). navigation-Original-off-a y navigation-Original-off-common-a se excluyeron por observadores incorrectos.

La .8 histórica tiene contador 100k p50/p95 1.307,59/1.598,81 ms y primera fila 266,98/412,97 ms; privada 412,73/450,60 MiB y working set 604,01/642,25 MiB. La .6 tuvo contador 100k 1.141,71/1.272,35 ms y primera fila 233,93/401,98 ms; privada 447,01/484,43 MiB y working set 642,71/679,71 MiB. Se conservan navigation-IV-off-final06-raw y navigation-IV-off-final03-raw. La diferencia de memoria entre esos bloques no constituye una prueba controlada de retención o fuga.

## Miniaturas repetidas y orden vertical

Cada serie primaria completó diez aperturas de **52/52 imágenes** o **3/3 vídeos H264**, sin timeout, diagnóstico OFF y sin build concurrente. El detector reconoce el patrón de cuatro colores dentro de los mismos rectángulos Picture; un icono genérico no cuenta. .9 corresponde al PID 28760; .8 al PID 36356.

| Snapshot | Primera imagen p50/p95 [min–max] | 52 imágenes p50/p95 [min–max] | Vídeo: todas p50/p95 [min–max] |
|---|---:|---:|---:|
| III | 702,66 / 753,80 [641,33–757,09] | 1.320,87 / 1.383,10 [1.308,33–1.386,60] | 307,28 / 355,56 [260,12–363,43] |
| .6 final03, histórico | 753,55 / 876,11 [661,18–876,14] | 1.247,09 / 1.587,00 [1.169,66–1.643,49] | 271,86 / 314,23 [222,99–315,80] |
| .7 final05, histórico | 650,14 / 786,14 [572,72–788,94] | 1.219,10 / 1.382,24 [1.099,04–1.454,14] | 243,06 / 280,06 [196,18–281,28] |
| .8 final06, histórico | 541,54 / 589,82 [534,28–604,58] | 708,90 / 796,27 [671,22–810,55] | 197,32 / 225,74 [163,13–227,30] |
| **.9 final07, actual** | **524,18 / 631,03 [432,94–652,96]** | **659,79 / 733,35 [565,80–739,92]** | **233,58 / 284,60 [181,31–292,31]** |

En III la primera miniatura de vídeo es 305,46/349,68 ms; en los snapshots IV primera y todas coinciden en una captura. No se infiere orden de terminación individual entre capturas.

Frente a III, **.9 reduce la mediana de primera imagen 25,40% y de las 52 visibles 50,05%**; el p95 completo disminuye **46,98%**. Frente a .7, las medianas disminuyen 19,37%/45,88%. Vídeo completo .9 disminuye **23,98% frente a III**, pero **aumenta 18,38% frente a .8**. Su p95 de primera imagen también supera .8 (631,03 frente a 589,82 ms). La mejora no es monótona ni universal; cada diferencia pertenece al fixture y a bloques calientes/no controlados.

PrintWindow puede pedir render y perturbarlo. UIA, captura, detector, polling y guardado permanecen incluidos; no se resta su coste. Patrones compartidos no prueban identidad individual de archivo; hay contratos separados de vigencia/reciclaje. No son píxeles físicos ni FPS. III calibró conteo más un segundo; IV usó cinco para estabilizar las mismas 52 geometrías. La preparación adicional no está cronometrada y puede calentar caché. No se purgó caché global.

[El análisis espacial .9](../benchmarks/results/phase4/thumbnails-final07-images-summary/order.json) agrupa Y con tolerancia 2 px. .9 y .8 completan filas superior→inferior en **10/10**; cada fila superior termina antes o en la misma captura que el inicio inferior. III tiene 0/10 en esas relaciones. En .9/.8 la primera captura incluye la fila superior **y otras filas en 10/10**; hay empate en el límite de los primeros cuatro registros en 10/10, así que no se identifican los primeros cuatro resultados físicos. .6/.7 tenían primera captura solo superior 10/10; esa propiedad histórica no se traslada a .8/.9. Cuatro proveedores concurrentes conservan orden de inicio por índice/FIFO sin imponer orden artificial de finalización.

Evidencia: [imágenes .9 e históricos](../benchmarks/results/phase4/thumbnails-final07-images-summary/summary.json), [vídeos .9 e históricos](../benchmarks/results/phase4/thumbnails-final07-videos-summary/summary.json) y first-seen.csv. Se conservan el control ON n=1 y la serie .6 con solo 17 geometrías excluida mediante exclusion.json; no se usan como primarias. Las capturas .6 siguen en thumbnails-IV-off-final03-primary52 y primary-videos.

## Primera visita y diagnóstico sin capturas

Tres rutas nuevas .9, una apertura por ruta, completan **52/52** sin timeout: primera **617,52/491,41/566,58 ms**, todas **1.259,18/1.025,67/1.099,55 ms**. .8 tenía primera 753,79/722,26/576,63 ms y todas 1.286,59/1.385,82/1.186,56 ms. La mediana de tres rutas pasa de 722,26 a 566,58 ms para primera, y de 1.286,59 a 1.099,55 ms para todas. [El resumen separado](../benchmarks/results/phase4/thumbnails-firstvisit-final07-summary/summary.json) conserva cada n=1: rutas distintas, bloques no alternados, sin baseline III equivalente ni garantía de caché global fría. Esta muestra pequeña no se mezcla con las diez aperturas repetidas ni garantiza esa mejora futura.

[El perfil caliente .8](phase4-latency-diagnosis-final06-no-capture.json) contiene solo dos operaciones 7/9 de una sesión ON exitosa, sin capturas o sondeo de readiness. Registra 104 decodes con duración **0,1316–0,4871 ms**, cache inicial alrededor de 3 ms y primera asignación **215,90/196,93 ms desde Requested**. La espera de pasada conserva **177,54/164,75 ms**. El reloj comienza dentro de la aplicación después de pasos previos de navegación; no se compara como el mismo cronómetro OFF/UIA o presentación física. Los primeros marcadores no tienen RowId y no garantizan una cadena de la misma imagen.

La primera visita ON 11 .8 registra 52 misses y 52 generaciones, n=1: primera aplicación 443,16 ms y última 1.014,14 ms desde Requested. Una diferencia entre primeros hitos de cache/generación motivó .9, que evita reaplicar iconos genéricos vigentes y añade decode síncrono acotado al cache compartido. Los intentos ON fallidos/startup/calibración quedan excluidos.

[El perfil ON .9 sin capturas](phase4-latency-diagnosis-final07-no-capture.json) tiene dos operaciones 3/5: 52 hits y 52 aplicaciones por operación; primera asignación **590,26/226,14 ms**, última **772,70/549,61 ms** desde Requested. Decode máximo **2,709/0,4038 ms**. Los resultados son mixtos y no prueban mejora uniforme del perfil.

[La primera visita ON .9](phase4-latency-diagnosis-final07-firstvisit.json), operación 7 y n=1, registra 52 misses/generaciones/aplicaciones: primera **558,62 ms**, última **1.398,67 ms** desde Requested. Proveedor/conversión nativa p50 **25,79 ms**; decode máximo **23,10 ms**. No se afirma que todo decode sea menor de 0,5 ms. No hay RowId ni modo de decode registrado; los primeros hitos pueden pertenecer a filas distintas y no permiten inferir cadena causal, bloqueo UI síncrono o comparación con el reloj OFF. Faltan sondas de frames en operaciones 5/7; esa ausencia no demuestra falta de render. No hay prueba nueva de FPS físicos.

## Scroll, resistencia e interacciones

.9 completa **60 movimientos UIA**: diez SmallIncrement y diez LargeIncrement en cada 10k/50k/100k, todos Moved=true y cero timeouts. Es llamada/observación del proveedor, sin rueda/arrastre físico o frames. Evidencia: scroll-IV-off-final07-increments-10000/50000/100000. Las preparaciones Original/III anteriores quedaron parciales por no alcanzar el extremo o porcentaje inicial distinto; no ofrecen comparación física concluida. [PHASE4_SCROLL.md](PHASE4_SCROLL.md) conserva estas series y las históricas .8.

.9 completa **20 ciclos rápidos, 140 solicitudes y 20 confirmaciones finales**, sin timeout. Confirma carpeta/contador finales después del reposo, sin verificar cada imagen durante la ráfaga. [La sesión](../benchmarks/results/phase4/rapid-IV-off-final07-20/session.json) conserva el proceso y el alcance; .8 sigue histórica.

[Las operaciones reales .9](phase4-app-interactions-final07.json) tienen siete Passed: selección individual, multiselección, copiar/pegar, cortar/pegar, renombrar, atrás/adelante y toggle de preview. Solo fixtures sintéticos; archivos se corroboran por ruta/hash donde corresponde. Crear carpeta, nueva pestaña, ordenar y layout siguen Pending por cobertura UIA incompleta, sin afirmar que fallaron. Preview acredita toggle, sin prueba completa de contenido decodificado. Las ejecuciones .8 se conservan separadas.

Búsqueda nativa NTFS/sugerencias tiene contratos de corrección/cancelación, sin medir escritura→resultado en UI. Una búsqueda recursiva Files y otra indexada Explorer no tienen necesariamente igual alcance.

## Windows Explorer: serie primaria cualificada

El intento explorer-observer-validation 1×10k abrió el fixture, pero su primer observador no obtuvo nombres/conteo en diez segundos. CompletedWithTimeouts se conserva excluido; **no es tiempo de carga de Explorer**. Un control revisado n=1 verificó reacquisición de lista/estado y peers DataItem/ListItem con fallback RawView acotado.

La primaria **explorer-navigation-reacquired-primary** terminó Completed: 40 muestras, diez por 1k/10k/50k/100k, cero timeouts y cero reintentos por peers obsoletos. Details, ventana Normal 26;26;1440;753 en preparación y muestras. concurrent-build-checks.json conserva guardas sin build detectado. HWND 4131696/proceso 2000; solo se cerró la ventana creada mediante Quit, sin terminar el proceso compartido ni leer ubicaciones ajenas.

| Entradas | Nombre accesible p50/p95 [min–max], ms | Contador de estado p50/p95 [min–max], ms |
|---|---:|---:|
| 1.000 | 176,82 / 229,39 [149,68–263,35] | 168,88 / 221,37 [144,35–254,57] |
| 10.000 | 191,74 / 211,98 [184,39–214,70] | 184,94 / 203,84 [178,68–204,58] |
| 50.000 | 325,49 / 371,05 [304,22–399,89] | 319,37 / 363,86 [295,30–392,18] |
| 100.000 | 417,88 / 457,27 [397,46–462,38] | 411,27 / 450,41 [391,10–455,38] |

Fuente: [session.json](../benchmarks/results/phase4/explorer-navigation-reacquired-primary/session.json) y samples.csv, interpolación lineal. observer-script.ps1 SHA-256 **0B987C5504713B820CB700195134CE72D540AE1F293A2CE74F23788979991D6C**, coincidente con sesión. La consulta de estado precede a la de nombres; el contador puede aparecer antes. COM, reacquisición, consultas y polling permanecen incluidos. No se atribuyen CPU/RAM del proceso compartido a esta ventana. No se midieron miniaturas Explorer o fotogramas físicos.

Para repetir en un directorio nuevo preservando históricos:

~~~powershell
$taskWorkspace = 'C:\Users\guill\Documents\ChatGPT\Files Fast'
& "$taskWorkspace\tooling\Measure-Phase4ExplorerSession.ps1" -Scenario 1000,10000,50000,100000 -Repetitions 10 -SessionPurpose Comparison
~~~

Una comparación física adicional necesitaría geometría/viewport equivalentes, igual acción inicial y reloj, alternancia de variantes y captura de vídeo; conservar semántica de búsqueda y preparación. Este observador cualificado permite describir Explorer, sin demostrar superioridad universal entre aplicaciones.

## Alcance y estado de entrega

La comparación incorpora las series .9 y 327 contratos; .6/.7/.8 son históricos y Explorer conserva otro método. [La apertura OFF de entrega](phase4-final07-off-delivery-launch.json) acredita PID 38424/HWND 4655090 y control preparados; la serie primaria fue PID 28760. No es otra repetición de rendimiento. Solo falta verificar el tag local previsto. Permanecen No medido: FPS físicos, búsqueda escrita en UI, RAM en reposo y miniaturas equivalentes Original/Explorer. [FINAL_DELIVERY.md](FINAL_DELIVERY.md) identifica el binario y su recuperación.
