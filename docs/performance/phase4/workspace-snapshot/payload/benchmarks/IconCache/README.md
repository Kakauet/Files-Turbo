# Banco de pruebas de IconCacheService

El proyecto compila el fichero real `IconCacheService.cs`. La variante `baseline` usa una copia textual del blob Git `e6a22b2c00666187e3c6bda464047f1de9f295e7`, extraído del tag `files-turbo-baseline` (commit `1994d9a4269f5144c70bc67d6a48de4a20616912`). Se normalizan solamente los finales de línea a CRLF. `turbo` enlaza directamente el fichero modificado en `source`.

`DependencyStubs.cs` sustituye exclusivamente APIs externas: extracción de iconos shell y clases WinUI. No lee archivos de usuario, no extrae iconos reales y no renderiza interfaz. La región medida utiliza una caché caliente y comprueba que no llama al doble de shell. El resultado describe tiempo y asignaciones de búsquedas en esta caché interna, no latencia de navegación completa, miniaturas ni arranque de la aplicación.

Los recuentos de 1.000/10.000/50.000/100.000 representan llamadas a la caché, no archivos físicos. Se usan cuatro patrones de claves y 31 repeticiones, calentamiento previo, mediana, p95, mínimo/máximo y bytes asignados en el hilo medido. Los dobles conservan contratos de tareas pendientes, excepciones y `null` para pruebas dirigidas.

Ejemplo desde la raíz del workspace, coordinando builds secuencialmente:

```powershell
. .\tooling\Enter-WorkspaceEnvironment.ps1
$env:DOTNET_TieredCompilation = '0'
& .\tooling\dotnet-10.0.401\dotnet.exe build .\benchmarks\IconCache\IconCache.csproj -c Release -p:Variant=baseline -v:quiet
& .\tooling\dotnet-10.0.401\dotnet.exe .\benchmarks\IconCache\bin\baseline\Release\net10.0\FilesTurbo.IconCache.baseline.dll benchmark baseline "$PWD\artifacts\icon-cache" 31
& .\tooling\dotnet-10.0.401\dotnet.exe build .\benchmarks\IconCache\IconCache.csproj -c Release -p:Variant=turbo -v:quiet
& .\tooling\dotnet-10.0.401\dotnet.exe .\benchmarks\IconCache\bin\turbo\Release\net10.0\FilesTurbo.IconCache.turbo.dll benchmark turbo "$PWD\artifacts\icon-cache" 31
```

El directorio exacto de salida puede variar según MSBuild: consulte la línea de salida de la compilación. Las pruebas también se ejecutan antes de medir; el modo `test` permite repetir solamente los contratos. Las observaciones de concurrencia del original se registran sin tratarlas como contratos ya garantizados por esa versión. Turbo exige una sola carga por clave concurrente e invalidación segura al limpiar durante una carga.

La variable `DOTNET_TieredCompilation` solo afecta al proceso y sus hijos. Debe ser equivalente en ambos ejecutables y conviene eliminarla al terminar esa consola. La serie inicial con configuración predeterminada mostró cambios de nivel JIT en el primer patrón; una segunda comparación con el JIT por niveles desactivado evita atribuir ese calentamiento a la optimización. Cada ejecución registra su entorno en JSON; no deben mezclarse series con distintas configuraciones. Las mediciones deben ejecutarse sin una compilación concurrente.

La optimización modifica el trabajo de búsqueda en caché, las tareas completadas y la deduplicación de cargas pendientes. Conserva los `null` cacheados y el reintento tras excepción. No introduce límites LRU, invalidación DPI, una caché de miniaturas nueva ni cambios en los decodificadores. El uso de tareas y `Lazy` añade memoria fija por clave frente al byte array original; se mide la eliminación de asignaciones transitorias por llamada y no se afirma una reducción de RAM en reposo.
