# Rendimiento del seguimiento SAM en Prometheus AMD APU 3.21.2

Pruebas en Windows x64, Ryzen 3 7320U, Radeon 610M y 8 GB de RAM. La rama se dirige a APU de AMD. Los resultados corresponden a este equipo de referencia.

## Comparación a la frecuencia del archivo original

Se midieron dos tramos de 14 fotogramas consecutivos del vídeo original, a 504 × 360 y 26,593 fps. En el tramo de movimiento el personaje pasa de estar de pie a agacharse y taparse la cara. Se usó el mismo archivo, el mismo SAM 2.1 Small FP32 y la misma semilla en cada par.

| Tramo y fotogramas de origen | 3.21.1 secuencial | 3.21.2 con preparación en paralelo | Reducción de tiempo |
| --- | ---: | ---: | ---: |
| Control, 65–78 | 106,70 s | 75,01 s | 29,7 % |
| Movimiento, 76–89 | 107,54 s | 75,35 s | 29,9 % |

El tramo de control se usó para comprobar el prototipo. El tramo de movimiento se midió con la implementación del candidato. Las 28 máscaras binarias coincidieron píxel por píxel con las de la referencia secuencial. En el tramo de movimiento, el codificador acumuló 47,22 s y la atención temporal 54,82 s en la referencia. La lectura del fragmento con FFmpeg en el banco de pruebas tardó 0,25 s. Los tiempos de inferencia incluyen preparar el estado temporal y emitir las máscaras con los modelos ya cargados.

El pico de memoria de trabajo del proceso en el tramo de movimiento pasó de 3.212,5 a 3.458,8 MiB. La memoria incluye el runtime y sus bibliotecas. Cada sesión DirectML ejecuta una sola inferencia a la vez, y un trabajador prepara como máximo el siguiente fotograma mientras la CPU propaga el actual. La integración conserva la resolución del modelo, la frecuencia de muestreo, el estado temporal y el formato de proyectos. [Restricciones de concurrencia de DirectML en ONNX Runtime](https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html)

Para comparar en otro equipo, iniciar el programa con `PROMETHEUS_SAM2_TRACK_PIPELINE=0` devuelve a DirectML su ejecución secuencial. Esta opción solo afecta al seguimiento con DirectML. La matriz de compatibilidad y rendimiento de otras generaciones de APU está pendiente.

## Pruebas en el editor mediante MCP

El proyecto usa un recorte en la línea de tiempo del mismo MP4 a partir de 2,632 s del original. El seguimiento recorrió 28 fotogramas a 26,593 fps y la reparación propagó la corrección en ambos sentidos. La cancelación de una ejecución activa tardó 21,43 s desde el inicio hasta su confirmación y dejó cero seguimientos aplicados. Un nuevo trabajo completó el seguimiento. Después se añadió una segunda semilla a la misma capa y se reparó.

| Operación | Tiempo total | Mediana de consulta MCP | Pico de memoria de trabajo muestreado |
| --- | ---: | ---: | ---: |
| Seguimiento de 28 fotogramas | 178,05 s | 1,5 ms | 2.796,9 MiB |
| Reparación desde el punto intermedio | 183,02 s | 1,5 ms | 3.138,7 MiB |

Se guardó el proyecto, se reabrió y se descomprimieron las 28 máscaras de 504 × 360 para verificar su tamaño y cobertura. Estos tiempos incluyen la carga del modelo, la obtención de los fotogramas, la propagación y la escritura de máscaras. El recorte de 28 fotogramas representa alrededor de un segundo de vídeo a la frecuencia original.

## Pruebas de regresión

La última suite completa pasó 508 de 508 pruebas. El primer pase volvió a presentar el fallo intermitente de recuperación de forma de onda registrado en 3.21.0. La comprobación pasó en la segunda ejecución completa.
