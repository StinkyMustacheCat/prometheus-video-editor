# Comprobación de la selección de CI

Las 129 pruebas de lógica seleccionadas en `.github/workflows/build.yml` se ejecutaron localmente con las rutas de FFmpeg, ffprobe y del runtime de IA apuntando a ubicaciones inexistentes. Resultado: 129 aprobadas, cero fallidas y cero omitidas. El flujo evita depender de herramientas locales o de una GPU dedicada.

La suite completa de la entrega se ejecutó por separado con el runtime incluido: 507 aprobadas, cero fallidas y cero omitidas. También se comprobaron exportación nativa, texturas MGT, MCP y carga local de Whisper. La ejecución remota del flujo se iniciará cuando se suba el repositorio.
