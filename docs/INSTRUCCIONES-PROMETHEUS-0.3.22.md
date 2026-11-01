# Prometheus 0.3.22 — tramos web opcionales

Cierra Prometheus y extrae este parche sobre la carpeta que ya tiene 0.3.21. Acepta reemplazar archivos. Requiere la versión completa 0.3.16.7 y todos los parches previos.

La descarga completa sigue siendo predeterminada. En Exportar → Avanzado puedes marcar «Descargar solo tramos web usados». La opción solo se aplica a MP4 final; no se combina con vistas ligeras ni con WAV. Se consideran únicamente los clips dentro del rango que elijas exportar. Prometheus añade márgenes, agrupa tramos cercanos y reutiliza la caché. Si la fuente no admite rangos precisos o una descarga parcial falla, obtiene el medio completo para preservar el resultado.

La ventana de progreso indica cuántos medios están listos y cuál se procesa. Durante la conversión muestra un porcentaje medido por FFmpeg cuando conoce la duración; durante descargas sin tamaño conocido muestra actividad sin inventar un porcentaje. La barra inferior muestra el render real y su tiempo restante aproximado tras acumular datos.

Prueba primero con un vídeo web largo y un recorte breve. Exporta una vez con tramos, repite para verificar la caché y compara con el modo completo. Prueba cancelar en preparación y durante render.
