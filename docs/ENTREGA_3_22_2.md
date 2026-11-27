# Prometheus 3.22.2 — prerelease de validación (no publicada)

Esta es una versión de prueba, no estable. La rama contiene fuentes; los ZIP todavía no se publican porque sigue pendiente el cierre de redistribución y correspondencia de fuentes de los nuevos proveedores ONNX Runtime y PyTorch CPU. Consulta `PUBLICACION.md` y `release-readiness.json`. La guía para el PC limpio, incluidos los resultados que deben reportarse, está en `PRUEBA_PC_LIMPIO_3_22_2.md`.

La integración abarca 3.22.0–3.22.2: NVIDIA/AMD, reconocimiento por función y Ajustes → Procesamiento. Conserva la corrección 3.19.10 de audio con muchos cortes.

## Abrir

`Portable/Prometheus-3.22.2/Prometheus.Client.exe`

Cuando se publique el paquete aprobado, contendrá multimedia, CEF, SAM, Whisper, CUDA y las alternativas AMD/CPU; no dependerá del USB ni de las rutas del equipo de compilación. No descargues ni reconstruyas artefactos desde esta rama para sustituir los ZIP. `Validacion/` conserva registros previos de la entrega, sin proyectos ni audio privado; no son una ejecución de pruebas en el worktree actual.

En **Ajustes → Procesamiento**, revisar la GPU detectada, la ruta de cada función y el último motor comprobado. Abrir el panel no carga modelos. Mantener Automático para empezar; el selector permite corregir la decisión. Un trabajo activo conserva su configuración. Para conectar MCP, ejecutar `Configure-Mcp.ps1` y usar el bloque generado para esta carpeta.

## Comprobaciones de integración

- 536 pruebas de la suite y tres pruebas del puente de dispositivos aprobadas, cero fallos. Los ensayos de procesos Python se ejecutaron con el runtime presente.
- SAM imagen: CUDA, DirectML y WebGPU. Misma máscara binaria en el caso de puntos positivos/negativos ensayado.
- SAM temporal: tres fotogramas, CUDA y DirectML + CPU. Máscaras idénticas en ese caso.
- Whisper: GPU FP16 y CPU INT8; ambas rutas transcribieron 32 palabras del tramo de voz de 12 segundos.
- H.264/HEVC por GPU y CPU, ProRes 422/4444 y WAV; dos exportaciones MGT consecutivas.
- Recuperación real tras cancelar con un segmento guardado; mantiene libx264 aunque cambie la preferencia local a NVIDIA.
- El proyecto real revisado, ahora con 321 entradas de audio en su mezcla inicial, entrega PCM y se detiene sin el error 206.
- Portable autónomo: MCP, herramientas multimedia, texturas CEF, alfa, parámetros vivos e intercambio entre procesos.
- Revisión visual de selección manual, aplicación, restablecimiento, actualización y exportación del diagnóstico.

Los registros previos identifican como equipo de integración un Ryzen 9 3900, RTX 3060 y 32 GB de RAM. Las pruebas sintéticas comprueban casos concretos; no certifican cualquier material ni rendimiento sostenido. La rama AMD del USB tenía pruebas en Ryzen 3 7320U / Radeon 610M; este paquete unificado no está validado allí ni en el 5300U. Tampoco se declara validado un equipo híbrido físico.

## Revisión manual recomendada

1. Comprobar en el PC principal Automático, SAM con varios clics, seguimiento y Whisper; revisar el motor mostrado al terminar.
2. Abrir una copia de un proyecto real, mover el cabezal y exportar un tramo con MGT, alfa y audio.
3. Copiar el portable completo al portátil; comprobar detección de la APU, DirectML para SAM, seguimiento con CPU temporal, Whisper CPU y exportación AMF/CPU. Guardar el diagnóstico si hay diferencias.
4. Cambiar una función a CPU o restaurar Automático y confirmar que la siguiente tarea usa la elección.

No se declara cerrada la matriz física de hardware ni la revisión legal de los proveedores añadidos. Esta prerelease no está publicada y no sustituye la 3.19.10 estable. No uses estos resultados como certificación de APU o híbrido.
