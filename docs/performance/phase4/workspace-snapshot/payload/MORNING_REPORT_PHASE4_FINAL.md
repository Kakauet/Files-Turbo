# Files Turbo IV — informe para el usuario

11 de octubre de 2026. **La versión 4.2.39.9, final07, está construida, validada, registrada, medida y abierta con diagnóstico OFF para entrega.** Fuente **bf4390ee0a2eed6cece0e99d1a021ce7fd255574**. Pasan **327/327 contratos**. Las 52 miniaturas visibles completan en **659,79 ms de mediana**, frente a **1.320,87 ms en III**, y las filas completan de arriba hacia abajo en las diez pruebas. También terminan navegación, scroll, ciclos rápidos y siete operaciones UI en su alcance. El tag local files-turbo-phase4 está previsto, pendiente de verificación tras guardar los documentos. Las revisiones .6/.7/.8 y sus datos se conservan como históricos.

## 1. Las cinco optimizaciones más importantes

1. **Atender primero lo visible y de arriba hacia abajo.** La fase inicial solicita una pasada coalescida Normal. La cola ordena índices ascendentes y también contenedores realizados cuando falta el rango visible. Cuatro trabajos de imagen y metadatos separados conservan guardas de cancelación y reciclaje.
2. **Reducir esperas de decode e iconos genéricos.** El destino considera DPI y conserva proporción. Solo PNG pequeños reconocidos, hasta 256×256 y 256 KiB, con destino físico de hasta 256, admiten decode síncrono optativo; los demás siguen asíncronos. .9 evita reaplicar el mismo icono genérico solo si coinciden su referencia exacta y el estado del item, y añade la ruta acotada al cache compartido.
3. **Liberar la plaza de imagen antes del overlay.** Los overlays pasan a metadatos Low tras aplicar la miniatura. Se retiraron las dos compuertas de fase del PictureBorder en Grid/Cards; rutas directas y cancelación conservan sus garantías.
4. **Evitar reiniciar una colección que conserva las mismas filas.** Un snapshot con las mismas referencias/orden no emite Reset; hasta 128 filas nuevas se añaden una a una. Reordenaciones, sustituciones, retiradas o agrupación conservan el fallback anterior.
5. **Limitar trabajo periódico de fechas a filas realizadas.** El tick relativo revisa contenedores presentes, con protección frente a cambios de item/navegación. Cambio de formato y activación de pestaña conservan refresco completo.

[PHASE4_PERFORMANCE.md](reports/PHASE4_PERFORMANCE.md) documenta cambios y límites. Las medidas no aíslan la aportación de cada optimización.

## 2. Cuánto mejoraron realmente las miniaturas

Diez aperturas por variante, mismas 52 imágenes visibles, diagnóstico OFF y cachés calientes/no controladas. Valores **mediana / p95**, ms:

| Observación capturada | Turbo III | IV .9 final |
|---|---:|---:|
| Primera imagen | 702,66 / 753,80 | 524,18 / 631,03 |
| Las 52 imágenes | 1.320,87 / 1.383,10 | 659,79 / 733,35 |
| Los tres vídeos H264 | 307,28 / 355,56 | 233,58 / 284,60 |

Frente a III, .9 reduce la mediana **25,40% para la primera imagen** y **50,05% para las 52 visibles**; el p95 completo disminuye **46,98%**. Frente al intermedio .7, las medianas disminuyen 19,37%/45,88%. El vídeo completo mejora **23,98% frente a III**, pero empeora **18,38% frente a .8**. El p95 de primera imagen también supera .8. No hay mejora monótona o universal.

Las diez aperturas .9 completan filas superior→inferior. El primer sondeo ya contiene varias filas; los empates no permiten ordenar los primeros cuatro eventos individuales. La cola sí comienza por arriba, sin serializar los cuatro proveedores.

Tres primeras visitas a rutas nuevas .9 tardan **1.259,18 / 1.025,67 / 1.099,55 ms** en completar las 52 imágenes. La mediana de esas tres es **1.099,55 ms**, frente a **1.286,59 ms en tres rutas .8**. Son rutas distintas, n=1 por ruta, sin baseline III equivalente o garantía de cache global fría: muestra débil, separada de las diez aperturas repetidas.

PrintWindow puede inducir render e incluye consultas, captura y sondeo; no mide el fotograma físico ni garantiza carga instantánea. [PHASE4_THUMBNAILS.md](reports/PHASE4_THUMBNAILS.md) conserva todos los resultados.

## 3. Cuánto mejoró el scroll

La versión final .9 completa **60 movimientos** en 10.000/50.000/100.000 entradas, cero timeouts: diez incrementos pequeños y diez grandes por tamaño, comprobando cambio del porcentaje del proveedor. La referencia .8 también se conserva por separado.

**No hay un porcentaje acreditado de mejora de fluidez o FPS.** Se cronometran llamadas UIA y observación del proveedor; las series anteriores no ofrecen comparación física equivalente completada. [PHASE4_SCROLL.md](reports/PHASE4_SCROLL.md) distingue llamadas de render real.

## 4. Cuánto mejoró la apertura de carpetas

El mismo observador de accesibilidad mide primera fila y contador completo. Original tiene cinco aperturas por tamaño; III y .9, diez. Las 40 muestras .9 completan sin timeout. Medianas del contador, ms:

| Entradas | Original | Turbo III | IV .9 final |
|---|---:|---:|---:|
| 1.000 | 370,90 | 202,86 | 231,83 |
| 10.000 | 458,95 | 292,28 | 226,90 |
| 50.000 | 2.028,53 | 727,24 | 722,77 |
| 100.000 | 3.548,98 | 1.201,27 | 1.156,09 |

Frente a III, el contador .9 mejora en mediana **22,37% en 10k**, **0,61% en 50k** y **3,76% en 100k**, pero empeora **14,28% en 1k**. La primera fila 100k pasa de 248,17 a 231,95 ms; la de 1k es mayor que III. No se afirma mejora de todos los tamaños. Los bloques no se alternaron y tienen historia/cachés distintas; accesibilidad y contador no equivalen a píxeles y layout finalizados.

## 5. Qué diferencias existen frente a Files original

En el bloque 100k, .9 observa contador completo a **1.156,09 ms**, frente a **3.548,98 ms del Original**: mediana **67,42% menor**. El Original se compiló sin optimizaciones Turbo y se adaptó solo a identidad/protocolo experimentales para comparar; no se reemplazó Files original instalado.

No se midieron miniaturas equivalentes del Original, memoria en reposo ni latencia de búsqueda escrita en UI. Privada y working set .9 son mayores que III al terminar este bloque, con historia distinta y sin probar fuga o reposo. [FINAL_EXPLORER_COMPARISON.md](reports/FINAL_EXPLORER_COMPARISON.md) conserva resultados favorables y desfavorables.

## 6. Cómo queda frente a Windows Explorer

Explorer completa **40 navegaciones**, diez por tamaño, cero timeouts y ventana normal 1.440×753. Para 100k observa nombre accesible a **417,88 / 457,27 ms** y contador de estado a **411,27 / 450,41 ms**, mediana/p95.

Explorer usa COM Navigate2 y reacquisición UIA; Files usa otro setter y peers cacheados. El viewport interno tampoco es idéntico. **No se calculan cocientes de superioridad entre aplicaciones.** No hay medida equivalente de sus miniaturas, FPS físicos o memoria atribuible a esta ventana. El intento inicial cuyo observador falló permanece excluido; su timeout no es tiempo de carga de Explorer.

## 7. Qué funcionalidades quedaron comprobadas

La .9 pasa **327 contratos**: **168 focalizados y 159 regresiones**, cero fallos. Incluyen cola visible, cancelación, identidad/reciclaje, DPI/decode, handoff exacto de icono genérico, cache compartido, publicación incremental, fechas, navegación y búsquedas NTFS/recursiva. Parte de WinUI, Shell y decoder son dobles; no prueban toda la interfaz.

En la aplicación .9 completan **40 navegaciones**, **60 movimientos**, **20 ciclos rápidos con 140 solicitudes**, diez aperturas de 52/52 imágenes, diez de 3/3 vídeos y tres primeras visitas de 52/52, cero timeouts. Los ciclos confirman carpeta/contador finales después del reposo, sin acreditar cada imagen durante la ráfaga.

La .9 pasa **selección individual, multiselección, copiar/pegar, cortar/pegar, renombrar, atrás/adelante y toggle de preview**, en fixtures y con rutas/hashes donde corresponde. El toggle acredita estado, sin probar todo el contenido decodificado. Crear carpeta, nueva pestaña, ordenar y cambiar layout siguen **Pending** por cobertura UIA incompleta. [FINAL_STABILITY.md](reports/FINAL_STABILITY.md) delimita el alcance y preserva los runs históricos .8.

## 8. Qué errores o límites siguen existiendo

La carpeta de 1k conserva una mediana de contador **14,28% mayor que III**. Las primeras visitas todavía tardan aproximadamente **1,03–1,26 s** para completar el viewport. .9 tiene peor vídeo y p95 de primera imagen que .8; las mejoras no son uniformes. La memoria final de navegación supera III, sin que la historia distinta permita demostrar fuga o causa.

El perfil ON sin capturas tiene resultados mixtos: dos aperturas con primera asignación a **590,26/226,14 ms desde Requested**, y una ruta nueva a **558,62 ms**, última a **1.398,67 ms**. Esa última registra un decode de **23,10 ms**; no todos quedan por debajo de 0,5 ms. El reloj interno difiere de la captura OFF, faltan identificadores de fila y modo de decode y no hay cobertura física de frames. No permite inferir una cadena causal por imagen, bloqueo síncrono o una mejora uniforme del perfil ON.

Build08 conserva **88 advertencias y cero errores**. No hay prueba de ausencia total de bugs, de todos los proveedores Shell, de render físico, de RAM en reposo o de todas las operaciones. Pending indica cobertura pendiente, sin inventar un fallo. El usuario aclaró que el parpadeo no ocurre en su uso normal; las capturas pueden perturbar render y no justifican declararlo como un bug nuevo.

El registro del antiguo final02 falló por compartir versión y se recuperó con una versión superior. Final04 se preservó sin registrar mientras se corregía una advertencia de metadata. Todos los candidatos y sus datos permanecen disponibles.

## 9. Cómo ejecutar Files Turbo final

El candidato **final07 4.2.39.9** está registrado. Para abrir el layout verificado:

~~~powershell
$taskWorkspace = 'C:\Users\guill\Documents\ChatGPT\Files Fast'
& "$taskWorkspace\tooling\Measure-Phase3ManualSession.ps1" -Action Disable
& "$taskWorkspace\tooling\Start-Phase4Experimental.ps1"
~~~

Disable afecta al siguiente proceso; Start verifica el existente o lo inicia. La entrega ya está abierta en **PID 38424**, con diagnóstico OFF y carpeta benchmarks/media-phase4 confirmada. El PID primario de las medidas fue 28760. Build08 terminó con **0 errores, 88 advertencias y 1:52,50**. [FINAL_DELIVERY.md](reports/FINAL_DELIVERY.md) contiene hashes completos, registro/actualización y recuperación III. El tag local files-turbo-phase4 está previsto y aún pendiente de verificación. **No se ha hecho push.**
