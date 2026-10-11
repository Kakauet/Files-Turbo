# Búsqueda — Files Turbo Phase III

## Cuello de botella de interfaz

La barra de búsqueda de Phase II esperaba siempre 200 ms, ejecutaba el proveedor y esperaba a que terminasen los iconos de sus diez sugerencias antes de publicar los nombres. Ese último paso incluía shell/STA y conversiones de bitmap en el dispatcher. Un proveedor rápido podía quedar oculto detrás de esas esperas. La lista principal también precargaba iconos antes de publicar lotes y antes de aplicar todos los resultados; la integración de ShellViewModel se describe en el informe general.

Phase III publica los nombres reales y sus rutas antes de enriquecer los iconos. Conserva el glifo existente de búsqueda hasta que llega cada imagen. Las propiedades que controlan ese glifo y el icono ahora notifican cambios; cada imagen actualiza exactamente el SuggestionModel que produjo esa consulta. Se comprueban consulta, pestaña, ubicación, modo, cancelación y propiedad de la solicitud antes de publicar o cambiar iconos.

El debounce utiliza 80 ms para consultas de al menos tres caracteres cuando el último proveedor terminó en 150 ms o menos, 160 ms para consultas cortas y 200 ms para AQS o después de una búsqueda lenta. El coste observado excluye los iconos: una cola lenta de shell no penaliza artificialmente la próxima consulta por nombre. Una consulta cancelada durante el debounce no inicia el proveedor; una operación anterior que termina tarde no publica resultados.

## Proveedor y corrección

No se cambia NativeNameSearch ni el orden de proveedores. Se mantienen Windows Search, su ranking de sugerencias, AQS, etiquetas, bibliotecas, Home, Unicode, wildcards DOS, alcance recursivo, reparse tags y el recorrido nativo que reconcilia el índice. Se evitan consultas Directory.Exists al clasificar accesos directos devueltos como archivos por el proveedor WinRT. Las coincidencias de nombres de archivos se comprueban antes de solicitar sus propiedades básicas; las carpetas conservan su comprobación posterior con DisplayName.

La consulta WinRT cede al dispatcher cada 32 elementos, además de sus operaciones asíncronas, para evitar una larga sucesión de propiedades prefetched resueltas sin ceder. La apertura del StorageFolder y la consulta de estado indexado respetan la cancelación. Las sugerencias sin suscriptores SearchTick no acumulan una segunda lista de resultados pendientes.

Se descartó reutilizar sin más la colección de la carpeta actual: las búsquedas existentes incluyen descendientes y esa colección solo contiene un nivel. Tampoco se incorporó una caché con resultados de frescura dudosa. Un futuro modo explícito «solo esta carpeta» necesitaría validación del watcher y de sus filtros antes de habilitar esa reutilización. Se conserva Everything como integración opcional pendiente; no se instala, activa ni presupone un servicio ni se indexa el equipo.

## Instrumentación real integrada

La instrumentación optativa y limitada a fixtures sintéticos de UiPerformanceTrace registra operaciones SearchSuggestions independientes de la navegación y de la búsqueda completa: QueryChanged, ProviderStarted, ProviderCompleted, DataPublished, IconsCompleted, Canceled y Failed. No registra el texto de la consulta ni nombres de archivos.

El DataTemplate real registra SuggestionContainerLoaded y SuggestionAfterRender cuando el mismo modelo sigue perteneciendo a la solicitud actual. Loaded confirma un contenedor; RunAfterNextRender confirma un callback posterior de renderizado XAML. Ninguno demuestra por sí solo que se hayan presentado píxeles al usuario ni que un elemento fuera del viewport sea visible. Las mediciones de UIA o la sesión manual deben aportar esa evidencia. Las sesiones reales y sus limitaciones pertenecen a `reports/PHASE3_REAL_WORLD_PERFORMANCE.md`.

## Verificación y banco de publicación

El proyecto `benchmarks/Phase3/Search/SearchPipeline.csproj` enlaza el helper usado por la aplicación y el NativeNameSearch real. Comprueba que los nombres se publican mientras la fase de iconos sigue bloqueada; cancelación durante debounce, proveedor y enriquecimiento; proveedores antiguos que no cooperan; cambio de ámbito; escritura rápida; doce solicitudes independientes concurrentes; y cero resultados sin entradas ficticias.

El banco compara la secuencia de debounce original/Phase II de 200 ms con la secuencia Phase III enlazada a fuente. El algoritmo original de recorrido se extrajo del FolderSearch original y utiliza su helper original de extensiones. Phase II y Phase III enlazan el mismo proveedor nativo. Usa las cuatro carpetas sintéticas existentes, seis consultas, diez resultados máximos, calentamiento y orden Original → Phase II → Phase III → Phase III → Phase II → Original.

La métrica PublishedMs termina en un callback del harness. Incluye espera y recorrido real de NTFS, pero excluye WinRT/indexación, ListedItem, shell/iconos, dispatcher y renderizado WinUI. Es una verificación del coste de preparación de sugerencias; no se presenta como tiempo de resultado visible. La independencia de la publicación frente a los iconos se demuestra con un contrato bloqueado, sin inventar un tiempo de extracción de shell.

Entrega de datos: `artifacts/phase3-search-verified/`. El build Release del harness terminó con cero advertencias y cero errores. Pasaron 11/11 contratos del helper real, incluidos el backoff tras un proveedor lento; 28/28 contratos del proveedor nativo; y 3/3 contratos de recorrido recursivo contra el algoritmo original, incluida una junction que apunta al propio fixture del workspace y no debe recorrerse. Los dos últimos bancos se ejecutaron con sus binarios existentes de Phase II, porque NativeNameSearch no cambia; los contratos nuevos enlazan SearchSuggestionPipeline de Phase III. No se utiliza una implementación simulada de ese helper.

Se realizaron 432 muestras medidas y 144 calentamientos, seis muestras por variante/consulta/tamaño. Todos los hashes de conjuntos de las sugerencias coincidieron. Las variantes se ejecutaron secuencialmente en un único proceso; no son seis procesos independientes. No se compilaron otros proyectos ni se ejecutó medición gráfica durante este banco. p50 promedia los dos valores centrales; p95 usa rango más cercano y, con seis muestras, es el máximo observado. No estima la cola de latencia de la GUI.

### Carpeta de 100.000 archivos: espera y publicación del harness

| Consulta | Original p50 ms | Phase II p50 ms | Phase III p50 ms | Phase II p95 ms | Phase III p95 ms |
|---|---:|---:|---:|---:|---:|
| Exacta | 302,26 | 280,88 | 165,59 | 308,53 | 216,79 |
| Parcial | 225,58 | 218,30 | 101,51 | 231,26 | 113,25 |
| Sin resultados | 293,34 | 264,13 | 155,29 | 293,28 | 196,38 |
| `.txt`, diez sugerencias | 218,38 | 228,11 | 94,72 | 232,83 | 96,36 |
| Unicode `東京` | 223,75 | 225,32 | 183,06 | 233,41 | 194,97 |
| Wildcard | 218,19 | 223,72 | 98,45 | 229,10 | 126,74 |

`東京` tiene dos caracteres y conserva 160 ms de debounce; no se oculta que mejora menos que las consultas largas. La dispersión mínima/máxima y todos los tamaños están en `pipeline/summary-corrected.csv`; las muestras sin agregar están en `pipeline/pipeline-publication.csv` y JSON. Se conserva también el resumen inicial: su columna de proveedor elegía el central superior, por lo que el CSV corregido recalcula esa mediana correctamente desde las muestras. Las medianas de publicación anteriores ya eran correctas. Esto no requiere repetir el banco ni sobrescribir los datos originales.

El recorrido nativo de Phase II y Phase III es el mismo. Sus variaciones de tiempo de proveedor no se presentan como otra aceleración ni como regresión demostrada del algoritmo. La mejora medible de este banco procede de adelantar la solicitud/publicación con el debounce; el contrato bloqueado verifica por separado que la extracción de iconos ya no precede a los nombres. El comportamiento de la consulta WinRT, los bindings y los callbacks de renderizado precisa la comprobación en la aplicación Release integrada.

Reproducción desde la raíz del workspace, conservando resultados anteriores:

```powershell
& tooling\Run-Phase3SearchBenchmarks.ps1 -RunName phase3-search-recheck -Repetitions 3
```

La ejecución inicial dejó diez contratos en `contracts/`. Una compilación breve posterior añadió el contrato de backoff y dejó los once definitivos en `contracts-additional/`. Se mantienen ambos registros. El código actual genera los once en una sola ejecución. Los hashes de fuente, SDK 10.0.401, sistema operativo, orden y naturaleza de caché caliente están en `run-metadata.json`.

## Pendiente en la GUI

Hay que comprobar manualmente o mediante UIA las sugerencias y sus iconos, la escritura rápida real, el cambio de carpeta/pestaña mientras busca, AQS/etiquetas, bibliotecas con raíces solapadas e índice, cloud/red y carpetas NTFS sensibles a mayúsculas. No se garantiza búsqueda instantánea en árboles no indexados. Los contratos del proveedor y el banco de callback no sustituyen esas pruebas.
