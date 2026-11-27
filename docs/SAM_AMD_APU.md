# SAM 2.1 en Prometheus AMD APU 3.21.0

Validación local del 26 de noviembre de 2026 en Windows x64, Ryzen 3 7320U, Radeon 610M y 8 GB de RAM. La adaptación usa SAM 2.1 Small FP32 con entrada de 1024 × 1024 al modelo. Toma la máscara con mayor puntuación y la devuelve al tamaño del fotograma del editor. El modo interactivo conserva las características del fotograma mientras se añaden o retiran puntos.

## Rendimiento y fidelidad

| Proveedor | Primera preparación | Preparaciones siguientes | Predicción con el fotograma preparado | Tiempo de CPU por preparación |
| --- | ---: | ---: | ---: | ---: |
| DirectML | 4,30 s | 3,09–3,11 s | 0,10–0,20 s | 3,05–3,81 s |
| WebGPU | 4,83 s | 4,40–4,47 s | 0,12–0,65 s | 0,20–0,97 s |
| ONNX CPU | 3,41 s | 3,52–3,90 s | 0,07–0,09 s | 13,41–15,22 s |

Mediciones secuenciales del trabajador Python del portable. La sesión ya estaba cargada al iniciar la primera preparación. El tiempo de CPU suma el trabajo en todos los núcleos. Se eligió DirectML por sus preparaciones posteriores más cortas y su menor consumo de CPU que ONNX CPU. WebGPU usó menos CPU y registró tiempos de preparación más altos. Ambos proveedores GPU ejecutaron operaciones del grafo en CPU. [Documentación de DirectML](https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html) · [Documentación de WebGPU](https://onnxruntime.ai/docs/execution-providers/WebGPU-ExecutionProvider.html)

Se compararon fotogramas a 1 y 5 segundos del vídeo suministrado (504 × 360), más una estampa en PNG. Para cada entrada se aplicaron un punto positivo y luego un punto negativo adicional. Las seis máscaras binarias de cada uno de los tres proveedores coincidieron píxel por píxel con SAM 2.1 Small PyTorch FP32 en CPU. La paridad medida corresponde a estas entradas y a ese modelo. [SAM 2.1 de Meta](https://github.com/facebookresearch/sam2) · [Estampa de The Met, CC0](https://commons.wikimedia.org/wiki/File:Horse_standing_on_a_field_in_profile_to_left_MET_DP879864.jpg)

## Prueba en el editor mediante MCP

El proyecto contiene el vídeo de 0 a 8 s y la imagen de 9 a 12 s, colocados en V1 por un changelist MCP. El editor guardó y reabrió el proyecto con sus tres máscaras.

| Posición en la línea de tiempo | Entrada | Máscara | Tiempo MCP |
| --- | --- | --- | ---: |
| 1 s | Vídeo | 1 punto positivo | 9,727–15,868 s |
| 5 s | Vídeo | 1 punto positivo y 1 negativo | 6,629–9,070 s |
| 10 s | Imagen | 1 punto positivo y 1 negativo | 7,229–7,831 s |

Los intervalos corresponden a dos recorridos completos. Cada trabajo MCP inicia un trabajador nuevo y su tiempo incluye la lectura del fotograma, la carga, la inferencia y el sondeo hasta obtener el resultado. Las mediciones con el fotograma ya preparado corresponden a una sesión que reutiliza el resultado del codificador.

El ensayo exploratorio de seguimiento temporal con el predictor de vídeo de Meta en CPU produjo ocho máscaras en 100,95 s, incluida la inicialización. Confirma que la ruta CPU se ejecuta y da un orden de magnitud del coste. La calidad temporal en oclusiones y una ruta acelerada para vídeo en la Radeon quedan para 3.21.1.

## Runtime y procedencia

El portable incluye los cuatro archivos ONNX FP32 en `Runtime/sam2/onnx` y usa el Python 3.11 que ya acompaña a Whisper. Los dos proveedores están aislados en `ort-directml` (onnxruntime-directml 1.24.4) y `ort-webgpu` (onnxruntime 1.32.0 y onnxruntime-ep-webgpu 0.6.0). Sus avisos y licencias acompañan a los paquetes. SAM 2.1 usa Apache-2.0. [Exportación ONNX fijada por revisión](https://huggingface.co/onnx-community/sam2.1-hiera-small-ONNX/tree/a7df49d8de14b9d2e4504d1687b0d568f905fd8d/onnx)

Para reconstruir el portable, preparar ese árbol de runtime junto al de Whisper y multimedia, con los paquetes Windows x64 para CPython 3.11. Instalar cada proveedor en su propia carpeta con `pip --target` y pasar el árbol a `Build.ps1 -RuntimeDirectory`. Los archivos externos `.onnx_data` deben quedar junto al grafo correspondiente.

| Archivo | SHA-256 |
| --- | --- |
| `vision_encoder.onnx` | `aacf1f7137bb6fffcf6bf166abcfabe28f57a76059254f3fb611c4a64a208119` |
| `vision_encoder.onnx_data` | `260fd1f0a34e72a3dc79a739e563b4facc0ba75504818b433a1f808e66637456` |
| `prompt_encoder_mask_decoder.onnx` | `079c59b261f723ff5c6a125e69b0170a957b21c58738c28d2b0394ecd0587d7f` |
| `prompt_encoder_mask_decoder.onnx_data` | `f9e59a584ab8ced21fa812c211bc01084204db1c9e92a5ef4fb3a49972b4e864` |

`PROMETHEUS_SAM2_BACKEND` permite elegir `auto` (DirectML en el portable), `directml`, `webgpu`, `onnx-cpu` o `cuda`. El seguimiento integrado y la selección `cuda` requieren el runtime CUDA de la edición estándar. El formato de proyecto sigue guardando máscaras SAM con los mismos campos.

## Pruebas de regresión

La última ejecución completa pasó 508 de 508 pruebas. En dos ejecuciones anteriores fallaron de forma intermitente una comprobación de recuperación de formas de onda y la eliminación de un PNG temporal de la prueba de bordes SAM. Ambas pasaron en la última ejecución.
