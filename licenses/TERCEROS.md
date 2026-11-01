# Atribución y Licencias de Componentes de Terceros

Este documento detalla todas las herramientas, bibliotecas y modelos de terceros integrados, distribuidos o consumidos por **Prometheus v1.0.0**, en estricto cumplimiento de los términos de la **GNU General Public License v3.0 (GPL-3.0)** y las licencias de cada autor original.

---

## Tabla Resumen de Licencias

| Componente | Versión | Licencia Principal | Compatibilidad GPL-3.0 | Uso en Prometheus |
| :--- | :--- | :--- | :---: | :--- |
| **FFmpeg / FFprobe** | 9.0.1 (Gyan Essentials) | **GPL-3.0** | Idéntica | Motor de decodificación, composición y exportación audiovisual |
| **SAM 2.1 (Segment Anything)** | 2.1 Small (Meta AI) | **Apache 2.0** | Compatible | Máscaras inteligentes y segmentación temporal de vídeo |
| **OpenAI Whisper** | Embed Runtime | **MIT** | Compatible | Transcripción de audio a texto y generación de subtítulos |
| **Playwright Core** | 1.x (Microsoft) | **Apache 2.0** | Compatible | Renderizado y animación de plantillas MGT y gráficos web |
| **Three.js** | r128 | **MIT** | Compatible | Visor y renderizador 3D de subtítulos tipográficos MGT |
| **yt-dlp** | 2026.08.19 | **Unlicense / GPL-2.0+** | Compatible | Asistente de consulta y descarga de metadatos web |
| **Microsoft.Web.WebView2** | 1.0.x (Microsoft) | **BSD-3-Clause** | Compatible | Control incrustado de navegador Chromium para la interfaz |
| **Microsoft .NET Framework** | 4.8 (Microsoft) | Propietaria SO | N/A (Sistema) | Entorno de ejecución de Windows (no redistribuido) |

---

## Detalle de Componentes y Cumplimiento

### 1. FFmpeg y FFprobe
* **Licencia:** GNU General Public License v3.0 (`GPL-3.0`)
* **Proveedor de la compilación:** `gyan.dev` (compilación 9.0.1-essentials_build).
* **Parámetros de compilación:** Configurado con `--enable-gpl --enable-version3`, soporte CUDA/NVENC/NVDEC y bibliotecas libres asociadas. Ver archivo [`FFmpeg-build.txt`](FFmpeg-build.txt) para la orden completa.
* **Hashes SHA-256:**
  * `ffmpeg.exe`: `72A489ECCD008C2EC2C0A5856C5C75BC3D8BBFA90166C4566865C246445E6AA3`
  * `ffprobe.exe`: `19202B23C0043F15AD1B7BCE2344F406FD52BD6EFD8F995CE02E7392A1CEC52F`
* **Código fuente:** https://ffmpeg.org/download.html y https://www.gyan.dev/ffmpeg/builds/
* **Aviso:** No se modificaron los binarios. Se invocan como procesos externos mediante tuberías estándar de comunicación entre procesos (IPC).

### 2. Segment Anything Model (SAM 2.1)
* **Licencia:** Apache License, Version 2.0 (`Apache 2.0`)
* **Autor:** Meta AI Research
* **Ubicación:** `runtime/sam2/`
* **Código fuente:** https://github.com/facebookresearch/sam2
* **Aviso de patente y copyright:** Conserva su archivo `LICENSE` original de Apache 2.0 en `runtime/sam2/repo/LICENSE`.

### 3. OpenAI Whisper
* **Licencia:** MIT License (`MIT`)
* **Autor:** OpenAI
* **Ubicación:** `runtime/whisper/`
* **Código fuente:** https://github.com/openai/whisper
* **Aviso:** Utilizado para el reconocimiento automático del habla (ASR) e inferencia local en CPU/GPU.

### 4. Playwright Core y Node Runtime
* **Licencia:** Apache License, Version 2.0 (`Apache 2.0`)
* **Autor:** Microsoft Corporation
* **Ubicación:** `mgt-engine/runtime/playwright-core/`
* **Código fuente:** https://github.com/microsoft/playwright

### 5. Three.js
* **Licencia:** MIT License (`MIT`)
* **Autor:** Ricardo Cabello (mrdoob) y colaboradores de Three.js
* **Ubicación:** `runtime/mgt-subtitle-view/licenses/Three-LICENSE.txt`
* **Código fuente:** https://github.com/mrdoob/three.js

### 6. yt-dlp
* **Licencia:** The Unlicense / GPL-2.0+ (con componentes GPLv3 incorporados en el standalone)
* **Hash SHA-256:**
  * `yt-dlp.exe`: `66674953FE251B89F4D08C5F0E35E0728679BD67AB3D7D05C0562AF101DD3E7A`
* **Código fuente:** https://github.com/yt-dlp/yt-dlp
* **Avisos adicionales:** Consulta [`yt-dlp-LICENSE.txt`](yt-dlp-LICENSE.txt) y [`yt-dlp-THIRD_PARTY_LICENSES.txt`](yt-dlp-THIRD_PARTY_LICENSES.txt).

### 7. Microsoft WebView2 Runtime & SDK
* **Licencia:** BSD-3-Clause / Microsoft Software License
* **Ubicación:** Archivos de aviso en `licenses/webview2/` (`LICENSE.txt` y `NOTICE.txt`).
* **Archivos distribuidos:** `Microsoft.Web.WebView2.Core.dll`, `Microsoft.Web.WebView2.WinForms.dll`, `WebView2Loader.dll`.
* **Aviso:** Se redistribuyen de conformidad con la licencia de redistribución de bibliotecas nativas de Microsoft Edge WebView2.

---

## Código Fuente de Prometheus

El código fuente de Prometheus se encuentra en [`src/`](../src/) bajo licencia **GPL-3.0**. Todo usuario o desarrollador tiene derecho a inspeccionar, modificar y recompilar el software utilizando el script de compilación incluido [`Build.ps1`](../Build.ps1). El texto completo de la licencia está en [`LICENSE`](../LICENSE).
