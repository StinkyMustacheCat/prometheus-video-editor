# Prometheus 0.3.5 — conectar espacios reservados

Aplica los parches 0.3.1, 0.3.2, 0.3.3 y 0.3.4 sobre la entrega completa 0.3.0. Después extrae este ZIP sobre la misma carpeta `Prometheus-0.3.0-win-x64`, con Prometheus cerrado y aceptando reemplazos.

Haz clic derecho sobre un clip de **espacio reservado** en la línea de tiempo. Puedes definir el tipo esperado (vídeo, imagen o web), conectar un medio ya importado, un archivo local, una imagen local o un enlace público compatible. Para vídeos puedes indicar el punto inicial de la fuente en HH:MM:SS:FF. El clip conserva su posición y duración. Si el vídeo no alcanza para cubrirlo, el cambio se rechaza sin alterar el proyecto. La sustitución admite deshacer y rehacer.

Un enlace de vídeo crea un medio virtual con la duración completa y prepara una vista ligera en segundo plano. La descarga final sigue el flujo existente de resolución; la descarga selectiva por rangos queda para otra cota.

Prueba sugerida: crea un placeholder de cinco segundos, divídelo, conecta solo una mitad con un vídeo suficientemente largo y comprueba que la otra sigue reservada. Guarda/reabre el proyecto; prueba deshacer/rehacer y la conexión con imagen o enlace.
