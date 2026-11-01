# Prometheus 0.3.2 — parche incremental

Aplica primero el parche 0.3.1 sobre la entrega completa 0.3.0. Después extrae este ZIP sobre la misma carpeta `Prometheus-0.3.0-win-x64`, con Prometheus cerrado y aceptando reemplazos.

El panel Medios ahora incluye carpetas de organización, búsqueda por nombre y coincidencias cercanas, filtros, vista de lista/cuadrícula y una barra inferior de iconos con texto emergente. El círculo de cada medio distingue video local, imagen, audio y web; el borde verde indica web resuelto. Haz clic derecho sobre un medio para organizarlo, abrir su ubicación o página de origen, copiar la URL, reconectar un archivo local o quitarlo del proyecto. El menú sobre espacio vacío contiene importación, nueva carpeta, resolución y reconexión. Arrastra un medio sobre una carpeta para organizarlo.

Eliminar una carpeta deja sus medios en «Sin carpeta». Eliminar un medio del proyecto conserva su archivo original; si se usa en la línea de tiempo, Prometheus avisa antes de quitar los clips correspondientes. Estas carpetas son del proyecto y no mueven archivos en el disco.

Prueba sugerida: crea una carpeta, mueve un medio dentro, busca parte de su nombre y cambia a cuadrícula; guarda, reabre y exporta un montaje con ese medio para verificar que la organización no afecta al render.
