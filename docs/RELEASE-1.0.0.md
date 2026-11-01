# Notas de Lanzamiento: Prometheus 1.0.0

**Fecha de lanzamiento:** 31 de octubre de 2026  
**Versión:** 1.0.0 (Gold Master)  
**Licencia:** GNU General Public License v3.0 (GPL-3.0)  

---

## 1. Resumen Ejecutivo

Prometheus 1.0.0 culmina el proceso de estabilización técnica integral del motor audiovisual, superando los cuatro hitos críticos que impedían la producción profesional de secuencias extensas y proyectos multicapa complejos:

1. **0.10.63 — Desbloqueo y Aceleración de Exportación (NVENC + Audio-First):** Fin de las restricciones de línea de comandos de Windows, particionado temporal en tramos de 60 segundos, soporte para hardware NVIDIA NVENC, desacoplamiento del audio en una pasada previa directa a WAV sin pérdidas y diagnóstico preciso de errores en caso de fallo.
2. **0.10.64 — Perfiles de Preparación de Medios:** Detección y corrección de vídeo entrelazado (`bwdif`/`yadif`), tone-mapping HDR a SDR mediante el algoritmo de Hable, y preservación inmutable del medio original.
3. **0.10.65 — Índice y Compatibilidad Multiformato:** Compatibilidad certificada con 10 contenedores (`.mp4`, `.mov`, `.mkv`, `.webm`, `.avi`, `.m4v`, `.ts`, `.mts`, `.m2ts`, `.mxf`) y validación rigurosa en 4 etapas (decodificación, búsqueda frame-accurate, proxy y exportación).
4. **0.10.66 — Composición Visual y Modos de Fusión:** Corrección de artefactos cromáticos en modos Diferencia y Multiplicar sobre capas transparentes (RGBA limpio) y eliminación del fotograma negro al ocultar pistas.

---

## 2. Novedades y Mejoras Clave

### A. Pipeline de Exportación de Alto Rendimiento
* **Arquitectura Audio-First:** Se genera primero un archivo maestro de audio WAV a máxima velocidad sin compresión; los tramos de vídeo se renderizan de forma 100% pura con `-an`, eliminando los cuellos de botella de sincronía de reloj PTS/DTS y la duplicación de decodificadores de audio en procesos paralelos.
* **Aceleración por GPU (NVIDIA NVENC):** Codificación por hardware calibrada con preset `p4` estándar (`p3` en rápido) con cuantización espacial adaptativa `-spatial-aq 1` y decodificación por hardware `-hwaccel auto`.
* **Selector en Interfaz:** Desplegable de aceleración en `ExportDialog` con opción de GPU (NVIDIA NVENC) o CPU (libx264) y monitoreo dinámico en la ventana de progreso.

### B. Preparación y Tratamiento de Color
* Soporte nativo para material broadcast entrelazado con desentrelazado adaptativo de movimiento temporal `bwdif` de doble cadencia.
* Tone-mapping HDR Hable integrado que preserva el rango dinámico en monitores SDR estándar (BT.709) sin saturación ni aplastamiento de negros.
* Arquitectura de almacenamiento de representaciones con roles separados: `role="original"` para el archivo del usuario, y `role="final"` / `role="preview"` en el caché de preparación.

### C. Certificación de Formatos
* Se auditaron y validaron 10 contenedores multimedia en 4 etapas: decodificación básica, búsqueda frame-accurate, proxy ligero y exportación a disco.
* Metadatos de contenedor, streams, FPS, cadencia variable (VFR), color y escaneo registrados bajo `@namespace="prometheus.media.index"`.

### D. Composición Matemática y Fusión
* Reescritura del pipeline de modos de fusión en espacio `format=rgba` puro, eliminando la contaminación marrón en la composición de textos rasterizados y capas con transparencia.
* Corrección en el cálculo de visibilidad de recortes temporales que generaba fotogramas negros al apagar pistas superiores con contenido debajo.

---

## 3. Pruebas de Regresión y Validación

La suite automatizada contiene ocho baterías de regresión e integración:
* `PreV1Regression01058`: grupos de edición, duplicación, máscaras temporales, anidamiento y render de corte/transición.
* `Regression01059`: pistas de audio y mezcla al terminar un clip superior.
* `Regression01062`: medios anidados, contexto de instrucciones, procesamiento por lotes y render de títulos.
* `TestCompositionVisual`: diferencias de píxeles para los modos normal, Diferencia y Multiplicar.
* `TestBlackFrame`: comparación de color al ocultar la pista superior.
* `TestPrepProfiles`: perfiles HDR/entrelazado, representaciones e índice MKV.
* `TestExportErrorReporting`: fallo de medio corrupto con nombre y fragmento en el diagnóstico.
* `TestGpuAndCpuExport`: archivos H.264 verificables y progreso completo para NVENC y libx264.

Para repetirlas se requiere .NET Framework 4.8 y FFmpeg/FFprobe en `runtime\` del checkout o en un paquete indicado mediante `-RuntimeRoot` / `PROMETHEUS_TEST_RUNTIME`. La prueba NVENC requiere hardware NVIDIA y controladores compatibles. Consulta `Test.ps1` y el README para ejecutar la suite.
* **Benchmark en Proyecto Real (*La revolución gamer*):** Exportación probada y certificada sobre 117 clips reales, 10 pistas y hasta 31 capas simultáneas de composición.

---

## 4. Distribución de Binarios Portátiles (GitHub Releases)

El portable se distribuye en **tres archivos ZIP independientes**, cada uno con la misma raíz interna `Prometheus-1.0.0/`. No son volúmenes partidos de un único archivo comprimido:

1. **`Prometheus-1.0.0-windows-x64.part1.zip` (~350 MB):**
   * Editor completo, librerías, interfaz gráfica, motor de subtítulos MGT, WebView2 y suite FFmpeg/FFprobe con aceleración NVIDIA NVENC.
   * Funcional de forma autónoma para edición y exportación estándar.
2. **`Prometheus-1.0.0-windows-x64.part2.zip` (~1.4 GB):**
   * Entorno PyTorch CUDA acelerado por GPU para inferencia local de IA.
3. **`Prometheus-1.0.0-windows-x64.part3.zip` (~1.1 GB):**
   * Modelos neuronales y librerías de inferencia para OpenAI Whisper y Segment Anything (SAM 2.1).

> **Instalación:** `part1` basta para el editor y la exportación estándar; descarga también `part2` y `part3` para las funciones de IA. Extrae cada ZIP por separado en la misma carpeta de destino (por ejemplo, con "Extraer aquí") y conserva la raíz interna común, `Prometheus-1.0.0\`. No selecciones varios ZIP como si fueran partes de un volumen comprimido: no se fusionan automáticamente como volúmenes.

La distribución y el código fuente se rigen por la **GPL-3.0** indicada en [`LICENSE`](../LICENSE); las atribuciones y licencias de terceros están en [`licenses/TERCEROS.md`](../licenses/TERCEROS.md).
