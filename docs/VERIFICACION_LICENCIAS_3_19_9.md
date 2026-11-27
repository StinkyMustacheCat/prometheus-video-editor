# Historial: verificación de procedencia — 3.19.9

> **Alcance histórico:** esta evidencia describe la base 3.19.9 y no constituye una revisión ni cierre de las dependencias añadidas en Prometheus 3.22.2. Para el estado actual consulta `PUBLICACION.md`, `release-readiness.json` y `licenses/processing-components.json`.

Fecha: 25 de noviembre de 2026. Esta revisión sustituye el estado de preparación de 3.19.8.

## FFmpeg

El binario estático Gyan 9.0.1 no se utiliza en esta entrega. Se compilan FFmpeg 9.1 y Rubber Band 4.0.0 desde los archivos originales incluidos. La receta propia conserva el filtro `rubberband`, los codificadores usados por el editor y la aceleración NVIDIA/Direct3D declarada en la configuración. La composición del editor sigue utilizando su motor actual.

Se conservan código, licencias, receta reutilizable, receta ejecutada, configuración generada y registro de compilación. Para las bibliotecas acompañantes se incluyen los paquetes de fuentes de MSYS2 con código original, parches y PKGBUILD. Los hashes de los paquetes binarios se contrastan con la base oficial. La correspondencia del PKGBUILD con `.BUILDINFO` se registra por paquete.

Un archivo de fuentes de ngtcp2 contenía un cambio posterior que retiraba `openssl-3` de las herramientas de compilación. Se recuperó el PKGBUILD anterior desde su revisión inmutable y su hash coincide con el registrado en el binario. Las fuentes y parches restantes se conservan. Ambas versiones y la explicación están adjuntas.

Las dependencias Rust se conservan como archivos originales `.crate`, incluidos sus avisos, con checksums de los lockfiles y del registro oficial. Se incluyen también las variantes de Windows necesarias para el cambio explícito de `windows-sys` del constructor de librsvg. Los manifiestos distinguen esas fuentes auxiliares de los archivos realmente distribuidos del programa. No se afirma una reconstrucción idéntica de todas las DLL de terceros; sí se conserva su procedencia, código y materiales de construcción.

Evidencias: `licenses/vendors/FFmpeg-3.19.9/`, `licenses/native-components.json`, `ThirdPartySources/FFmpeg/` y sus manifiestos.

## Componentes conservados

- NVIDIA: 22 DLL distintas y dos copias adicionales contrastadas con redistribuibles oficiales. Se conservan los términos separados por componente; no se relicencian bajo GPL.
- yt-dlp: ejecutable oficial identificado y dependencia nativa `curl_cffi` contrastada byte por byte. Se conservan sus avisos completos, fuentes y recetas, incluida la licencia del ejecutable combinado.
- PyAV: wheel oficial contrastado, fuentes históricas del constructor, dependencias, fuentes MSYS2 y herramienta delvewheel recuperadas. Se mantiene la vía GPL del conjunto binario y BSD-3-Clause del código PyAV, sin depender de la reclasificación de x264/x265 de su API.
- Modelos Whisper y SAM: archivos contrastados con las revisiones y hashes de sus proveedores. Código y licencias originales adjuntos.
- CEF, Chromium, .NET, NuGet y Python: inventarios y avisos originales conservados; créditos completos del Chromium incluido.

Las evidencias de estos componentes están en `licenses/vendors/Verification-2026-11-25`. La validación funcional concreta de esta entrega y de sus archivos comprimidos se encuentra en ENTREGA_3_19_9.md y VERIFICACION_DEL_PAQUETE.json.

## Referencias principales

- https://ffmpeg.org/legal.html
- https://packages.msys2.org/packages/mingw-w64-x86_64-ffmpeg
- https://repo.msys2.org/mingw/sources/
- https://github.com/breakfastquay/rubberband/tree/v4.0.0
- https://github.com/msys2/MINGW-packages/commit/f6fe8c6a81c3f6e51c900d9b0ceb4bf87f615b17
- https://github.com/yt-dlp/yt-dlp#licensing
- https://github.com/PyAV-Org/PyAV/issues/2270
- https://docs.nvidia.com/cuda/archive/12.6.0/eula/index.html
