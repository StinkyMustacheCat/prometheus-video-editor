# Integración MGT en Prometheus 0.6.5

Prometheus trata `.mgt.json` y `.mgtproj` como fuentes de medios independientes. Su proyecto guarda el archivo original, duración, streams, parámetros visuales por instancia y referencias a vistas ligeras/finales. Los identificadores temporales de trabajo del motor no se guardan en `.pthproj`.

El adaptador inicia el motor MGT incluido sin ventana, en una sesión local exclusiva. Comprueba el PID, el estado y la disponibilidad del contrato de assets v2. Importa el archivo mediante la API v2, consulta el preflight, genera un MOV ProRes con transparencia y, si corresponde, audio AAC, y libera el trabajo antes de cerrar el motor. Prometheus compone ese MOV con su línea de tiempo mediante FFmpeg. Los parámetros declarados por MGT aparecen en **Controles MGT**; al editar un clip se crea una instancia independiente, conservando intacto el medio de la biblioteca y otros usos del mismo archivo.

La vista ligera usa el perfil `preview` del motor y se prepara después de insertar el asset. La exportación usa `final`, incluso si el medio ya tenía vista ligera. El preflight de exportación informa si hay que resolver una composición. El proyecto puede guardar ambos renders en `Nombre.assets`; si se pierde uno y queda el MGT original, Prometheus puede regenerarlo.

La composición usa el tiempo y la velocidad del clip en Prometheus. El motor genera el medio a 60 fps y Prometheus lo adapta a la secuencia/exportación, preservando la duración. No se exponen en esta versión recursos internos de MGT para edición, transiciones MGT ni WebBridge; esos cambios pertenecen al contrato de MGT y a versiones posteriores.

Pruebas automatizadas de esta entrega: importación de los ejemplos `.mgt.json` y `.mgtproj`, vista ligera, resolución final, monitor, MP4 con audio cuando existe, guardado/reapertura `.pthproj` y cambio de parámetro por instancia. La revisión visual manual del flujo completo sigue siendo recomendable antes de usar un proyecto importante.
