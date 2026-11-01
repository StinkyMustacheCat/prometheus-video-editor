# Prometheus 0.3.14 — audio web y cierre de la serie

Extrae este parche sobre la carpeta de 0.3.13 con Prometheus cerrado y acepta reemplazar archivos. Si prefieres una instalación limpia, usa el ZIP **Prometheus-0.3.14-completo-win-x64.zip**.

Una pista de audio permite importar un enlace público desde su menú contextual o mediante arrastre. Un espacio reservado de audio también ofrece **Definir contenido → Enlace web de audio**. Prometheus crea el asset virtual, lo coloca en la línea de tiempo y descarga en segundo plano solo el audio con `yt-dlp -f bestaudio`. Lo normaliza a AAC estéreo 48 kHz en MP4 para edición y exportación. El archivo final se guarda en caché y se reutiliza si se usa otra vez el mismo enlace. Mientras se prepara, el lector puede seguir avanzando en silencio.

La descarga por rangos sigue fuera de esta serie; se resuelve la fuente completa. La resolución web requiere un enlace público compatible y conexión de red.

Prueba sugerida: suelta un enlace de YouTube en A1, edita mientras termina, usa Resolver medios utilizados y exporta un WAV. Repite el mismo enlace para comprobar que se reutiliza el archivo local. Prueba también un espacio reservado de audio.
