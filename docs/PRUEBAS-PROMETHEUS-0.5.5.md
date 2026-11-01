# Revisión manual de Prometheus 0.5.5

1. Abre un proyecto de prueba y **Paneles → Instrucciones**. Copia el estado de la secuencia y pide un JSON con esos IDs y revisión. Al pegarlo, **Cargar tarjetas** debe indicar cuántas cargó, sin modificar la timeline.
2. Pide dividir un clip con `clip.cut`, aplicar un efecto a `$corte.right` y eliminar otro con `clip.delete`. **Revisar** debe mostrar los segmentos que aparecen o salen. **Aplicar** hace el lote; un solo **Ctrl+Z** lo revierte.
3. Aplica un efecto con `result`, cambia un parámetro mediante `effect.set_parameter` y comprueba que el valor queda en **Controles de efectos**. Prueba también activar/desactivar y reordenar la instancia.
4. Crea una secuencia vertical de 1080×1920 a 60 FPS y una pista nueva; sitúa un clip en la pista usando `$nombre_de_pista`. Comprueba que la secuencia aparece y que el lote se deshace en un paso.
5. Pide `media.import_url` con un enlace público compatible y una posición en una pista. **Revisar** debe consultar título y duración sin descargar el vídeo. **Aplicar** debe crear el clip y permitir seguir trabajando mientras se prepara la vista ligera.
6. Conecta un enlace a un espacio reservado de audio o vídeo con `placeholder.fill_url`. Si el espacio está vinculado, ambos componentes deben usar el mismo medio, conservar posición y duración. Con un vídeo más corto, debe aparecer la cola rayada.
7. Cambia el proyecto después de copiar su estado e intenta revisar el JSON anterior: Prometheus debe rechazarlo por revisión antigua. Un ID inexistente o un campo ajeno a la acción también debe impedir el lote completo.

La creación de secuencias admite por ahora 60 FPS. Las instrucciones de esta versión no importan enlaces de imagen ni interpretan lenguaje natural dentro de la aplicación.
