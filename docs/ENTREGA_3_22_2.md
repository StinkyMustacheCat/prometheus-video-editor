# Prometheus 3.22.2 — primera revisión conjunta

Esta entrega termina 3.22.0–3.22.2: integración NVIDIA/AMD, reconocimiento por función y Ajustes → Procesamiento. Conserva la corrección 3.19.10 de audio con muchos cortes.

## Abrir

`Portable/Prometheus-3.22.2/Prometheus.Client.exe`

La carpeta ya contiene multimedia, CEF, SAM, Whisper, CUDA y las alternativas AMD/CPU; no depende del USB F: ni de las rutas del equipo de compilación. No hace falta reconstruir para probarla. `GitHub/` contiene las fuentes limpias; `Dependencies/`, la caché de compilación; `ThirdPartySources/`, los materiales de fuentes de la base. `Validacion/` conserva los registros de esta entrega sin copiar proyectos ni audio privado.

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

Equipo de esta validación: Ryzen 9 3900, RTX 3060 y 32 GB de RAM. Las pruebas sintéticas comprueban esos casos; no certifican cualquier material ni rendimiento sostenido. La rama AMD del USB tenía pruebas en Ryzen 3 7320U / Radeon 610M; este nuevo paquete unificado todavía debe revisarse allí y en el 5300U. No se declara validado un equipo híbrido físico.

## Revisión manual recomendada

1. Comprobar en el PC principal Automático, SAM con varios clics, seguimiento y Whisper; revisar el motor mostrado al terminar.
2. Abrir una copia de un proyecto real, mover el cabezal y exportar un tramo con MGT, alfa y audio.
3. Copiar el portable completo al portátil; comprobar detección de la APU, DirectML para SAM, seguimiento con CPU temporal, Whisper CPU y exportación AMF/CPU. Guardar el diagnóstico si hay diferencias.
4. Cambiar una función a CPU o restaurar Automático y confirmar que la siguiente tarea usa la elección.

3.22.3 queda para calibración y política de recursos. 3.22.4 reúne la matriz física de hardware y el cierre de publicación. Esta carpeta es una entrega de revisión funcional; no se subió automáticamente a GitHub ni se sustituyó la 3.19.10 estable.
