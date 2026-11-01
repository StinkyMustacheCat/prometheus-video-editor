# Prometheus

![Version](https://img.shields.io/badge/version-1.0.0-blue.svg)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-green.svg)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-lightgrey.svg)
![Framework](https://img.shields.io/badge/.NET-Framework%204.8-purple.svg)
![Tests](https://img.shields.io/badge/tests-8%20suites-blue.svg)

**Prometheus** es un editor audiovisual no lineal (NLE) modular de escritorio para Windows, enfocado en el montaje rápido, composición multicapa, subtitulado inteligente y exportación acelerada por hardware.

---

## Descarga de Paquetes Portátiles (Releases)

Para utilizar Prometheus sin necesidad de compilar el código fuente, descarga los paquetes binarios precompilados desde la página [**Releases**](https://github.com/StinkyMustacheCat/prometheus-video-editor/releases).

La distribución portable se publica como **tres archivos ZIP independientes**. Cada ZIP conserva la misma carpeta raíz interna `Prometheus-1.0.0/`; no son volúmenes de un único archivo comprimido.

* **[`Prometheus-1.0.0-windows-x64.part1.zip`](https://github.com/StinkyMustacheCat/prometheus-video-editor/releases/download/v1.0.0/Prometheus-1.0.0-windows-x64.part1.zip) (~365 MB) — Editor Base y Render:**
  * Aplicación completa `Prometheus.exe`, interfaz gráfica, librerías, visor MGT y suite de **FFmpeg 9.0 con aceleración NVIDIA NVENC / CPU**.
  * Si solo necesitas editar, componer y exportar vídeo sin las funciones de IA, este archivo es 100% funcional por sí mismo.
* **[`Prometheus-1.0.0-windows-x64.part2.zip`](https://github.com/StinkyMustacheCat/prometheus-video-editor/releases/download/v1.0.0/Prometheus-1.0.0-windows-x64.part2.zip) (~1.75 GB) — Aceleración PyTorch CUDA:**
  * Entorno acelerado por GPU CUDA 12.6 para inferencia local de inteligencia artificial.
* **[`Prometheus-1.0.0-windows-x64.part3.zip`](https://github.com/StinkyMustacheCat/prometheus-video-editor/releases/download/v1.0.0/Prometheus-1.0.0-windows-x64.part3.zip) (~1.55 GB) — Modelos de IA Whisper y SAM 2.1:**
  * Modelos neuronales y librerías de inferencia para transcripción/subtitulado automático y recorte inteligente de máscaras.

> **Instalación:**
> `part1` es suficiente para el editor y la exportación estándar; descarga también `part2` y `part3` para las funciones de IA. Extrae cada archivo `.zip` **por separado** al mismo destino y conserva la carpeta raíz interna. Los archivos quedarán en la carpeta común `Prometheus-1.0.0\`. No selecciones los tres ZIP como si fueran un archivo dividido: no se fusionan automáticamente como volúmenes.
>
> Reserva **16 GB de espacio libre** durante la descarga y extracción de los tres paquetes completos. Una vez extraídos, ocupan aproximadamente **6.1 GB** en conjunto.
>
> Las sumas de comprobación SHA-256 están en [`SHA256SUMS.txt`](https://github.com/StinkyMustacheCat/prometheus-video-editor/releases/download/v1.0.0/SHA256SUMS.txt), adjunto a la misma release.

> **Código fuente:** El código fuente está en `src/` dentro del repositorio y no está incluido en estos ZIP portátiles. Para compilar o modificar Prometheus, descarga o clona el repositorio; consulta [Compilación desde el Código Fuente](#compilación-desde-el-código-fuente).

---

## Características de la Versión 1.0.0

* **Pipeline de Exportación Acelerado (GPU / CPU):**
  * **Arquitectura Audio-First:** Procesamiento y masterizado previo de la pista de audio completa a WAV estéreo a máxima velocidad, desacoplándola del renderizado de vídeo para evitar cuellos de botella de entrelazado PTS/DTS.
  * **Aceleración por GPU (NVIDIA NVENC):** Codificación por hardware calibrada (`h264_nvenc`, preset `p4` con cuantización espacial adaptativa `-spatial-aq 1` y decodificación asistida `-hwaccel auto`).
  * **Particionado en tramos (Multi-Chunk Rendering):** División adaptativa en fragmentos de 60 segundos ejecutados concurrentemente bajo semáforo multihilo (`SemaphoreSlim`), eliminando las limitaciones de longitud de línea de comandos en Windows.
  * **Selector de aceleración en la interfaz:** Desplegable dinámico para alternar entre `GPU · NVIDIA NVENC (Acelerado)` y `CPU · Software libx264 (Compatible)` con monitorización de porcentaje en tiempo real.
  * **Ensamble bitstream sin pérdidas:** Fusión instantánea de tramos de vídeo y pista de audio en ~1 segundo vía copia directa (`-c:v copy -c:a aac -movflags +faststart`).
  * **Diagnóstico de errores:** Notificación explícita del tramo de tiempo `[MM:SS - MM:SS]` y del medio causante si se produce una interrupción.

* **Perfiles de Preparación Automática de Medios:**
  * **Desentrelazado inteligente:** Detección de orden de campos (`tt`, `bb`, `tb`, `bt`) y desentrelazado adaptativo de alta calidad con `bwdif` (doble cadencia) y fallback a `yadif`.
  * **Tone-mapping HDR a SDR:** Mapeo de curvas de transferencia PQ / SMPTE2084 / BT.2020 a BT.709 SDR mediante el algoritmo de Hable, previniendo aberraciones de color o sobreexposición.
  * **Preservación de fuentes originales:** El archivo fuente original se mantiene intacto (`role="original"`), asociando la copia procesada a `cache/prep/` para previsualización y exportación.

* **Compatibilidad Multiformato Ampliada:**
  * Admisión e indexación de contenedores `.mp4`, `.mov`, `.mkv`, `.webm`, `.avi`, `.m4v`, `.ts`, `.mts`, `.m2ts` y `.mxf`.
  * Indexación profunda de streams, FPS, cadencia variable (VFR), rotación, metadatos de color, canal alfa y pistas de audio.
  * Verificación rigurosa en 4 etapas: decodificación básica, búsqueda exacta por fotograma (*frame-accurate seek*), proxy ligero y exportación a disco.

* **Composición Visual y Modos de Fusión:**
  * Modos *Diferencia* y *Multiplicar* con precisión matemática en espacio `format=rgba`, eliminando la contaminación cromática marrón sobre textos y elementos con transparencia.
  * Corrección de fotogramas negros al ocultar pistas superiores en el monitor y en la exportación final.

---

## Requisitos del Sistema

### Mínimos
* **Sistema Operativo:** Windows 10 (64-bit) o Windows 11.
* **Procesador:** CPU Intel Core i5 / AMD Ryzen 5 (4 núcleos o superior).
* **Memoria RAM:** 8 GB.
* **Almacenamiento:** **16 GB de espacio libre** recomendado para descargar y extraer los tres ZIP completos; después de extraídos ocupan aproximadamente **6.1 GB**. `part1` por sí solo requiere menos espacio.
* **Runtime:** Microsoft .NET Framework 4.8 (incluido de forma nativa en Windows 10/11).

### Recomendados
* **Procesador:** CPU de 8 núcleos o superior.
* **Memoria RAM:** 16 GB o más.
* **Tarjeta Gráfica:** NVIDIA GeForce GTX 1650, RTX 20/30/40 o superior con drivers actualizados (NVIDIA Studio o Game Ready) para aceleración NVENC y soporte CUDA.

---

## Compilación desde el Código Fuente

El repositorio está organizado bajo el estándar oficial de proyectos .NET y C#:

```text
Prometheus/
├── Prometheus.sln       (Solución estándar para Visual Studio / Rider / VS Code)
├── Build.ps1            (Compilación automatizada en línea de comandos)
├── Test.ps1             (Ejecutor autónomo de pruebas unitarias e integración)
├── src/
│   ├── Prometheus.Core/         (Modelos, estado y lógica central)
│   ├── Prometheus.Media/        (Indexación, perfiles de preparación y decodificación)
│   ├── Prometheus.Rendering/    (Pipeline GPU NVENC, composición y multi-chunk)
│   └── Prometheus/              (Interfaz gráfica de usuario de escritorio WinForms)
├── tests/               (Baterías de pruebas unitarias y de integración)
├── lib/                 (Ensamblados WebView2 de referencia)
├── docs/                (Documentación arquitectónica)
└── licenses/            (Licencias y auditoría de componentes de terceros)
```

### Opciones de Compilación:

#### Opción 1: Visual Studio / IDE
Abre `Prometheus.sln` en Visual Studio (2019/2022 o superior) o JetBrains Rider y presiona **Compilar solución** (`Ctrl+Shift+B`).

#### Opción 2: Línea de Comandos Automatizada
```powershell
# Compila toda la solución de forma autónoma generando los binarios en bin/
powershell -ExecutionPolicy Bypass -File .\Build.ps1
```

---

## Suite de Pruebas Automatizadas Reproducibles

Para verificar de forma independiente la integridad y estabilidad del motor:

```powershell
# Ejecuta la suite completa de 8 baterías de pruebas unitarias e integración
powershell -ExecutionPolicy Bypass -File .\Test.ps1
```

El runner exige las ocho suites y FFmpeg/FFprobe en `.\runtime\`. Si el runtime está en otro paquete, indica la carpeta que contiene `runtime\` con `-RuntimeRoot` o con la variable `PROMETHEUS_TEST_RUNTIME`. La suite de exportación GPU requiere además una GPU NVIDIA, controladores compatibles y FFmpeg compilado con NVENC. Si falta una suite o un prerequisito, el runner termina con error en lugar de omitir pruebas.

El script compila automáticamente las pruebas y valida:
1. Regresión 0.10.58 (Core y gestión de efectos).
2. Regresión 0.10.59 (Transiciones y pistas de audio).
3. Regresión 0.10.62 (Línea de tiempo y medios anidados).
4. Hito 0.10.63 (Desbloqueo de exportación y reporte detallado de errores).
5. Hito 0.10.64 (Perfiles de preparación HDR y desentrelazado).
6. Hito 0.10.66 (Modos de fusión Diferencia/Multiplicar sobre alphas).
7. Hito 0.10.66 (Eliminación de fotograma negro en capas inferiores).
8. Pipeline de exportación acelerada (GPU NVENC y CPU libx264).

---

## Licencia

Prometheus se distribuye bajo los términos de la **GNU General Public License versión 3 (GPL-3.0)**.
Consulta el archivo [`LICENSE`](LICENSE) para obtener los términos y condiciones completos de uso, modificación y distribución.

Los avisos de atribución y licencias de los componentes externos incluidos (FFmpeg, SAM 2, Whisper, Playwright, Three.js, yt-dlp, WebView2) se detallan en [`licenses/TERCEROS.md`](licenses/TERCEROS.md).
