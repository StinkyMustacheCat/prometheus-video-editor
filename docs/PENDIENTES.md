# Pendientes después de 3.23.6

## 3.24.0 · Migración a .NET 10 LTS

Actualizar el SDK fijado, los proyectos y la compilación autónoma de Windows. Validar en un entorno limpio el arranque del editor, las texturas MGT, MCP, la exportación, SAM y Whisper.

## ProRes 4444 con alfa por Vulkan

Probar el decodificador Vulkan de FFmpeg con el MOV vertical 4K a 60 FPS que se usó para validar 3.23.6. Medir apertura, saltos y reproducción con varias capas. Comparar el color, las zonas transparentes y los bordes semitransparentes.

Si mejora la reproducción, probar el intercambio de texturas entre Vulkan y Direct3D 11 en el mismo adaptador. La meta es que los fotogramas se queden en la GPU hasta el compositor. Empezar en RTX 3060 y evaluar AMD después. Conservar el lector por CPU como respaldo por formato y adaptador, incluido QuickTime Animation.

La versión se decide al medir el resultado dentro del editor. La codificación ProRes por Vulkan se evalúa por separado del lector.
