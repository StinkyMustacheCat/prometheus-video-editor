# Seguimiento SAM en Prometheus AMD APU 3.21.1

Verificación en Windows x64, Ryzen 3 7320U, Radeon 610M y 8 GB de RAM. El codificador de imagen SAM 2.1 Small FP32 a 1024 se ejecuta con DirectML. El predictor de vídeo oficial conserva en PyTorch CPU la atención temporal, la memoria y la propagación de máscaras. El seguimiento de la edición estándar conserva su ruta CUDA/BF16. [Predictor de vídeo SAM 2.1 de Meta](https://github.com/facebookresearch/sam2/blob/main/sam2/sam2_video_predictor.py)

## Comparación con PyTorch FP32 en CPU

Ocho muestras del vídeo aportado, a 2 fps, desde el segundo 2,4 del original. Se empleó la misma máscara inicial y la misma configuración temporal en las tres rutas. El tramo contiene el paso de estar de pie a agacharse y cubrirse la cara. La parte visible del personaje permanece en la máscara después del gesto. El fragmento no muestra el regreso a la postura inicial.

| Codificador | Tiempo acumulado del codificador | Seguimiento total | Solapamiento mínimo con la referencia |
| --- | ---: | ---: | ---: |
| PyTorch CPU | 38,78 s | 67,33 s | Referencia |
| DirectML | 27,93 s | 54,56 s | 100 % |
| WebGPU | 36,66 s | 65,73 s | 100 % |

Los tiempos incluyen la preparación del estado temporal desde un modelo cargado hasta la máscara final. Las ocho máscaras binarias de ambos proveedores GPU coincidieron píxel por píxel con la referencia en este tramo. DirectML redujo un 19 % el tiempo de seguimiento medido y se seleccionó como opción automática para este equipo. El protocolo conserva el tamaño del vídeo. El seguimiento usa la frecuencia medida en el archivo y puede recuperar el dato en proyectos que guardaban 60 fps de forma predeterminada. Se preparó un archivo reducido a 2 fps para repetir el ensayo de ocho fotogramas.

## Prueba en el editor mediante MCP

En el proyecto de prueba se importó el tramo a 2 fps y se crearon sus ocho máscaras. Se comprobó la cancelación activa con una copia que tenía el dato antiguo de 60 fps. El editor mostró el primer fotograma como 1 de 8 y la cancelación dejó la capa sin un seguimiento nuevo. Después se repitió el recorrido completo, se añadió una corrección en el segundo 1,5 y se reparó el seguimiento de la misma capa.

| Operación | Tiempo total | Tiempo mediano de una consulta MCP al editor | Pico de memoria de trabajo del predictor |
| --- | ---: | ---: | ---: |
| Seguimiento completo | 69,40 s | 1,0 ms | 2.956,5 MiB |
| Reparación desde el fotograma corregido | 67,60 s | 0,9 ms | 3.197,7 MiB |

Al guardar y reabrir se verificaron las dos semillas y el archivo de seguimiento con las ocho máscaras de 504 × 360. Se descomprimió cada máscara para comprobar que contenía el tamaño esperado y píxeles de primer plano. El tiempo de una consulta MCP mide la respuesta del editor durante la inferencia. El trabajo completo incluye preparación, decodificación, inferencia y guardado.

## Integración y componentes

- El worker informa progreso por fotograma. Las ventanas completadas se guardan en checkpoints. Los checkpoints AMD distinguen el proveedor del codificador y la ruta CUDA conserva sus claves anteriores.
- El exportador de imagen añade al nivel final un embedding de ausencia de memoria. La integración lo sustrae antes de entregar las características a SAM, para que el predictor temporal decida cuándo aplicarlo. Se comparten los grafos FP32 de 3.21.0. [Exportación ONNX fijada por revisión](https://huggingface.co/onnx-community/sam2.1-hiera-small-ONNX/tree/a7df49d8de14b9d2e4504d1687b0d568f905fd8d/onnx)
- El portable usa Python 3.11 y los paquetes PyTorch 2.8.0+cpu y torchvision 0.23.0+cpu en `Runtime/sam2/track-cpu`. La rama utiliza la copia del código SAM y el checkpoint Small de la distribución base. El SHA-256 de `sam2.1_hiera_small.pt` es `6d1aa6f30de5c92224f8172114de081d104bbd23dd9dc5c58996f0cad5dc4d38`. Las licencias de los paquetes acompañan al runtime.
- Para seleccionar el proveedor del seguimiento, `PROMETHEUS_SAM2_TRACK_BACKEND` admite `auto`, `directml`, `webgpu`, `torch-cpu` y `cuda`. La selección de máscaras de un fotograma mantiene su configuración propia, `PROMETHEUS_SAM2_BACKEND`.
- En MCP, `sam_seed` con el `mask_id` existente aplica una corrección a la misma capa. `sam_track` con `repair: true` usa esa semilla para actualizar el seguimiento existente. El resultado permanece en el formato de proyecto y almacén de máscaras de la base.

El ensayo cubre el tramo de baja resolución indicado. La propagación en CPU sigue siendo el límite para clips sostenidos a la frecuencia original.

## Regresión

La última suite completa pasó 508 de 508 pruebas. Una ejecución anterior falló en una comprobación de niveles de audio (`AudioMixPlanTests.LevelCache_ReportsSourcePeakAndTrackGain`), que pasó al repetir la suite.
