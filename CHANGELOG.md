# Cambios

## 3.23.3 | Imágenes de Google Docs y del navegador

- Ctrl+V importa las imágenes rasterizadas incrustadas en el HTML del portapapeles, registra el medio y lo coloca en el cabezal.
- Al pegar o arrastrar, los datos locales de la imagen tienen prioridad sobre un enlace web alternativo.
- Una referencia exclusiva del navegador muestra un aviso específico cuando su imagen no está disponible para importar.

## 3.23.2 | Reproducción en proyectos densos

- 3.23.0 incorpora medición opcional de latencia de audio y cadencia de composición mediante el ámbito MCP `playback`.
- 3.23.1 prepara la mezcla compatible por tramos mientras mantiene un solo reloj de audio. Las mezclas con efectos de audio o cambios de velocidad conservan el procesamiento continuo.
- 3.23.2 da prioridad a las capas visibles y reutiliza lectores compatibles y trabajadores MGT con un límite fijo. Las distintas instancias MGT abren documentos independientes dentro del trabajador.

## 3.22.3 — Seguimiento y transcripción cancelables

- Seguimiento SAM y Whisper comparten una ventana de progreso que pausa el transporte, bloquea la edición y permite cancelar la tarea activa. Los trabajos iniciados mediante MCP utilizan la misma ventana y respetan su cancelación.
- El seguimiento muestra la carga del modelo y los fotogramas procesados. Whisper distingue la preparación del audio, el inicio del modelo y la transcripción. Si se cancela, se conservan los resultados que ya estaban aplicados.
- Un clic en una zona neutra devuelve el control de Espacio al transporte después de escribir en un campo, sin seleccionar la línea de tiempo.

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
