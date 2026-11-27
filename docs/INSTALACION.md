# Instalación y componentes

1. Para la revisión 3.22.2, usa la carpeta portable completa o extrae todos los ZIP binarios de esa versión en la misma ubicación. Cada uno conserva la misma carpeta raíz. No ejecutes el programa dentro del ZIP ni mezcles componentes de distintas versiones.
2. Abre `Prometheus.Client.exe`. Los componentes `MgtLive`, `McpBridge`, las herramientas multimedia y `Runtime` deben conservar sus rutas relativas.
3. Para Whisper y SAM, deben existir `Runtime/whisper/python/python.exe`, `Runtime/whisper/model/model.bin`, `Runtime/sam2/repo` y `Runtime/sam2/sam2.1_hiera_small.pt`.
4. Consulta **Ajustes → Procesamiento** para comprobar los adaptadores y la ruta de cada función. Mantén actualizado el controlador del fabricante correspondiente. No se instala un controlador desde este paquete.
5. En un Windows recién instalado, CEF y algunas bibliotecas de IA pueden necesitar Microsoft Visual C++ Redistributable x64. Se incluye el instalador original firmado en `Prerequisitos/vc_redist.x64.exe`; ejecútalo si falta ese componente. Durante la preparación no se instaló ni modificó el runtime del sistema.

Se necesita espacio para el programa extraído, los modelos y las cachés de trabajo; el tamaño exacto de los ZIP se encuentra en el manifiesto de entrega. Los modelos están incluidos: no se descargan al primer uso.

El programa es autónomo respecto a .NET, pero requiere los servicios gráficos y multimedia de Windows. La carpeta debe ser escribible. Algunas cachés y recursos generados se guardan en `Prometheus` en la raíz del disco de la aplicación, y los ajustes de interfaz en los datos locales del usuario: autónomo no significa que todos sus datos queden dentro de una única carpeta. Consulta LIMITACIONES.md.

No sobrescribas tu proyecto ni elimines sus carpetas de assets para actualizar. Conserva una copia de los proyectos antes de abrirlos con una versión nueva.

No hay cookies, sesiones web ni cuentas preconfiguradas. Las cookies son opcionales y se configuran en Ajustes → Descargas web. Los vídeos restringidos pueden requerir una cuenta autorizada; no se ofrece una autenticación universal del navegador.
