# Construcción de entradas — revisión acotada de Phase III

El recorrido real de Win32 crea ListedItem completos; el banco anterior de navegación sustituye ListedItem, sus notificaciones, formato de tamaños y NaturalStringComparer por tipos controlados. Sus tiempos no representan todo el coste de construcción/ordenación de la aplicación. La ordenación real conserva el comparador natural y cultural, el orden estable y las prioridades de carpetas/accesos directos. LINQ obtiene Name una vez por entrada y pasada de ordenación; no una vez por comparación.

Se aplican tres cambios pequeños: Name comprueba ShowFileExtensions antes de crear un basename descartado; la clasificación de archivos ya confirmados por FIND_DATA compara la extensión existente con .lnk/.url sin consultar un directorio relativo al proceso; y la clasificación de archivos comprimidos usa una lista estática y un bucle en lugar de crear un array y una closure por entrada. Se conserva la búsqueda de substrings de archivo comprimido, su prioridad y la comprobación posterior de directorios reales. No se modifica el helper global de accesos directos ni la resolución de symlinks/enlaces.

Los contratos extraen literalmente el getter, las expresiones de clasificación y IsZipPath, y compilan el helper completo original/actual con contadores de las llamadas Directory.Exists. Usan archivos y directorios desechables dentro del workspace, incluyendo un directorio ajeno con el mismo nombre que un archivo .lnk. Pasan 9/9 contratos; Release termina con cero advertencias y cero errores. Evidencia: `artifacts/phase3-entry-construction-final/entry-contracts.json`, con hashes de fuente y de todos los miembros extraídos.

En 10.000 lecturas de Name con extensiones visibles, la asignación baja de 800.000 a 0 bytes (80 bytes por lectura en este nombre de fixture). En 10.000 clasificaciones de archivo comprimido sin coincidencia, baja de 1.760.000 a 0 bytes (176 bytes por llamada). La clasificación de un archivo normal conocido pasa de una consulta Directory.Exists a cero; accesos directos también realizan cero consultas. Son asignaciones administradas y conteos de llamadas en un único proceso; no tiempos de WinUI, GetFile completo, recorrido nativo ni ordenación completa. No se atribuye una reducción de latencia visible a estas cifras.

La primera ejecución del harness compiló correctamente, pero no encontró el JSON de metadatos copiado en su carpeta de salida. Se corrigió únicamente TargetPath del proyecto y se usó una carpeta de resultados nueva; se conservan el build inicial y su fixture. El resultado definitivo procede de la segunda ejecución completa.

Se conserva Task<ListedItem?> en GetFile. Cambiarlo a ValueTask podría evitar un Task por archivo normal completado sin await, pero exige otra validación de consumidores/caminos asíncronos y queda fuera de estos tres cambios. No se convierte el comparador ni se difiere el tipo/tamaño que participa en la ordenación.

Reproducción con nombres nuevos para conservar las fuentes extraídas y los resultados:

```powershell
. tooling\Enter-WorkspaceEnvironment.ps1
& tests-extra\EntryConstruction\Prepare-EntryContracts.ps1 -SnapshotName recheck
& "$env:DOTNET_ROOT\dotnet.exe" build tests-extra\EntryConstruction\EntryConstruction.csproj -c Release -p:SnapshotRoot=snapshots/recheck
& "$env:DOTNET_ROOT\dotnet.exe" tests-extra\EntryConstruction\bin\Release\net10.0\EntryConstruction.dll --output artifacts/phase3-entry-construction-recheck
```
