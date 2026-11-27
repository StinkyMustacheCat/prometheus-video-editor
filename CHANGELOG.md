# Cambios

## 3.22.2 — ajustes de procesamiento y entrega unificada

- Ajustes → Procesamiento: inventario ligero de CPU, RAM y adaptadores; selección por función, preferencias manuales, restauración automática y diagnóstico exportable.
- Selección efectiva y último motor utilizado visibles en ajustes y mediante MCP `get_state` con `scope: processing`. Las preferencias no modifican el proyecto ni su historial de deshacer.
- Procesos separados para cambiar de proveedor de IA, restauración del fotograma SAM preparado y fallos manuales explícitos. Respaldo automático comprobado al iniciar cada motor.
- Se conserva el codificador de los segmentos al reanudar una exportación recuperable.

## 3.22.1 — detección y selección de hardware

- Identidad DXGI del adaptador, controlador, memoria dedicada, RAM y arquitectura UMA. La RAM compartida no se suma como memoria adicional ni identifica por sí sola una APU.
- CUDA, DirectML y WebGPU reciben el dispositivo elegido; el compositor mantiene su adaptador efectivo para compartir texturas con CEF.
- Las rutas NVIDIA y AMD se resuelven por función. No se seleccionan por marca del procesador ni por el orden del primer adaptador.

## 3.22.0 — integración de las ramas NVIDIA y AMD APU

- Se integra la adaptación AMD APU 3.21.2 sobre la base 3.19.10, conservando la corrección de órdenes de audio extensas de Windows.
- AMF para exportación H.264/HEVC; SAM ONNX DirectML/WebGPU, PyTorch CPU temporal y Whisper CPU INT8, junto al runtime CUDA original.
- Runtimes ONNX y PyTorch CPU/CUDA aislados. Se mantienen los formatos de proyectos, máscaras y MGT.
- La evidencia de la rama AMD anterior se conserva como antecedente; la validación de la integración en cada equipo se registra por separado.

## 3.19.10 — reproducción y exportación de montajes con muchos cortes

- Las mezclas de audio extensas se entregan a FFmpeg mediante un archivo temporal, evitando el error 206 de Windows al superar el tamaño máximo de la orden de inicio.
- La corrección cubre reproducción, exportación de audio y mezcla para exportación de vídeo, sin cambiar cortes, efectos ni niveles.
- Limpieza del archivo de mezcla al terminar, detener, cancelar o fallar el proceso. Comprobación explícita de rutas que todavía excedan el límite.
- Regresión con muchos cortes, equivalencia de PCM/WAV y comprobación sobre el proyecto real que provocaba el bloqueo.

## 3.19.9 — entrega completa

- FFmpeg 9.1 compilado desde fuentes incluidas, con Rubber Band 4.0.0 para conservar el cambio de tono.
- Motor multimedia y sus DLL aislados en `Runtime/ffmpeg`, con resolución relativa y overrides explícitos conservados.
- Fuentes originales de bibliotecas, parches, recetas, dependencias Rust, registros e inventarios de procedencia incluidos.
- Paquetes de fuentes de terceros divididos por tamaño para adjuntarlos a una release.


## 3.19.8 — Preparación de distribución

- Documentación del cliente actual, compilación y pruebas reproducibles con versiones de dependencias fijadas.
- Distribución autónoma para Windows x64, inventario y textos de licencia de dependencias.
- Acceso a la licencia, avisos y estado de publicación desde Ajustes → Acerca de Prometheus y licencias.
- IA localizada junto al ejecutable, sin búsqueda implícita de una instalación WinForms anterior en el disco.
- Configuración MCP generada para la ruta donde se extrae el portable.
- Separación de fuentes públicas, binarios, IA y fuentes de terceros; sumas de comprobación de los archivos de entrega.

Esta versión conserva la base funcional 3.19.7. No incorpora una migración de framework ni anuncia nuevos sistemas operativos o GPUs compatibles.

## 3.19.3–3.19.7

Paquete previo de estabilización tras auditoría: recuperación y procesos externos, coherencia de la edición, automatización MCP y gestión de errores/reintentos de formas de onda. Los resultados de pruebas de la entrega actual se documentan por separado; los ensayos automáticos no sustituyen la revisión manual de un proyecto real.
