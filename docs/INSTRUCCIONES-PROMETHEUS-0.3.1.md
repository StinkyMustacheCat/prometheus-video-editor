# Prometheus 0.3.1 — parche incremental

Extrae este ZIP sobre la carpeta `Prometheus-0.3.0-win-x64` de la entrega completa 0.3.0 y acepta reemplazar archivos. Cierra Prometheus antes de aplicar el parche. Después ejecuta `Prometheus.exe` como siempre.

**Resolver medios utilizados** funciona ahora en segundo plano. Puedes seguir seleccionando, moviendo y recortando clips, además de reproducir con la vista ligera mientras se descarga el archivo final. Cada medio queda conectado al terminar. Esc cancela la resolución en segundo plano; cambiar de proyecto también cancela la tarea. Si exportas durante la resolución, la exportación espera o reutiliza el archivo en caché sin descargar dos veces la misma URL.

Para probarlo: importa un enlace de YouTube, espera la vista ligera, pulsa **Resolver medios utilizados** y continúa editando o reproduciendo. Al terminar, guarda y reabre el proyecto para comprobar que la representación final sigue conectada.

Este parche no incluye `runtime`: conserva los archivos de la entrega completa 0.3.0. La resolución sigue siendo del medio completo; descarga selectiva de rangos tendrá una cota posterior.
