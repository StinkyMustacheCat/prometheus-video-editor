# Conexión MCP local

Prometheus actúa como servidor de edición mientras está abierto y el botón MCP muestra el estado activo. `McpBridge/Prometheus.McpBridge.exe` convierte el transporte estándar de entrada/salida del cliente MCP al canal local del editor. No necesita un puerto público ni una cuenta externa.

Ejecuta `Configure-Mcp.ps1` desde la carpeta del portable para generar los ejemplos con la ruta real. El script no modifica la configuración de ninguna aplicación. Copia el bloque correspondiente al cliente que utilices y recarga su conexión MCP. No uses una ruta de otra máquina.

El puente y el editor deben ejecutarse con el mismo usuario de Windows. Si Windows deniega el canal, comprueba primero que no se esté ejecutando uno como administrador y otro como usuario normal. La restricción de usuario del canal se conserva.

Herramientas expuestas: `get_state`, `inspect_clip`, `get_catalog`, `evaluate_changelist`, `apply_changelist`, `editor_action`, `editor_history`, `capture_frame`, `project_file`, `media_analysis` y `export_sequence`.

Consulta `get_catalog` para conocer las operaciones y parámetros exactos de la versión. La edición usa evaluación previa y aplicación del plan; valida el resultado antes de guardar o exportar. La herramienta de estado permite limitar el contexto por rango, según su esquema. Las transcripciones, medios, rutas y capturas pueden contener información privada: compártelos solo con el cliente elegido.

El nombre histórico del servidor en el protocolo es `prometheus-v3-2`; la versión se lee del ensamblado. No es el número de versión de la aplicación. El esquema actual de proyectos sigue siendo `2.0.0`; tampoco equivale a la versión del producto.
