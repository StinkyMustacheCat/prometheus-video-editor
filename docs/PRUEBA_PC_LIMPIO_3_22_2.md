# Guía de prueba en PC limpio — Prometheus 3.22.2

**Estado: pendiente de publicación.** Esta guía prepara una prerelease de validación, no una release estable. No hay ZIP públicos disponibles desde esta rama; no uses artefactos de una entrega anterior como sustituto. La publicación queda bloqueada hasta completar la revisión de redistribución y fuentes descrita en `PUBLICACION.md`.

## Alcance conocido

- Windows x64. La validación de integración registrada fue en Ryzen 9 3900, RTX 3060 de 12 GB y 32 GB de RAM.
- No se ha verificado físicamente este paquete en una APU ni en un sistema híbrido. La prueba anterior de AMD APU 3.21.2 no es evidencia para 3.22.2.
- SAM temporal en la ruta AMD mantiene la propagación de fotogramas en CPU. El flujo AMF puede copiar BGRA de GPU a CPU; no es cero copia.
- Los informes de 536 pruebas unitarias, tres pruebas de puente de dispositivos y validaciones sintéticas son registros de la entrega, no resultados del worktree ni pruebas físicas de APU/híbrido.

## Requisitos

- PC Windows de 64 bits, controladores gráficos actuales del fabricante y permisos de escritura en la carpeta de la aplicación.
- Espacio libre suficiente para extraer el portable completo (los registros de entrega indican 9.964.257.234 bytes desplegados), además de espacio para cachés y proyectos de prueba.
- Microsoft Visual C++ Redistributable x64 puede ser necesario. La instalación puede requerir permisos de administrador.
- No uses proyectos originales sin copia de seguridad. Prueba con material no sensible y no adjuntes medios privados, cookies ni credenciales al informe.

## Instalación cuando los paquetes estén publicados

1. Descarga todos los ZIP de la misma prerelease y verifica cada archivo con `SHA256SUMS.txt` antes de extraerlo.
2. Extrae `Portable` y todos los archivos `AI-XX` juntos, conservando la carpeta raíz y rutas relativas. No ejecutes el programa dentro del ZIP ni mezcles versiones.
3. `Source`, `Dependencies` y `ThirdPartySources` son paquetes distintos; no son necesarios para abrir el portable. Consérvalos con la misma versión para consultar fuentes, dependencias o avisos.
4. Si el instalador de Visual C++ resulta necesario, usa únicamente el instalador firmado incluido en la entrega aprobada o el canal oficial de Microsoft.
5. Abre `Prometheus.Client.exe` y guarda una copia del diagnóstico antes de probar cambios de GPU.

## Pruebas solicitadas

1. Registra CPU, cada GPU/APU, versión de Windows, controladores, memoria y si el equipo es APU, híbrido o solo NVIDIA/AMD.
2. Abre **Ajustes → Procesamiento** y anota el adaptador y motor detectados para cada función. Repite con Automático y con una selección manual de CPU o GPU.
3. Prueba SAM por puntos con varios clics y una imagen de prueba; luego prueba seguimiento de varios fotogramas. Anota por separado la GPU de inferencia y si la propagación temporal usa CPU.
4. Prueba Whisper con un clip corto no privado y compara transcripción y tiempos; registra el motor mostrado.
5. Exporta un tramo corto de un proyecto de prueba a H.264 o HEVC y, si el equipo lo admite, prueba AMF. Incluye alfa/audio/MGT solo si forman parte del objetivo de esa prueba.
6. Cierra y vuelve a abrir la aplicación; confirma que el selector de procesamiento conserva o restablece la opción esperada. No uses la prueba para sobrescribir proyectos de trabajo.

## Informe que debe enviar el tester

- Nombre y etiqueta exacta de la prerelease, SHA-256 de los ZIP y si la extracción terminó sin errores.
- Modelo de CPU/APU, cada GPU, versión de Windows y controladores; identifica si es un equipo híbrido y qué pantalla está conectada a cada GPU.
- Pasos exactos, selección Automático/manual, motor detectado, resultado esperado y resultado observado.
- Errores completos, diagnósticos exportados, capturas pertinentes y si el fallo se repite tras reiniciar.
- Indica explícitamente qué pruebas no se realizaron. Un resultado de escritorio NVIDIA o una prueba sintética no cuenta como validación física de APU/híbrido.
