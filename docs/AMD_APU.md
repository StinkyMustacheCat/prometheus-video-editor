# Prometheus AMD APU

Variante de Prometheus para Windows x64 con APU de AMD. El objetivo es conservar los proyectos, las herramientas y la fidelidad de composición. El Ryzen 3 7320U con Radeon 610M y 8 GB de RAM es el primer equipo de referencia. La matriz de otras generaciones queda para la etapa de compatibilidad.

| Referencia | Identificador |
| --- | --- |
| Origen | Prometheus `v3.19.9`, commit `44a2d6b` |
| Rama de trabajo | `feature/amd-apu-compatibility` |
| Primer candidato de la variante | `Prometheus AMD APU 3.20.2` |
| Candidato actual | `Prometheus AMD APU 3.21.2` |
| Nombre del ZIP portable | `Prometheus-3.21.2-AMD-APU-x64.zip` |

## Ruta de versiones

- **3.20.0–3.20.1** — Diagnóstico, reproducción, composición Direct3D 11 y gráficos MGT con CEF.
- **3.20.2** — Exportación H.264 y HEVC con AMD AMF y alternativa CPU. Validación local con un montaje a 1080p SDR que incluye vídeo, audio y MGT.
- **3.20.3** — Whisper CPU INT8 con el modelo Small, tiempos por palabra y Wordline. Incluye el runtime local de Whisper en el candidato portable.
- **3.21.0** — Segmentación por puntos con SAM 2.1 Small a 1024 en DirectML. El [informe de SAM](SAM_AMD_APU.md) documenta la comparación con WebGPU y la referencia de PyTorch en este equipo.
- **3.21.1** — Seguimiento temporal SAM con codificador DirectML y propagación PyTorch CPU. [Resultados de seguimiento](SAM_SEGUIMIENTO_AMD_APU.md)
- **3.21.2** — Preparación del siguiente fotograma con DirectML durante la propagación temporal en CPU. [Mediciones a frecuencia original](SAM_RENDIMIENTO_AMD_APU.md)
- **3.22.0 (propuesta)** — Validación en varias generaciones de APU AMD y selección automática por equipo.

## Comprobaciones del candidato

Usa el botón MCP del editor y el puente local de la carpeta `McpBridge` para repetir comprobaciones de transcripción y exportación. La guía [Conexión MCP local](MCP.md) describe el transporte y las herramientas disponibles.

La validación de esta variante se registra por versión y equipo. Los proyectos existentes usan el formato `.pthproj` y el ejecutable del portable se llama `Prometheus.Client.exe`.

El [informe de integración](AMD_APU_INTEGRACION.md) registra los hallazgos y las pruebas que deben repetirse al combinar esta rama con el proyecto principal.

## Whisper en el Ryzen 3 7320U

Prueba exploratoria con dos extractos de voz en español de 70 segundos. El modelo Small y sus ajustes de reconocimiento fueron los mismos en todas las pruebas. Cada medición inició un proceso nuevo y contó carga del modelo e inferencia. Los tiempos pueden variar según el audio y la carga del equipo.

| Muestra | Ruta base | CPU con cuatro hilos | Palabras | Diferencia en marcas de tiempo |
| --- | ---: | ---: | ---: | ---: |
| 1 | 35.4 s | 33.0 s | 171 | 0 ms |
| 2 | 53.0 s | 43.3 s | 185 | 0 ms |

El pico medido de memoria de trabajo del proceso fue de unos 690 MiB. El ensayo por MCP usó un clip recortado de 24 segundos a velocidad 1,25×, con silencio en la primera pista y voz en la segunda. Generó 76 palabras y 76 subtítulos. Guardó y reabrió el proyecto, verificó el ciclo de exportar y leer Wordline, y canceló trabajos en cola y activos.

Grabaciones de prueba de LibriVox. [Muestra 1](https://commons.wikimedia.org/wiki/File:Quijote_vol1_11d_cervantes.mp3) · [Muestra 2](https://commons.wikimedia.org/wiki/File:Quijote_vol1_09c_cervantes.mp3)
