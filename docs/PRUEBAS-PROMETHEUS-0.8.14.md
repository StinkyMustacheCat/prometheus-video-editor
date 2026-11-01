# Pruebas de uso — Prometheus 0.8.14

1. Abre un proyecto anterior y comprueba que la secuencia, efectos y curvas se conservan. Guarda una copia en el formato actual antes de continuar en versiones anteriores.
2. Anima Escala y Posición sobre un vídeo de 30 FPS dentro de una secuencia de 60 FPS. Comprueba el movimiento en el monitor y en un MP4 exportado a 60 FPS. Cambiar los FPS del monitor debe afectar solo a la vista previa.
3. Activa el botón temporal de un parámetro: no debe aparecer un punto nuevo. Cambia el valor en un tiempo posterior: debe conservar el valor inicial y crear el nuevo punto. Desactiva y reactiva el botón: los puntos deben permanecer. Restablece: debe añadirse el valor predeterminado en la posición del lector.
4. En el visor de claves, haz doble clic en una fila, navega con las flechas superiores y con las de una fila, despliega dos curvas y desplázate verticalmente. Verifica la selección y edición de puntos existentes.
5. Mantén Alt y arrastra un clip de vídeo vinculado con audio. La copia debe heredar los atributos, permitir efectos propios y conservar un vínculo nuevo entre sus componentes. Deshaz la copia con Ctrl+Z.
6. En Exportar, elige por separado carpeta y nombre. Exporta y comprueba que el historial del proyecto abre el resultado o su carpeta. Prueba por separado «Abrir carpeta al terminar» y «Reproducir al terminar». La barra se vuelve verde y los botones de resultado se activan únicamente al finalizar.

Se verificaron automáticamente migración de proyectos, animación y exportación de Escala/Posición a 60 FPS, copia independiente y deshacer, controles del visor, historial y estado final de exportación. Los gestos y el rendimiento en un proyecto real deben comprobarse manualmente.
