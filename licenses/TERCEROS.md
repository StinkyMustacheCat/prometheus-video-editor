# Componentes de terceros â€” Prometheus 3.19.9

Prometheus se distribuye bajo GPL-3.0-or-later. Cada componente de terceros conserva sus derechos, condiciones y avisos. Los textos originales se incluyen sin traducir ni reemplazar sus titulares por los de Prometheus.

| Componente | Licencia principal / observaciones | Avisos |
|---|---|---|
| Avalonia 11.2.5, CommunityToolkit.Mvvm, Vortice, NAudio | MIT; vÃ©anse dependencias nativas y fuentes por separado | `nuget/` |
| Jint 4.18.0 | BSD-2-Clause | `nuget/Jint-4.18.0/` |
| Acornima 1.9.1, Adam Simon | BSD-3-Clause | `nuget/Acornima-1.9.1/` |
| CEF y CefSharp 156 | BSD y licencias de componentes de Chromium | `vendors/` y `nuget/` |
| SkiaSharp, HarfBuzzSharp y ANGLE | Licencias de las envolturas y avisos nativos completos | `nuget/` |
| Inter | SIL Open Font License 1.1; independiente de la licencia del paquete Avalonia.Fonts.Inter | `vendors/Inter-OFL.txt` |
| .NET | MIT y avisos de terceros del runtime autÃ³nomo | `dotnet/` |
| Microsoft Visual C++ Redistributable x64 | TÃ©rminos de Microsoft separados; instalador original firmado, sin ejecutar | `vendors/VC-Redistributable-*` |
| FFmpeg/ffprobe 9.1 (`9.1-prometheus-3.19.9`) | GPLv3 o posterior; compilaciÃ³n propia desde fuentes; incluye Rubber Band 4.0.0 | `vendors/FFmpeg-3.19.9/`, `native-components.json` y `ThirdPartySources/FFmpeg/` |
| yt-dlp 2026.08.19 | Fuente Unlicense; ejecutable combinado GPLv3 o posterior por sus dependencias | `vendors/yt-dlp-*` |
| Python 3.11 | PSF y avisos de sus bibliotecas | `python/` |
| PyTorch 2.8.0, torchvision 0.23.0 | BSD-3-Clause y avisos de componentes incluidos | `python/` |
| torchaudio 2.8.0 | BSD-2-Clause y avisos adicionales | `python/` |
| Faster Whisper 1.2.1, CTranslate2 4.8.2, ONNX Runtime 1.30.0 | MIT y avisos nativos | `python/` |
| PyAV 18.1.0 | CÃ³digo BSD-3-Clause; para el conjunto binario se documenta la vÃ­a GPLv3 o posterior ofrecida por el proveedor, conservando avisos individuales | `python/`, `vendors/PyAV-MSYS2/`, verificaciÃ³n y fuentes adjuntas |
| Whisper small | MIT; tarjeta de modelo y revisiÃ³n verificadas | `vendors/` |
| SAM 2.1 | Apache-2.0; ccTorch BSD-3-Clause | `vendors/` y repositorio adjunto |
| CUDA 12.6 y cuDNN 9.10.2.21 | TÃ©rminos NVIDIA; no quedan cubiertos por la GPL de Prometheus | `vendors/` |
| Certifi y tqdm | Incluyen material MPL-2.0; fuentes adjuntas | `python/` |

El inventario completo de paquetes, textos copiados y sus sumas se encuentra en `components.json`. Las licencias principales de esta tabla no sustituyen los avisos transitivos del paquete. La presencia de un archivo de licencia no implica que todas las obligaciones de redistribuciÃ³n estÃ©n cerradas: revisa `docs/PUBLICACION.md` y el estado de fuentes de la entrega.

El portable de esta entrega usa FFmpeg/ffprobe 9.1 compilado desde fuentes; no usa el ejecutable Gyan 9.0.1. Los archivos de Gyan que permanecen en `ThirdPartySources` son evidencia de adquisiciÃ³n, no binarios del portable. Las DLL auxiliares de FFmpeg se atribuyen por separado en `native-components.json`. PyAV 18.1.0 usa su propio conjunto de binarios FFmpeg 8.1.2, distinto del motor principal del editor. Se incluyen fuentes, parches, configuraciÃ³n y recetas de compilaciÃ³n; no se afirma una reconstrucciÃ³n bit a bit de todas las DLL auxiliares.

La revisiÃ³n del 25 de noviembre de 2026 verificÃ³ 24 archivos NVIDIA (22 DLL distintas), la biblioteca nativa de yt-dlp y los materiales de fuentes de PyAV. Los datos se conservan en `vendors/Verification-2026-11-25` y los textos originales NVIDIA por componente en `vendors/NVIDIA-components`. La procedencia y los materiales de construcciÃ³n de FFmpeg 9.1 se documentan en `vendors/FFmpeg-3.19.9` y `ThirdPartySources/FFmpeg`.

Enlaces de referencia: https://ffmpeg.org/legal.html Â· https://github.com/yt-dlp/yt-dlp#licensing Â· https://github.com/facebookresearch/sam2 Â· https://huggingface.co/Systran/faster-whisper-small Â· https://docs.nvidia.com/cuda/archive/12.6.0/eula/index.html
