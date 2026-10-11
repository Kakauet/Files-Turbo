# Files Turbo — Phase III

Versión de desarrollo **4.2.39.4**, x64, Managed Release. Rama `codex/files-turbo-phase3`; referencia final `files-turbo-phase3`. Parte exactamente de Phase II `b18efc91ed3033081691f407a2bb7b5ee3d7d21d`, conservada en `files-turbo-phase3-start` y en su layout de recuperación.

## Qué cambia al utilizarlo

La carpeta publica nombres y datos esenciales sin esperar un icono por archivo. Los iconos, metadatos y columnas Git se solicitan para filas realizadas, mediante una cola con cuatro trabajos activos y 128 pendientes por página. Reciclar una fila o navegar descarta sus respuestas antiguas. La búsqueda muestra nombres antes de completar iconos y mantiene sus proveedores y alcance.

El profiling encontró dos consultas de disco repetidas en el hilo UI: clasificación multimedia y detección de soluciones, ambas usando una extensión como si fuera una ruta. Ahora comparan las extensiones conocidas. El estrés detectó también una navegación A→B→A que podía terminar en B; se corrigieron el envío de rutas, la actualización del directorio, los breadcrumbs y el icono de carpeta tardío.

## Qué está comprobado

La comparación gráfica, su dispersión y las rondas excluidas están en `reports/PHASE3_REAL_WORLD_PERFORMANCE.md` y `reports/PHASE3_MEASUREMENT.md`. El conteo accesible completo, la primera fila, la selección y los callbacks XAML se informan por separado. No se transforma un callback o una llamada UIA en FPS físicos.

En la GUI final, sin diagnóstico, el conteo completo p50 pasa de **3.015 a 1.130 ms en 100.000 entradas**; en 50k, de 1.447 a 609 ms. Son cinco repeticiones por tamaño, fixtures NTFS y caché caliente/no controlada. Las 30 ráfagas terminan en la carpeta solicitada; pasan cinco selecciones y veinte movimientos de scroll.

Hay regresiones: la primera fila accesible en 100k pasa de 154 a 185 ms y en 10k de 154 a 203 ms. El incremento pequeño de scroll observado también empeora. Estas métricas UIA incluyen al observador; no son clic hasta píxeles. La mejora demostrada es el tiempo hasta completar carpetas grandes, con navegación estable en esos ensayos.

**192 contratos pasan.** El Release definitivo compila con cero errores y 104 advertencias. El paquete completo conserva inventario, hashes, dependencias y servidor. La procedencia verifica que los fuentes actuales corresponden al PDB/ensamblado distribuido, en `reports/phase3-build-provenance.json`; la validación final está en `reports/phase3-final-verification.json`.

La búsqueda emparejada sobre NTFS conserva todos los hashes de resultados: en 100k, callback exacto Phase II→III 280,88→165,59 ms; parcial 218,30→101,51 ms. Incluye debounce y proveedor, excluye dispatcher/renderizado: **la mejora de búsqueda visible al escribir no está medida**. Tampoco está demostrada superioridad frente a Explorer, ni 144 FPS sostenidos.

## Ejecutar y recuperar

La identidad registrada es sólo `FilesTurbo.Experimental`; el layout definitivo es `artifacts/development-layout/managed-release-phase3-stable06/layout`. El MSIX de desarrollo está junto a él y es **sin firma**: se prueba mediante el layout registrado, no como instalador público.

Desde este workspace, en PowerShell 7.2 o posterior:

```powershell
./tooling/Register-Phase3Experimental.ps1
Start-Process ('files-turbo:?folder=' + [Uri]::EscapeDataString((Join-Path $PWD 'benchmarks/synthetic/1000'))) -WindowStyle Hidden
```

Para volver a Phase II:

```powershell
./tooling/Register-Phase3Experimental.ps1 -RestorePhase2
Start-Process ('files-turbo:?folder=' + [Uri]::EscapeDataString((Join-Path $PWD 'benchmarks/synthetic/1000'))) -WindowStyle Hidden
```

El script verifica identidad, ubicación y hash del manifest de II y registra cada operación de recuperación. Cierra únicamente la aplicación experimental al actualizar. Para regresar a III, ejecutar de nuevo el primer bloque. Los informes y layouts anteriores se conservan. El snapshot recuperable de scripts y contratos está en `source/docs/performance/phase3/workspace-snapshot`; no contiene SDK, fixtures ni binarios.

## Límites y siguiente trabajo

Quedan pendientes pruebas específicas de red/cloud, rutas largas, códecs, menús shell/COM, drag/drop y operaciones de archivos sobre copias desechables. La conservación de sus caminos de código no acredita compatibilidad universal.

La traza final ya separa envío, solicitud interna, publicación y callback visible. El siguiente trabajo debe comparar esos tramos con una traza equivalente de II y perfilar la validación previa y publicación→render; no hubo trabajos de cola de metadatos antes del primer callback visible en las ocho navegaciones instrumentadas. También falta completar búsqueda con entrada real y comparación manual justa con Explorer. Los procedimientos están en el informe general. El diagnóstico queda desactivado para el uso normal.
