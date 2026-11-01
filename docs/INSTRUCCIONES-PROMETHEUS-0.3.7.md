# Prometheus 0.3.7 — selección y menús

Extrae este parche sobre la carpeta de Prometheus 0.3.6 con el programa cerrado, aceptando reemplazos. Conserva las carpetas `runtime` y `source`.

Al pulsar Suprimir con varios clips seleccionados, desaparecen todos en una sola operación de deshacer; se incluyen sus componentes vinculados. Si una pista afectada está bloqueada, no se elimina ninguno.

Un clic izquierdo selecciona un espacio entre clips; un segundo clic izquierdo en ese espacio lo deselecciona. El clic derecho ya no cambia la selección: muestra **Cerrar espacio** únicamente cuando se hace sobre un espacio ya seleccionado y la operación es válida. El menú de clips oculta las acciones que no corresponden al estado actual, en vez de mostrarlas desactivadas.

Prueba sugerida: selecciona varios clips con Ctrl y suprímelos; deshaz una vez. Selecciona un espacio, abre su menú con clic derecho y vuelve a hacer clic izquierdo para deseleccionarlo. Comprueba los menús con el lector fuera del clip y con una pista bloqueada.
