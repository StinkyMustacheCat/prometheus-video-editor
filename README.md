# Prometheus 3.19.9

Editor de vídeo no lineal para Windows, con cliente Avalonia, composición Direct3D 11, gráficos MGT en vivo y automatización local mediante MCP.

Esta es la distribución del cliente actual. La interfaz WinForms de las versiones antiguas no forma parte de esta entrega. El nombre histórico `Prometheus.V2.sln` se conserva para compilar las fuentes; la versión del programa es 3.19.9.

## Funciones

- Edición multipista de vídeo, audio, imágenes y textos; vínculos, grupos, velocidad y fotogramas clave.
- Subtítulos con transcripción Whisper y estilos animados MGT; máscaras de forma y SAM 2.1.
- Composiciones MGT HTML/CSS/JavaScript en vivo con CEF y texturas compartidas Direct3D 11; doce transiciones GPU incluidas.
- Descarga web con yt-dlp, calidad configurable y cookies de navegador opcionales.
- Exportación H.264/HEVC en MP4, ProRes 422/4444 en MOV y audio WAV. ProRes y algunas tareas de decodificación usan CPU; no se promete un flujo enteramente en GPU.
- MCP local con consulta del montaje, edición evaluada antes de aplicarse, análisis y exportación.
- Guardado, recuperación de sesión, historial del proyecto y avisos de tareas que requieren atención.

## Uso

Extrae todos los ZIP de componentes de la misma versión en una carpeta común, conservando la estructura. Ejecuta `Prometheus.Client.exe`. El paquete autónomo incluye .NET; no necesita instalar el SDK. Consulta [instalación](docs/INSTALACION.md), [MCP](docs/MCP.md) y [limitaciones](docs/LIMITACIONES.md).

Requisitos de esta distribución: Windows x64, controlador gráfico actualizado y GPU NVIDIA compatible con las funciones utilizadas. La ruta de render actual y SAM se validaron en el equipo de desarrollo; no se anuncia soporte AMD, Linux, macOS ni compatibilidad completa con todas las generaciones NVIDIA. Consulta las limitaciones antes de descargar varios gigabytes de IA.

## Compilación

Con el SDK de .NET 8.0.427 y PowerShell, ejecuta `./Build.ps1`. El SDK se fija en `global.json` y los paquetes NuGet mediante `packages.lock.json`. Para incluir multimedia e IA se indican sus carpetas a `-ToolsDirectory` y `-RuntimeDirectory`; las fuentes correspondientes se publican junto con los binarios, según [publicación](docs/PUBLICACION.md). `./Test.ps1` ejecuta las pruebas sin abrir ni cerrar la ventana del editor.

## Licencias

Prometheus: **GPL-3.0-or-later**, Copyright (C) 2026 Nicolas & Prometheus Contributors. Sin garantía. [Texto completo](LICENSE).

Cada dependencia mantiene su licencia. No se aplica la GPL de Prometheus a todos los archivos del paquete. [Inventario y avisos](licenses/TERCEROS.md), [inventario detallado](licenses/components.json) y [fuentes de terceros](docs/FUENTES_TERCEROS.md).

FFmpeg utiliza una compilación GPLv3 o posterior. El ejecutable combinado de yt-dlp incluye componentes bajo GPLv3 o posterior aunque el proyecto yt-dlp use Unlicense. SAM 2.1 usa Apache-2.0; las fuentes Inter conservan OFL-1.1. Las bibliotecas NVIDIA tienen sus propios términos.

El uso de descargas, modelos y medios requiere los permisos correspondientes sobre ese contenido. Las cookies personales, proyectos de prueba y credenciales no forman parte de la entrega.

Repositorio: https://github.com/StinkyMustacheCat/prometheus-video-editor
