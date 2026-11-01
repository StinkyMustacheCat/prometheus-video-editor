# Prometheus 0.3.21 — entrada, salida y progreso

Cierra Prometheus y extrae este parche sobre la carpeta que ya tiene 0.3.20. Acepta reemplazar los archivos. Requiere la versión completa 0.3.16.7 y los parches anteriores.

I marca la entrada y O la salida en la posición del lector; ambos atajos son configurables. El menú contextual de la regla temporal también ofrece las marcas y borrarlas. Cuando hay un intervalo válido, la ventana Exportar lo propone y permite volver a Secuencia completa. El render del intervalo conserva huecos y no modifica el montaje.

La exportación muestra una ventana móvil que bloquea la edición mientras trabaja, con progreso de preparación de medios, progreso real del render y cancelación. La estimación de tiempo aparece solo tras observar suficiente avance del render.

Prueba I/O sobre dos clips, exporta el rango, revisa la duración, guarda y reabre, y prueba Deshacer/Rehacer y exportación completa.
