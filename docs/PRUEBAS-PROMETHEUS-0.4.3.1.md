# Revisión manual de Prometheus 0.4.3.1

1. Crea un proyecto, importa un vídeo o usa `demo`. Haz un montaje corto en Secuencia 01. Pulsa `+` sobre la línea de tiempo: la nueva secuencia debe estar vacía y los medios deben seguir en la biblioteca. Cambia entre pestañas y comprueba que no se mezclan clips.
2. En `⋯`, duplica una secuencia: debe copiar pistas, clips, marcadores y marcas I/O. Renombra la copia, modifica solo su montaje y comprueba que el original no cambia. Elimina la copia; nunca debe poder eliminarse la última secuencia.
3. Mueve el lector y el zoom de cada secuencia, guarda el proyecto y vuelve a abrirlo. Verifica la secuencia activa, los montajes, marcas y la vista de cada una. Prueba deshacer y rehacer tras editar en distintas secuencias.
4. Exporta desde cada secuencia. Sus clips y marcas I/O deben gobernar el resultado. Repite una exportación a la misma ruta: debe aparecer el aviso; elegir «No» debe conservar el archivo existente.
5. Guarda el proyecto y mira la carpeta `<nombre>.prometheus-data` junto al JSON. Edita algo, espera al menos 20 segundos y comprueba que aparezca `recuperacion.prometheus.json`. Después de guardar normalmente, debe desaparecer. Para probar recuperación tras un cierre inesperado, espera al autoguardado, finaliza el proceso sin el cierre normal, abre el proyecto guardado y acepta recuperar.

Los medios locales importados desde otras carpetas permanecen enlazados allí. La carpeta de datos organiza las nuevas descargas y medios generados después de guardar; no es todavía una función de consolidación portátil de todas las fuentes.
