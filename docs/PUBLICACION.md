# Publicación — Prometheus 3.22.2

**Estado: rama de código para pruebas publicada; prerelease binaria bloqueada.** Esta integración no es estable ni debe presentarse como compatible de forma general. No se adjuntarán los ZIP hasta completar la revisión de redistribución y correspondencia de fuentes de los componentes nuevos de ONNX Runtime y PyTorch CPU. El detalle está en `release-readiness.json` y `licenses/processing-components.json`.

Los registros incluidos son resultados de la entrega fuente, no una ejecución de pruebas en este worktree. Registran 536 pruebas unitarias, tres pruebas del puente de dispositivos y verificaciones sintéticas. La validación física disponible fue en Ryzen 9 3900 / RTX 3060 / 32 GB / Windows x64. No demuestra funcionamiento en APU ni en un sistema híbrido; esos resultados deben recogerse de testers en los equipos indicados en `PRUEBA_PC_LIMPIO_3_22_2.md`. La prueba anterior de AMD APU 3.21.2 no valida este paquete.

La integración conserva FFmpeg `9.1-prometheus-3.19.9` y Rubber Band 4.0.0. La revisión de fuentes/licencias de esa base es histórica y no cierra por sí sola el inventario añadido en 3.22.2. No se ha cambiado ni sustituido la versión estable 3.19.10.

Cuando se autorice una prerelease, el portable y los paquetes `AI-XX` deberán extraerse juntos en la misma carpeta raíz. `Source`, `Dependencies` y `ThirdPartySources` son paquetes separados; no se deben mezclar versiones. Antes de una publicación binaria hay que resolver los holds, omitir `Runtime/sam2/track-cpu/torch/lib/dnnl.lib` (biblioteca estática de enlace, no necesaria para ejecutar el portable), reconstruir los ZIP afectados, regenerar `SHA256SUMS.txt` y volver a verificar hashes, CRC y cobertura del manifiesto. Ese archivo de 2.325.803.578 bytes figura actualmente como la única entrada de AI-02.

Los artefactos de varios GB no se guardan en Git. No publicar una release estable ni convertir esta rama de pruebas en una release estable.
