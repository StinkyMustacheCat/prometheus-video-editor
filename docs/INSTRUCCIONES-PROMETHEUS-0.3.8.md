# Prometheus 0.3.8 — biblioteca visual y arrastre compatible

Extrae este parche sobre la carpeta de Prometheus 0.3.7, con la aplicación cerrada, y acepta reemplazar los archivos. No borres `runtime` ni `demo`.

El panel Medios presenta miniaturas y títulos con más espacio. El botón `+` está en la barra inferior y abre las opciones de importar o crear; el botón de carpeta muestra u oculta el árbol de organización. Las miniaturas se generan en segundo plano y se guardan en caché.

Al arrastrar archivos desde el explorador, las pistas solo aceptan tipos compatibles: vídeo e imágenes en pistas visuales, audio en pistas de audio. Los arrastres incompatibles se rechazan sin interrumpir el montaje con un cuadro de mensaje. Una línea separa las pistas visuales de las de audio.

Prueba sugerida: abre un proyecto con vídeo, imagen y audio; cambia entre lista y cuadrícula, abre la carpeta y comprueba que los títulos sigan legibles. Arrastra WAV sobre vídeo y PNG sobre audio: el cursor debe indicar que no se puede soltar.
