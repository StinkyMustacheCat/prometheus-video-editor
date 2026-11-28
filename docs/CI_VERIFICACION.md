# Comprobación de la selección de CI

## Prometheus 3.23.6

Una copia limpia de los archivos de Git restauró los paquetes en modo bloqueado y compiló la solución en Release con .NET 8.0.427. La compilación terminó con cero advertencias y cero errores.

La verificación local usó la misma selección de GitHub Actions. Pasaron esas 129 pruebas y 31 pruebas adicionales de descargas web y premultiplicación de alfa. Ambas ejecuciones usaron rutas inexistentes para FFmpeg, ffprobe y el runtime de IA. La suite completa de 3.23.6 pasó 569 pruebas con el runtime de la entrega.

## Registro anterior

Las 129 pruebas de lógica seleccionadas en `.github/workflows/build.yml` se ejecutaron localmente con las rutas de FFmpeg, ffprobe y del runtime de IA apuntando a ubicaciones inexistentes. Resultado: 129 aprobadas, cero fallidas y cero omitidas. El flujo evita depender de herramientas locales o de una GPU dedicada.

La suite completa de la entrega se ejecutó por separado con el runtime incluido: 507 aprobadas, cero fallidas y cero omitidas. También se comprobaron exportación nativa, texturas MGT, MCP y carga local de Whisper. La ejecución remota del flujo se iniciará cuando se suba el repositorio.
