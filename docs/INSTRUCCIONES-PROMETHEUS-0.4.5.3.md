# Parche 0.4.5.3

Aplica después de 0.4.5.2. Añade un LUT externo a un clip visual y guarda el proyecto. Debe aparecer una copia en `<proyecto>.prometheus-data/luts`. Cierra y vuelve a abrir tras retirar el LUT externo: el efecto debe seguir funcionando.

Para probar la recuperación, retira temporalmente la copia de la carpeta del proyecto. El Inspector debe ofrecer «Localizar LUT…» y la comprobación previa debe impedir la exportación hasta reconectarlo. Prueba también reemplazar la copia por texto inválido: debe señalarse como LUT ilegible.
