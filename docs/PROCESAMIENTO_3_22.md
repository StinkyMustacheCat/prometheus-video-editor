# Procesamiento unificado 3.22.2

La primera entrega conjunta cubre 3.22.0, 3.22.1 y 3.22.2. Integra la base NVIDIA 3.19.10 y los cambios AMD APU 3.21.2. El USB se utiliza como referencia y no es necesario para ejecutar el paquete.

## Selección por función

| Función | NVIDIA | AMD APU | Alternativa disponible |
| --- | --- | --- | --- |
| Monitor y MGT | Direct3D 11 / CEF | Direct3D 11 / CEF | Se conserva la GPU efectiva del compositor |
| H.264 / HEVC | Media Foundation / NVENC | AMF | CPU, según elección o respaldo automático |
| Máscara SAM | CUDA | DirectML | WebGPU u ONNX CPU |
| Seguimiento SAM | CUDA | DirectML + PyTorch CPU | WebGPU + CPU o PyTorch CPU |
| Whisper | CUDA FP16 | CPU INT8 | CPU INT8 |

Automático elige por compatibilidad y comprueba el motor al utilizarlo. La presencia de carpetas no certifica el controlador ni garantiza que todos los nodos de un modelo se ejecuten en GPU. Los adaptadores de software no se ofrecen como aceleradores de IA. DirectML usa el índice DXGI comprobado; CUDA resuelve el dispositivo por LUID y WebGPU exige una coincidencia inequívoca de fabricante/dispositivo.

La RAM instalada, disponible y la memoria dedicada se muestran por separado. La memoria compartida de Windows forma parte de la RAM. La condición UMA se consulta al controlador; no se deduce de que el procesador sea Ryzen. El ajuste de cuatro hilos automático se limita al perfil UMA de 8 GB de la adaptación original; no se aplica a todos los equipos.

## Ajustes

Abrir **Ajustes → Procesamiento** permite consultar el inventario sin cargar SAM ni Whisper. Cada función muestra su selección guardada, origen y último resultado comprobado. **Aplicar** afecta las tareas siguientes; un seguimiento o una exportación ya iniciados mantienen su configuración. Un cambio de proveedor de la selección SAM recrea el trabajador y restaura el último fotograma preparado.

- **Automático** puede iniciar un proceso nuevo con otro motor compatible si falla la inicialización de IA. Se conserva el motivo del respaldo. No cambia silenciosamente el motor de un seguimiento que ya empezó a guardar resultados.
- **Elección manual**: si no está disponible, se informa del fallo. No se sustituye silenciosamente por CPU.
- **GPU de IA**: seleccionable independientemente del adaptador del monitor. No cambia el dispositivo gráfico de CEF ni mueve texturas entre GPUs sin validación.
- **Avanzado → Hilos de CPU**: cero conserva la selección automática; los demás valores se aplican a los nuevos trabajadores que admiten el ajuste.
- **Restaurar automático** selecciona los valores predeterminados; **Aplicar** los guarda.
- **Actualizar detección** vuelve a consultar el hardware sin ejecutar inferencia.
- **Exportar diagnóstico** guarda hardware, componentes instalados, selecciones y resultados. No incluye cookies ni un volcado del entorno.

Las preferencias se guardan en `%LOCALAPPDATA%\Prometheus\processing-v1.json`, fuera del proyecto. El archivo vecino `processing-v1.results.json` conserva comprobaciones informativas con una firma del hardware, controlador y runtime. No almacena fotogramas, texturas ni recorridos MGT. Un cambio del runtime invalida estas comprobaciones; la próxima tarea verifica de nuevo su motor.

Los overrides externos existentes conservan prioridad sobre el trabajo, y el trabajo sobre la preferencia local. El diagnóstico muestra los nombres de los overrides presentes. Para recuperar un ajuste manual de una GPU retirada, elegir otra GPU, CPU o restaurar Automático. Dos adaptadores físicamente idénticos se consideran ambiguos y sus claves de sesión no se reutilizan silenciosamente tras un cambio de identidad.

## Exportación y recuperación

La opción Automático del diálogo respeta Procesamiento; GPU o CPU son elecciones explícitas para esa exportación. NVIDIA conserva la ruta de texturas de Media Foundation. AMF recibe BGRA mediante una tubería local con lectura desde GPU; no se anuncia como un flujo sin copias. ProRes mantiene su codificador CPU y WAV su mezcla de audio. La corrección 3.19.10 para órdenes de audio extensas sigue activa.

Al reanudar un trabajo recuperable se mantiene el codificador de los segmentos existentes. Para cambiarlo debe crearse una exportación nueva. No se mezclan segmentos de distintos codificadores como recuperación automática.

## Diagnóstico y MCP

`Prometheus.Client.exe --graphics-diagnostics diagnostico.json` agrega el informe de procesamiento al diagnóstico gráfico. `--processing-settings` abre solamente los ajustes, sin un proyecto. MCP mantiene las once herramientas; `get_state` con `scope: processing` devuelve este inventario sin cargar modelos.

## Alcance de las pruebas

La validación final de la entrega se adjunta en `Validacion/` y en el estado de la entrega. El equipo de integración es Ryzen 9 3900, RTX 3060 y 32 GB de RAM. Los informes `SAM_AMD_APU.md` y `SAM_SEGUIMIENTO_AMD_APU.md` describen pruebas de la rama 3.21.x en Ryzen 3 7320U / Radeon 610M; no certifican este binario unificado ni otros modelos AMD. Los tiempos de inicialización incluyen la carga del modelo y no equivalen a los clics con un fotograma ya preparado.

## Después de esta entrega

- **3.22.3:** calibración opcional y perfiles de rendimiento medidos por equipo; no se ejecuta al abrir el programa.
- **3.22.4:** validación conjunta en NVIDIA, APU y equipos híbridos, y cierre de publicación del paquete unificado.

No se añaden ROCm, Vulkan, un motor nuevo de MGT, prerenderizados ni cachés de recorridos de texturas. Se conserva el formato actual y el flujo vivo.
