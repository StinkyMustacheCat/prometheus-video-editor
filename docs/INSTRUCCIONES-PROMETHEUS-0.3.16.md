# Prometheus 0.3.16 — descarga selectiva de tramos

Extrae sobre la misma carpeta donde aplicaste 0.3.15, con Prometheus cerrado, y reemplaza los archivos.

- En Bandeja, clic derecho sobre un medio → Elegir tramo… permite indicar Desde/Hasta o volver a Todo. Al importar en pista, el clip usa ese tramo. Al guardar solo en Medios se conserva el medio completo.
- Al importar un enlace desde la línea de tiempo, la ventana de tramo aparece después de consultar su duración. Importar desde Medios sigue agregando el medio completo.
- Resolver medios utilizados y Exportar identifican los tramos de fuente realmente usados, añaden dos segundos de margen a cada lado y agrupan tramos separados por hasta tres segundos. Los tramos se descargan con yt-dlp y se normalizan con FFmpeg.
- La caché guarda tramos y sus marcas de tiempo en el proyecto. Al repetir la resolución, los tramos disponibles se reutilizan. Los nuevos recortes fuera de caché se resolverán de nuevo. La exportación lee los archivos parciales respetando el tiempo de origen; si una plataforma no permite descargar un tramo, intenta resolver el medio completo.
- Las vistas ligeras siguen sirviendo para editar mientras se resuelven los originales en segundo plano.

Prueba: añade un enlace largo, elige un tramo corto de Bandeja e impórtalo. Pulsa Resolver medios utilizados y observa el estado. Vuelve a pulsarlo: no debe descargar de nuevo. Exporta, corta o cambia el tramo de origen y comprueba que solo se requieren los nuevos segundos. Contrasta un fotograma y el audio con el original.

Los tramos de vídeo requieren que el extractor público y yt-dlp admitan `--download-sections`; cuando no sea posible se descarga el medio completo. El audio web aislado sigue usando la descarga completa de audio. La prueba automatizada verifica planificación, caché, tiempo de origen y exportación con un archivo local; la descarga real de cada plataforma necesita la prueba manual indicada.
