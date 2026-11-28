# Fuentes y procedencia de terceros

**Integración de revisión 3.22.2.** Se conserva el cierre multimedia de 3.19.9 descrito debajo. Los proveedores ONNX y PyTorch CPU añadidos se inventarían por separado en `licenses/processing-components.json`, con avisos y procedencia. El cierre de publicación unificada se revisará en 3.22.4.

El portable usa FFmpeg/ffprobe 9.1 compilado desde fuentes, con Rubber Band 4.0.0; no incluye ni usa el ejecutable Gyan 9.0.1. Los archivos Gyan conservados en `ThirdPartySources` son evidencia de adquisición. Se incluyen las fuentes, parches, recetas propias y paquetes de fuentes MSYS2 de bibliotecas auxiliares; no se afirma una reconstrucción bit a bit de todas las DLL. PyAV mantiene aparte sus binarios FFmpeg 8.1.2. Los puntos de NVIDIA, yt-dlp y PyAV mantienen las evidencias de la revisión anterior; el cierre de FFmpeg se documenta en VERIFICACION_LICENCIAS_3_19_9.md.

Los archivos de fuentes se distribuyen junto con los binarios correspondientes. Sus autores y licencias se conservan: no son cÃ³digo propio de Prometheus. Los archivos originales, versiones y SHA-256 se registran en los manifiestos de la entrega.

## Correspondencia identificada

- FFmpeg/ffprobe `9.1-prometheus-3.19.9`: compilación propia a partir del archivo original de FFmpeg 9.1, sin el parche de reclasificación de x264/x265. Rubber Band 4.0.0 se compila por separado con FFT y remuestreador incorporados. Se incluyen las recetas, configuración, registros de compilación y manifiestos. Las DLL auxiliares proceden de paquetes MSYS2 con SHA-256 comprobado, `.PKGINFO`, `.BUILDINFO`, fuentes originales y parches. Para ngtcp2 se recuperó además el PKGBUILD original cuyo hash coincide con el registro del binario; el archivo de fuentes del proveedor había retirado únicamente una dependencia temporal de compilación.
- yt-dlp 2026.11.23: este portable usa el ejecutable oficial. Sus avisos y el inventario de origen están en licenses/yt-dlp/2026.11.23. La licencia del ejecutable combinado incluye GPLv3 o posterior.
- PyAV 18.1.0: su cÃ³digo usa BSD-3-Clause. Las DLL de vÃ­deo incluidas se identifican por separado: FFmpeg 8.1.2. Su API informa LGPLv3 o posterior, pero el constructor del proveedor modifica la clasificaciÃ³n de x264/x265 en `configure`. Ese texto de la API no basta para determinar la licencia del conjunto; se conserva el parche y se trata la comprobaciÃ³n GPL de esas bibliotecas por separado. Las fuentes de PyAV por sÃ­ solas no son las fuentes de esas DLL.
- Certifi y tqdm incluyen material MPL-2.0. Sus fuentes exactas se acompaÃ±an como archivos originales de PyPI.
- Modelo Whisper: `Systran/faster-whisper-small`, revisiÃ³n `536b0662742c02347bc0e980a01041f333bce120`; `model.bin` SHA-256 `3e305921506d8872816023e4c273e75d2419fb89b24da97b4fe7bce14170d671`, verificado con los metadatos LFS oficiales. Se conserva la tarjeta del modelo y la licencia MIT del proveedor original.
- SAM 2.1: se incluye el repositorio local que utiliza el editor, el checkpoint y sus textos Apache-2.0/BSD-3-Clause. Los archivos modificados por Prometheus se distinguen mediante el inventario y los scripts de puente propios, conservando los avisos originales.
  El checkpoint coincide con `facebook/sam2.1-hiera-small`, revisiÃ³n `ee5bba1d82bb8749febdf90f45e84b687142ba03`: SHA-256 `6d1aa6f30de5c92224f8172114de081d104bbd23dd9dc5c58996f0cad5dc4d38`. El inventario de fuentes distingue diferencias de finales de lÃ­nea y los archivos adicionales de configuraciÃ³n; no deduce una revisiÃ³n histÃ³rica a partir de una comparaciÃ³n con la rama actual.
- CEF/Chromium: los avisos completos se obtienen de `chrome://credits` del Chromium que acompaÃ±a al trabajador MGT, ademÃ¡s de los textos de CEF y CefSharp.
- NuGet: los paquetes, autores, versiones y referencias de repositorio se registran en `licenses/components.json`; sus textos completos estÃ¡n en `licenses/nuget`. Las dependencias del runtime .NET se incluyen con sus avisos propios.

## Comprobaciones que no deben darse por supuestas

La entrega de fuentes correspondientes de un binario GPL/LGPL requiere mÃ¡s que el cÃ³digo principal: revisiones de bibliotecas enlazadas, parches y scripts necesarios para construirlo tambiÃ©n deben estar identificados y disponibles. Si el proveedor no publica esa correspondencia exacta, se debe obtener de Ã©l o producir una compilaciÃ³n propia con fuentes verificables y volver a validar el motor.

La redistribuciÃ³n de CUDA/cuDNN requiere verificar las bibliotecas concretas frente a sus listas de redistribuibles y conservar los tÃ©rminos aplicables. Los avisos de copyright de PyTorch no sustituyen automÃ¡ticamente esos tÃ©rminos.

El inventario de esta preparaciÃ³n incluye los componentes restaurados y los presentes en Python; algunos solo se usan para compilar o no son utilizados por las funciones actuales. El manifiesto de archivos del portable determina quÃ© se entrega efectivamente.

Consulta el estado por componente en `release-readiness.json`. Una obligaciÃ³n sin evidencia queda pendiente aunque el programa funcione correctamente.
