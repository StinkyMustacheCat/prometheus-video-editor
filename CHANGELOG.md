# Cambios

## 3.19.9 — entrega completa

- FFmpeg 9.1 compilado desde fuentes incluidas, con Rubber Band 4.0.0 para conservar el cambio de tono.
- Motor multimedia y sus DLL aislados en `Runtime/ffmpeg`, con resolución relativa y overrides explícitos conservados.
- Fuentes originales de bibliotecas, parches, recetas, dependencias Rust, registros e inventarios de procedencia incluidos.
- Paquetes de fuentes de terceros divididos por tamaño para adjuntarlos a una release.


## 3.19.8 — Preparación de distribución

- Documentación del cliente actual, compilación y pruebas reproducibles con versiones de dependencias fijadas.
- Distribución autónoma para Windows x64, inventario y textos de licencia de dependencias.
- Acceso a la licencia, avisos y estado de publicación desde Ajustes → Acerca de Prometheus y licencias.
- IA localizada junto al ejecutable, sin búsqueda implícita de una instalación WinForms anterior en el disco.
- Configuración MCP generada para la ruta donde se extrae el portable.
- Separación de fuentes públicas, binarios, IA y fuentes de terceros; sumas de comprobación de los archivos de entrega.

Esta versión conserva la base funcional 3.19.7. No incorpora una migración de framework ni anuncia nuevos sistemas operativos o GPUs compatibles.

## 3.19.3–3.19.7

Paquete previo de estabilización tras auditoría: recuperación y procesos externos, coherencia de la edición, automatización MCP y gestión de errores/reintentos de formas de onda. Los resultados de pruebas de la entrega actual se documentan por separado; los ensayos automáticos no sustituyen la revisión manual de un proyecto real.
