# Prometheus 3.19.10 — corrección de reproducción y exportación de audio

Corrige el error 206 de Windows al iniciar mezclas de audio de montajes con muchos cortes. La corrección pertenece a la base de Prometheus; no introduce cambios de compatibilidad APU.

## Causa

El mezclador enviaba las rutas de los medios y el grafo completo de filtros en una única línea de comandos. El proyecto real utilizado para reproducir el fallo generaba 58.838 caracteres y 215 entradas de audio al comenzar. Windows limita esa línea a 32.767 caracteres. FFmpeg estaba instalado y arrancaba correctamente por separado.

La mezcla preparada ahora externaliza el mismo grafo a un archivo UTF-8 cuando la orden crece. La orden del caso real queda en torno a 14.350 caracteres. No cambia cortes, velocidad, efectos, envolventes, balance ni niveles.

## Alcance

- Reproducción del audio de la línea de tiempo.
- Exportación WAV.
- Mezcla de audio para las exportaciones de vídeo, incluida la ruta recuperable que utiliza el mezclador compartido.
- Limpieza del archivo temporal al terminar, cancelar o fallar; comprobación del tamaño restante de la orden.

Si las propias rutas de miles de entradas todavía superan el límite después de externalizar el grafo, se informa esa limitación. Esta corrección no implementa un nuevo motor por bloques ni promete un número ilimitado de entradas.

## Validación

- 514 pruebas aprobadas, cero fallidas y cero omitidas; siete pruebas nuevas para esta corrección.
- Regresión de orden superior al límite, archivo sin BOM, limpieza, fallo de arranque y cancelación.
- Decodificación PCM idéntica entre filtros internos y externos sobre una señal controlada.
- Exportación PCM y WAV idénticas con 220 cortes.
- Decodificación del proyecto real en inicio, mitad y final: dos segundos por posición, salida de 768.000 bytes de PCM flotante estéreo a 48 kHz, sin errores de FFmpeg.
- Exportación WAV completa del proyecto real: 1.778,833333 segundos, PCM estéreo de 48 kHz, archivo validado con FFprobe. No se exportó de nuevo todo el vídeo.

Las pruebas del proyecto real lo leen sin guardarlo ni modificarlo. No se abre MainWindow para validar y no se reemplaza la sesión que pueda estar abierta. Las comprobaciones adicionales del portable y los artefactos figuran en VERIFICACION_DEL_PAQUETE.json.

## Uso

Cerrar normalmente la versión anterior y abrir el ejecutable de la carpeta Portable/Prometheus-3.19.10. Volver a abrir el proyecto guardado. Una aplicación que ya estaba abierta conserva los binarios anteriores hasta que se cierra.

Los componentes de IA, FFmpeg y sus dependencias se conservan respecto a 3.19.9. FFmpeg mantiene su identificador de compilación 9.1-prometheus-3.19.9, porque no fue recompilado en este arreglo. No se añaden dependencias de terceros.

No se declara compatibilidad AMD/APU ni se modifica su rama. Se mantienen las limitaciones de hardware, color y framework documentadas en LIMITACIONES.md.
