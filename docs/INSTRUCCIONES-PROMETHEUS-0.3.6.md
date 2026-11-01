# Prometheus 0.3.6 — comprobación previa y cierre de la serie

Aplica los parches 0.3.1 a 0.3.5 en orden sobre la entrega completa 0.3.0. Después extrae este ZIP sobre la misma carpeta `Prometheus-0.3.0-win-x64`, con Prometheus cerrado y aceptando reemplazos. Alternativamente, usa el ZIP completo 0.3.6.

Al exportar se abre una comprobación de los medios usados en la secuencia activa. Bloquea la exportación si hay una importación pendiente, un espacio reservado visual sin conectar para MP4, o un archivo local ausente o modificado. Los medios web todavía no resueltos aparecen como avisos: se descargan automáticamente antes del render final. Un doble clic en un problema con tiempo lleva el lector a ese lugar. Si falta un archivo local, **Reconectar carpeta** abre la búsqueda habitual.

El aviso de baja resolución solo informa cuando una fuente menor que 720p se ampliará para el archivo 1080p. La exportación WAV ignora los espacios reservados de vídeo. La descarga de rangos web, secuencias múltiples, efectos, transcripción y MGT no forman parte de esta serie.

Las vistas ligeras de varios medios web se preparan en cola. Importar un segundo enlace mientras se prepara el primero ya no deja al segundo sin vista ligera.

Prueba sugerida: inicia una exportación con un placeholder, otra con un archivo local faltante y otra con un enlace web virtual. Los dos primeros deben indicar el problema antes de renderizar; la tercera debe avisar que resolverá el vídeo y continuar. Comprueba que el proyecto se guarda y abre después de conectar un placeholder.
