# Files Turbo Phase III: viewport, reciclaje y trabajo diferido

Fecha: 11 de octubre de 2026. Se continúa la entrega Phase II; no se modifican informes ni resultados históricos.

## Evidencia del código real

| Vista real | Control y panel | Hallazgo |
| --- | --- | --- |
| Detalles | `ListView` / `ItemsStackPanel` vertical | Panel virtualizante. El `ScrollViewer` de la plantilla pertenece a la propia lista. |
| Columnas | `ListView` / `ItemsStackPanel` | Panel virtualizante; expone índices visibles. |
| Iconos, tarjetas, cuadrícula y lista | `GridView` / `ItemsWrapGrid` | Panel virtualizante con orientación según el modo; expone índices visibles. |

No se encontró un panel no virtualizante ni un `ScrollViewer` externo que justifique reemplazar estos controles. La inspección de XAML demuestra la configuración; la cantidad de contenedores realmente realizados necesita observación en la aplicación. Se mantienen plantillas, selección, arrastrar y soltar, tooltips, agrupación y estética.

Microsoft recomienda conservar `ItemsStackPanel`/`ItemsWrapGrid` y utilizar actualización por fases para aplazar contenido costoso de filas. También advierte que los contenedores reciclados conservan valores y requieren comprobaciones al actualizar. Se aplican esas recomendaciones al camino ya existente de fase 3: [optimización oficial de ListView/GridView para WinUI](https://learn.microsoft.com/en-us/windows/apps/develop/performance/optimize-gridview-and-listview), [paneles de lista y cuadrícula](https://learn.microsoft.com/en-us/windows/uwp/controls-and-patterns/listview-and-gridview).

Se encontraron cuatro fuentes concretas de trabajo evitable:

1. Los callbacks asíncronos de fase 3 capturaban un `ListedItem` sin comprobar que los argumentos y el contenedor continuasen correspondiendo a ese objeto. Cada fila realizada podía iniciar un trabajo completo sin límite compartido.
2. Al terminar el scroll, `Parallel.ForEachAsync` iniciaba metadatos desde el thread pool con concurrencia dependiente del número de CPU. No compartía límite ni prioridad con las solicitudes de fase 3.
3. Cambiar opciones/tamaño de icono en detalles recorría todos los archivos, llamaba a `ContainerFromItem` para cada uno y creaba `Task.WhenAll` sobre toda la colección. Detalles y cuadrícula además solicitaban propiedades Git para todos los archivos, incluidos los no realizados. En 100.000 archivos este camino podía crear 100.000 solicitudes de Git aunque la pantalla mostrase unas pocas filas.
4. El setter `ShellViewModel.EnabledGitProperties` también recorría toda la colección y lanzaba cargas Git al mostrar esas columnas. Este fan-out se sustituye por una solicitud de las filas visibles y por carga al realizar las siguientes filas.

## Implementación

`Helpers/ViewportWorkQueue.cs` se utiliza directamente desde `Views/Layouts/BaseLayoutPage.cs`:

- Máximo de cuatro trabajos completos de fila activos y 128 solicitudes pendientes por página. Las solicitudes visibles se atienden antes que las de la banda de caché de WinUI; dentro de una prioridad, las más recientes se atienden primero.
- Una solicitud nueva visible puede sustituir una pendiente antigua de menor prioridad. Las filas descartadas se vuelven a solicitar al entrar en el viewport; una pasada de reposición solicita las visibles pendientes al vaciarse la cola.
- Los trabajos comienzan en el dispatcher con prioridad baja. Se elimina el `Parallel.ForEachAsync` del scroll; ninguna lectura de contenedores se traslada al thread pool.
- El mismo `ListedItem` no ejecuta dos generaciones simultáneamente, incluso cuando su trabajo anterior ya se ha cancelado. Las llamadas nativas iniciadas conservan su plaza hasta terminar realmente.
- El reciclaje, la siguiente ráfaga de scroll y la salida de la página cancelan las solicitudes correspondientes. Los callbacks validan identidad del elemento, identidad del contenedor, estado de reciclaje y pertenencia a la página antes de comenzar.
- Se conserva la fuente de cancelación de una fila completada hasta su reciclaje/cancelación. Así se pueden cancelar también reintentos de miniatura que continúan después de terminar el método principal. Una fila cancelada puede solicitar otra generación después de que la anterior haya terminado.
- La solicitud incluye la versión de columnas Git habilitadas. Una versión ya completada no se repite por resultados Git ausentes/inaccesibles; habilitar nuevos campos solicita otra versión. Se evita un bucle de reintentos de metadatos Git de mejor esfuerzo.
- Los overloads cancelables de `ShellViewModel` mantienen vigencia y comprobaciones de cancelación antes de aplicar imágenes/metadatos; ese trabajo se integra con la navegación de Phase III.

`DetailsLayoutPage.xaml.cs`, `GridLayoutPage.xaml.cs` y `ColumnLayoutPage.xaml.cs` delegan la recarga de iconos en el mismo camino de filas realizadas. Se conserva la invalidación de inicialización de todos los elementos para que los siguientes contenedores soliciten el tamaño correcto; se elimina la copia completa, la búsqueda de contenedor para cada archivo y el fan-out Git de archivos invisibles. La invalidación sigue siendo O(n), pero consiste en flags y no en consultas de contenedores ni tareas por archivo.

El límite de cuatro corresponde a los trabajos completos de fila. No se presenta como límite absoluto de todas las llamadas nativas: el pool STA y los reintentos de miniaturas existentes conservan su propia ejecución. Una llamada nativa ya iniciada puede finalizar después de la cancelación; se descarta su resultado si perdió vigencia.

## Instrumentación y verificación

Con diagnóstico habilitado, la página conecta `UiPerformanceViewProbe` a la lista real y elimina la conexión al salir. El probe observa contenedores, callbacks de render de XAML, scroll, entrega de entrada y demora del dispatcher. La cola añade `ItemJobStarted`, `ItemJobCompleted` e `ItemJobCancelled`, con duración del trabajo y número de solicitudes activas/pendientes. Estos eventos están asociados a la operación de navegación/búsqueda, no a una medición sintética de colección.

`tests-extra/Viewport/ViewportContracts.csproj` enlaza **el archivo completo de producción** de la cola. Los 17 contratos comprueban concurrencia/capacidad, prioridad visible, coalescencia, reciclaje antes/después de comenzar, serialización de generaciones del mismo objeto, cancelación de reintentos después de completar, sustitución de ubicación, rechazo del dispatcher, errores observados, cambio de versión Git, liberación de referencias tras `Dispose` y 100 rondas de 1.000 solicitudes. **La serie verificada pasa 17/17**, con compilación sin errores ni advertencias; se guarda en `reports/phase3-viewport-contracts-verified.json`. SHA-256 del fuente enlazado: `09EBC7BE6984C765FA8079C184DCCC9B2D6914AC4A35C9F176E24D5B70C750C8`.

La validación final vuelve a pasar **17/17**, con compilación sin errores ni advertencias, en `reports/phase3-viewport-contracts-final.json`. Fuente final SHA-256: `B32BD85AB469B1C7CECE561B64F0BDAB1D2782B2EF5E2B0C0C8ED424EF597D49`. El único cambio de la cola respecto a la serie anterior declara la clase `partial`, para permitir la generación interoperable de CsWinRT y eliminar la advertencia nueva `CsWinRT1028`.

La primera ejecución pasó 15/17: dos contratos comprobaron inmediatamente cero trabajos activos cuando tres continuaciones de `await` todavía estaban en el thread pool. El dispatcher de prueba no instalaba `SynchronizationContext`; no representaba la afinidad de continuaciones del dispatcher de la app. Se corrigió **solamente el banco**, instalando un contexto de sincronización controlado y una cola concurrente para sus callbacks. Se conservan el programa anterior y sus fallos en `reports/phase3-viewport-first-failure`; `reports/phase3-viewport-contracts.json` también conserva la primera ejecución. El fuente de la cola de producción es idéntico entre ambas ejecuciones. No se usan duraciones de contratos para atribuir mejora a la aplicación.

En las primeras sesiones instrumentadas, conectar el probe desde el primer callback ya en curso impedía observar esa primera fase 0 y podía dejar el contador de callbacks en cero. La fase 0 se registra ahora directamente desde `BaseLayoutPage.FileList_ContainerContentChanging`; el probe lee ese contador por operación sin duplicar su suscripción. Se cuenta el callback real de realización, no una estimación de la cantidad total de controles únicos. Los ceros anteriores no demuestran ausencia de contenedores ni de virtualización. La señal de actividad de memoria ya se emite en cada `ScrollViewer.ViewChanged`, para posponer el trim incluso durante un gesto largo.

Reproducción secuencial dentro del workspace:

```powershell
. ./tooling/Enter-WorkspaceEnvironment.ps1
& "$env:DOTNET_ROOT/dotnet.exe" build tests-extra/Viewport/ViewportContracts.csproj -c Release -v:quiet -clp:ErrorsOnly
& "$env:DOTNET_ROOT/dotnet.exe" tests-extra/Viewport/bin/Release/net10.0/ViewportContracts.dll reports/phase3-viewport-contracts-rerun.json
```

Los contratos ejecutan el scheduler real con tareas retenidas y un dispatcher controlado. No ejecutan WinUI, el shell ni la extracción nativa de miniaturas y no proporcionan FPS ni tiempos de apertura.

## Límites y prueba gráfica pendiente

La captura EventPipe del proceso experimental 03 detectó además trabajo ajeno a la cola de viewport que retenía el hilo principal después de publicar 100.000 archivos: `AdaptiveLayoutHelpers` consultaba `Directory.Exists` a través de los clasificadores de extensiones para cada elemento. Se reemplazó únicamente su clasificador privado por un conjunto de las 33 extensiones multimedia existentes, sin alterar el helper compartido, filtros ni preferencias. `tests-extra/Adaptive` enlaza la clase completa y pasa **17/17**, con compilación sin errores ni advertencias, en `reports/phase3-adaptive-contracts-final.json`; SHA-256 `78E9727467F4E74CD9B87BFFF713B45E6575EB6620311DB51861EC454F1364AA`. Las pilas, intervalos UTC y pausas reales de GC se documentan en `reports/PHASE3_EVENTPIPE.md`; la captura 03 es evidencia anterior a esta corrección y no demuestra por sí misma una mejora posterior.

Los resultados gráficos de esta entrega se registran en el informe de interfaz de Phase III y su evidencia asociada. Este documento no atribuye una aceleración ni una reducción de frames lentos a los contratos de la cola. Para evaluar el impacto real se deben comparar detalles, lista, iconos y agrupación en los mismos fixtures, tamaño de ventana y monitor; realizar scroll lento/rápido, salto al principio/final y cambio de tamaño de icono. Verificar que nombre, icono, overlay, miniatura, selección y tooltip pertenecen al mismo archivo después de cada reciclaje.

La política conserva la espera de estabilización de 200 ms existente. No se modifica el presupuesto de caché de WinUI, ni se predice la dirección del scroll sin mediciones que lo justifiquen. Limitar metadatos puede aumentar la espera hasta que todas las miniaturas de una pantalla se completen, especialmente con propiedades lentas; las etiquetas esenciales y la interacción de la lista no esperan esa cola. La captura gráfica debe decidir si cuatro trabajos es el equilibrio adecuado para este equipo y para red/medios.
