# Phase III — vigencia de las solicitudes de path

La prueba gráfica de estrés de la versión 04 detectó un fallo real: tras diez ciclos correctos, el ciclo 10 terminó mostrando 100.000 archivos aunque la última de sus siete solicitudes pedía la carpeta de 1.000. Se conserva `benchmarks/results/phase3/ui-PhaseIII-20261011-135535-32b6640d/samples.csv`; el fallo no se elimina de la comparación ni se presenta como una sesión terminada correctamente.

La evidencia instrumentada de `manual-20261011-135516-8abb02fb/trace.jsonl` termina con la operación 56: solicitud a **13:55:50.8644196Z**, publicación final de 100.000 a **13:55:51.8868564Z**, listado completo a **13:55:52.1754546Z**, sin otra operación posterior. La app siguió respondiendo. El script anterior no guardaba el UTC de cada setter ni el destino en cada operación, así que esta evidencia no identifica cuál de las dos solicitudes de 100.000 ni cuál await exacto ganó. El código sí permitía reproducir determinísticamente la carrera descrita a continuación.

## Causa y corrección

`CurrentPathSet` emite `PathBoxQuerySubmitted`; `BaseShellPage.NavigationToolbar_QuerySubmitted` espera `CheckPathInputAsync`. Tanto esa ruta como la ruta moderna `HandleItemNavigationAsync` validan raíz/carpeta de forma asíncrona antes de navegar.

La secuencia vulnerable era **A actual → B en validación → A solicitado de nuevo**. A se descartaba porque el directorio de trabajo o los breadcrumbs todavía mostraban A. La validación antigua de B no quedaba invalidada y podía navegar después. Los breadcrumbs además podían corresponder a otra generación y descartar una solicitud válida.

Ambos métodos comparten ahora `LatestPathNavigationCoordinator<IShellPage>`:

- Cada envío incrementa la revisión **antes** de comprobar vacío o igualdad. Volver al directorio actual invalida la validación anterior aunque no requiera navegar.
- Los resultados de raíz, carpeta, archivo, aplicación, URI y diálogo se comprueban antes de continuar. Una tarea que pierde vigencia no navega, añade historial, lanza otra acción, muestra un error tardío ni cambia el texto de path.
- El owner debe seguir siendo el mismo shell de `ContentPageContext`; `Dispose` invalida solicitudes. Un resultado de validación antiguo no se redirige a otro tab o panel que pase a ser actual durante el await.
- La igualdad se decide con el `WorkingDirectory` real; los breadcrumbs dejan de ser la autoridad. Se mantiene normalización, resolución de alias, validación, FTP, navegación especial, ejecución de comandos y lanzamiento de archivos.
- Un diálogo moderno que pierde vigencia devuelve éxito de manejo para que su caller no restaure el texto viejo como error después de una solicitud nueva.

Si B ya empezó a cambiar de raíz de disco y `WorkingDirectory` sigue mostrando A, invalidar la validación del toolbar no basta. Por eso los no-ops del toolbar también requieren `!IsWorkingDirectoryUpdatePending`. `ModernShellPage.NavigateToPath` respeta la misma condición, de modo que A llegue a `SetWorkingDirectoryAsync` e invalide la generación de B.

## Contratos vinculados al fuente

`tests-extra/Phase3PathNavigation/Prepare-PathContracts.ps1` extrae **literalmente** los dos métodos de producción y `NormalizePathInput`. El proyecto enlaza además el coordinador completo. Sustituye los servicios de validación y UI por tareas controladas; no copia la lógica de navegación. Antes de guardar el resultado exige que el hash actual del toolbar coincida con el fuente del que se extrajeron los métodos.

La validación final pasa **37/37**, compilación sin errores ni advertencias, en `reports/phase3-path-navigation-contracts-final-02.json`. Incluye A→B→A en ambos métodos, A mientras B tiene un cambio de directorio pendiente, un C nuevo, rechazo/vacío, dominio compartido moderno/legacy, fallo tardío de carpeta/archivo/aplicación/URI/diálogo, cambio de owner, dispose, breadcrumbs atrasados, normalización slash/UNC, alias, FTP, archivos, comandos y salida de búsqueda al mismo directorio. La ejecución anterior de 35 casos se conserva en `reports/phase3-path-navigation-contracts-final.json`; precede a los dos casos de cambio de raíz pendiente.

Fuentes validados:

- Toolbar: `50FBACE19B050CD1C54B8DDFAFD3AA66872C4250F09A72BF1A08B2036116ABD0`.
- Coordinador: `188FBEBA9E0834F7808FC5A66DD118E9959360A2F08F80434B374043E4A0F8C5`.

Reproducción secuencial desde la raíz del workspace:

```powershell
./tests-extra/Phase3PathNavigation/Prepare-PathContracts.ps1
. ./tooling/Enter-WorkspaceEnvironment.ps1
& "$env:DOTNET_ROOT/dotnet.exe" build tests-extra/Phase3PathNavigation/PathNavigationContracts.csproj -c Release -v:quiet -clp:ErrorsOnly
& "$env:DOTNET_ROOT/dotnet.exe" tests-extra/Phase3PathNavigation/bin/Release/net10.0/PathNavigationContracts.dll reports/phase3-path-navigation-contracts-rerun.json
```

## Revisión del camino posterior

La revisión de lectura confirma que las revisiones capturadas de `SetWorkingDirectoryAsync` protegen la asignación de raíz después de await y los resultados Git, incluso con A→B→A; comparar solamente el texto del path no bastaba para ese ABA. `IsWorkingDirectoryUpdatePending` es falso para el estado inicial null/null, para el último envío vacío y cuando el path ya se ha aplicado, aunque quede trabajo Git pendiente. La revisión de breadcrumbs sustituye la lectura del CTS compartido por una revisión capturada por llamada.

También se revisó `CheckForSolutionFile`: las comparaciones directas con las extensiones almacenadas `.sln`/`.slnx` mantienen los casos y nulos existentes, eliminan el sondeo de paths relativos, la copia completa y PLINQ, y eligen el primer elemento en el orden actual. No se añade un filtro de carpetas que cambie la semántica de metadatos almacenados.

La revisión señaló tres bordes adicionales y el responsable de los archivos incorporó las guardas correspondientes: tras cambiar el filtro se comprueba la revisión antes de emitir el evento de path; el icono captura revisión/path y comprueba vigencia y disposal tras el await; breadcrumbs comprueba revisión/disposal después de Clear, antes de cada Add y después de cambiar la propiedad de override. La lectura final confirma que estas guardas cubren las reentradas y el icono tardío de A, incluso cuando el nuevo B todavía espera raíz y el directorio publicado sigue en A.

El harness independiente `tests-extra/Phase3WorkingDirectory` extrae literalmente los métodos de directorio, icono, breadcrumbs, navegación moderna y la propiedad de cambio pendiente. Compila esos cuerpos con proveedores controlados y callbacks de reentrada. Su ejecución autoritativa pasa **41/41**, compilación sin errores, en `reports/phase3-working-directory-contracts-final-01.json`. Incluye raíz de disco atrasada, A→B→A de Git/icono, vacío/disposal, reentrada de filtro, Clear/Add/override y la ruta moderna que fuerza A mientras B está pendiente. Estos casos complementan los 37 del toolbar; no sustituyen sus dependencias por una reproducción gráfica.

Los hashes completos de los archivos coinciden con la metadata de esa ejecución:

- ShellViewModel: `E7BFC351253F89CC330A2ED1FFD08B873C3280DB34173DCD30EC4D836B3AD25D`.
- BaseShellPage: `00B668C6C6B8DABDAD310243BF3DA3CB53F73ABFB387186F394C90B9BC115B9F`.
- ModernShellPage: `4201CFC25076F1BBB4DA55AC9BB022F8CB40767D24B26B317B5864F7BDA94D74`.

Reproducción del camino posterior, después de terminar cualquier otra compilación:

```powershell
./tests-extra/Phase3WorkingDirectory/Prepare-WorkingDirectoryContracts.ps1
. ./tooling/Enter-WorkspaceEnvironment.ps1
& "$env:DOTNET_ROOT/dotnet.exe" build tests-extra/Phase3WorkingDirectory/WorkingDirectoryContracts.csproj -c Release -v:quiet -clp:ErrorsOnly
& "$env:DOTNET_ROOT/dotnet.exe" tests-extra/Phase3WorkingDirectory/bin/Release/net10.0/WorkingDirectoryContracts.dll reports/phase3-working-directory-contracts-rerun.json
```

Los contratos no ejecutan WinUI, los proveedores UIA reales ni la resolución nativa del shell. Este informe no afirma todavía que la versión empaquetada 06 haya pasado el estrés gráfico; esa prueba requiere evidencia nueva con el paquete final y las solicitudes individuales registradas.
