# Registro de Cambios (Changelog)

Todas las modificaciones notables de Prometheus se documentan en este archivo.

El formato se basa en [Keep a Changelog](https://keepachangelog.com/es-ES/1.0.0/), y este proyecto se adhiere a [Semantic Versioning](https://semver.org/lang/es/).

---

## [1.0.0] - 2026-10-31

### Añadido
* **Arquitectura de Exportación Audio-First Pass:** Desacoplamiento del procesamiento de audio del renderizado de vídeo por tramos (técnica inspirada en Premiere y DaVinci Resolve). El máster de audio se procesa primero de forma íntegra a WAV sin compresión, permitiendo que los tramos de vídeo se codifiquen de forma 100% pura (`-an`) sin esperas ni desajustes de sincronización PTS/DTS.
* **Calibración de Aceleración por GPU (NVIDIA NVENC):** Codificación nativa por hardware calibrada con preset `p4` estándar (`p3` en modo rápido) y cuantización espacial adaptativa activada por hardware (`-spatial-aq 1`), junto a decodificación asistida (`-hwaccel auto`).
* **Selector de Aceleración en Diálogo de Exportación:** Selector desplegable explícito en `ExportDialog` para alternar entre `GPU · NVIDIA NVENC (Acelerado)` y `CPU · Software libx264 (Compatible)` con reporte en tiempo real en la barra de progreso.
* **Perfiles de Preparación Automática (0.10.64):**
  * Detección automática y desentrelazado de campos con filtro de alta calidad `bwdif` (modo `send_frame`, doble cadencia fluida) con fallback a `yadif`.
  * Mapeo de tonos HDR a SDR mediante el algoritmo de Hable para curvas SMPTE2084 / BT.2020 a BT.709 estándar sin pérdida de información de color.
  * Preservación inmutable de la fuente original (`role="original"`), registrando versiones normalizadas en `cache/prep/` para `role="final"` y `role="preview"`.
  * Extensión `@namespace = "prometheus.media.prep-profile"` con registro de perfil, dimensiones y espacio de color resultante.
* **Índice de Metadatos y Compatibilidad Multiformato (0.10.65):**
  * Indexación profunda de contenedor, streams de vídeo/audio, FPS, cadencia variable (VFR), orientación, orden de escaneo, canal alfa y distribución multicanal.
  * Ampliación de contenedores certificados: `.mp4`, `.mov`, `.mkv`, `.webm`, `.avi`, `.m4v`, `.ts`, `.mts`, `.m2ts` y `.mxf`.
  * Validación en 4 etapas: decodificación básica, búsqueda exacta por fotograma (*frame-accurate seek*), generación de proxy ligero y exportación a disco.
  * Extensión `@namespace = "prometheus.media.index"` integrada en el modelo del proyecto.

### Corregido
* **Desbloqueo de Exportación (0.10.63):** Eliminación del límite de longitud de línea de comandos Win32 mediante renderizado particionado por tramos temporales adaptativos de 60 segundos con solapamiento seguro de cortes.
* **Diagnóstico de Errores de Renderizado:** Detección y notificación exacta del tramo temporal `[MM:SS - MM:SS]` y del medio activo causante de cualquier fallo en la exportación, con volcado del log de depuración de FFmpeg.
* **Modos de Fusión Visual Diferencia y Multiplicar (0.10.66):** Eliminación de la contaminación marrón/verdosa sobre capas de texto y elementos con canal alfa, forzando la conversión matemática a `format=rgba` en todas las capas antes de la composición.
* **Eliminación del Fotograma Negro (0.10.66):** Corrección del cálculo de solapamientos en `PreviewWindow` para descartar pistas visuales deshabilitadas (`!track.enabled`), asegurando que las capas inferiores visibles se rendericen sin interrupciones negras.

---

## [0.10.62] - 2026-10-04
* Inicia la preparación del audio web al pegar o arrastrar el enlace directamente y permite reintentar con cookies desde el aviso de exportación.
* Permite copiar el comando de yt-dlp con el navegador elegido desde el diálogo de acceso.
* Crea pistas inferiores al arrastrar audio y ajusta la altura de la onda al volumen del clip y de la pista.
* Apaga el modo de fusión al terminar el clip, también durante la reproducción.
* Permite elegir la fuente del texto y ofrece el título de dos líneas «Letras en cascada» como medio anidado animado.
* Fija los separadores de los tiempos de descarga, evita reconstrucciones innecesarias de controles y limpia la pista temporal de Fondo desenfocado.
* Permite anidar un solo medio y elegir una o varias pistas al copiar contexto para Instrucciones.
* Aplica lotes grandes de Instrucciones en segundo plano con progreso visible y un solo paso de deshacer.

---

## [0.10.53] - 2026-09-20
* La máscara inteligente vuelve a usar exclusivamente SAM 2.1 Small; se retiran el código, el modelo y las referencias de MobileSAM.
* El panel Instrucciones distribuye sus acciones en varias filas cuando se estrecha.
* La pestaña inferior de Fotogramas clave dispone de altura suficiente para mostrarse completa.
* La rueda sobre el selector de modo de fusión no cambia accidentalmente la selección.
* La vista al arrastrar el lector respeta la visibilidad de subtítulos.

---

## [0.10.50] - 2026-09-01
* Pegar audio reconoce los mismos formatos que la importación.
* Extracción de streams compatibles desde subtítulos embebidos.
* Inserción de vídeo con audio vinculado en pistas independientes sin sobrescribir pistas existentes.
* Visualización en monitor y proxies respetando rotación y píxeles no cuadrados.
* Atajos de teclado avanzados: Shift+clic, Ctrl+Shift+Z, Shift+Supr, Ctrl+Alt+V y S para ajuste magnético.
* Soporte para importación de imágenes locales WebP, BMP, TIFF, AVIF y SVG.
