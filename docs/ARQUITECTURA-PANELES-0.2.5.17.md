# Arquitectura de paneles en 0.2.5.17

- `PanelWorkspace` mantiene el árbol de disposición, ventanas flotantes, menú Paneles, diseños y persistencia del layout. Su `contentHost` ocupa una zona interior independiente de las cuatro franjas de acoplamiento.
- `MediaPanelView` contiene los controles y gestos de la biblioteca. Expone acciones de importar, insertar, seleccionar y soltar archivos; no modifica el proyecto.
- `InspectorPanelView` construye la vista de medios y clips. Emite solicitudes de reconectar, cambiar visibilidad/silencio y ajustar volumen; no modifica el proyecto.
- `MainForm` coordina estas solicitudes con el editor, reproducción e historial. Registrar un panel exige su identificador, título, icono y control; no hay interfaz de registro para usuarios.
- `Timeline` y `PreviewCanvas` ya son controles separados, pero la creación de sus barras y algunos enlaces de aplicación siguen en `MainForm`. No se alteraron en esta entrega.

Prueba de extensión: los ocho placeholders se pueden abrir, flotar, acoplar, minimizar y recuperar sin cambiar el archivo de montaje. Una futura herramienta podrá sustituir el contenido reservado conservando la identidad del panel y su lugar en los diseños.
