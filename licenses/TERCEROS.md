# Componentes de terceros — Prometheus 3.22.2

Prometheus usa GPL-3.0-or-later. Cada dependencia conserva sus condiciones, titulares y avisos originales; la licencia del editor no sustituye las de los componentes.

| Componente | Licencia principal y avisos |
| --- | --- |
| Avalonia, CommunityToolkit.Mvvm, Vortice, NAudio | MIT; textos y dependencias nativas en `nuget/` |
| Jint / Acornima | BSD-2-Clause / BSD-3-Clause, en `nuget/` |
| CEF / CefSharp / Chromium | BSD y avisos de componentes, en `vendors/` y `nuget/` |
| SkiaSharp, HarfBuzzSharp y ANGLE | Avisos de envolturas y bibliotecas nativas, en `nuget/` |
| Inter | OFL-1.1, `vendors/Inter-OFL.txt` |
| .NET autónomo | MIT y avisos originales, en `dotnet/` |
| Visual C++ Redistributable x64 | Condiciones propias de Microsoft; instalador original firmado en `Prerequisitos/` y avisos en `vendors/` |
| FFmpeg / ffprobe `9.1-prometheus-3.19.9`, Rubber Band 4.0.0 | GPLv3 o posterior; fuentes, recetas y verificación de la base conservadas |
| yt-dlp 2026.08.19 | Fuente Unlicense; ejecutable combinado GPLv3 o posterior y avisos adicionales |
| Python 3.11 | PSF y avisos de sus bibliotecas, en `python/` |
| PyTorch 2.8.0 CUDA / torchvision 0.23.0 / torchaudio 2.8.0 | BSD-3-Clause / BSD-3-Clause / BSD-2-Clause y componentes transitivos, en `python/` |
| Faster Whisper / CTranslate2 / ONNX Runtime de Whisper | MIT y avisos nativos, en `python/` |
| PyAV 18.1.0 | BSD-3-Clause del código; vía GPLv3 o posterior para el conjunto binario documentada en la base y sus fuentes |
| Whisper small | MIT, tarjeta y revisión en `vendors/` |
| SAM 2.1 | Apache-2.0; ccTorch BSD-3-Clause; fuentes en el runtime y avisos en `vendors/` |
| CUDA 12.6 / cuDNN 9.10.2.21 | Términos NVIDIA separados, en `vendors/` y `python/` |
| Certifi / tqdm | Incluyen material MPL-2.0; fuentes de la base adjuntas |
| ONNX Runtime DirectML 1.24.4 | MIT y términos/avisos de componentes nativos incluidos en el paquete |
| ONNX Runtime 1.32.0 / complemento WebGPU 0.6.0 | Avisos originales del paquete y componentes transitivos |
| PyTorch 2.8.0+cpu / torchvision 0.23.0+cpu | BSD y avisos transitivos, aislados en `Runtime/sam2/track-cpu` |

El inventario general está en `components.json`; el adicional de la integración AMD, en `processing-components.json`. Los avisos nuevos se conservan en `processing/` y dentro de los componentes del runtime. Los grafos SAM FP32 y sus hashes/procedencia están documentados en `docs/SAM_AMD_APU.md`. Los informes de procedencia previos se conservan con sus fechas históricas, sin presentarlos como pruebas del nuevo binario.

Los materiales de fuentes multimedia de la base siguen acompañando la entrega. Esta 3.22.2 es una revisión local: el cierre conjunto de publicación y la matriz de hardware pertenecen a 3.22.4. Consulte `docs/PUBLICACION.md` y `release-readiness.json` para el estado exacto.
