# Revisión manual de Prometheus 0.5.6

1. Abre **Paneles → Instrucciones**, arrastra un efecto y comprueba que la tarjeta queda gris. Revisa: una aplicación válida queda verde; en una secuencia con vídeo y audio, un efecto solo visual actúa sobre vídeo sin marcar el audio como omisión; una instrucción con ID inexistente queda roja. Si hay otras tarjetas válidas, **Aplicar** ejecuta solo esas partes en un único paso de deshacer.
2. Reordena las tarjetas arrastrándolas y con las flechas del extremo derecho. El estado vuelve a gris y **Aplicar** se desactiva hasta repetir la revisión. Clic derecho abre Editar, Quitar, Cargar atributos copiados y Ver detalle; **Supr** quita la tarjeta seleccionada. **Vaciar** sigue visible.
3. Abre **Editar instrucción**, cambia la selección de clips en la línea de tiempo mientras la ventana sigue abierta y guarda. La tarjeta de alcance **Medios seleccionados** debe recoger los IDs de la selección actual. La línea de tiempo debe seguir operable.
4. Pega un documento JSON en **Pegar instrucciones** y pulsa **Cargar tarjetas**. La revisión debe iniciarse sola. Abre **Detalles** solo cuando quieras ver el resumen largo.
5. En un clip de vídeo con encuadre y varios efectos, usa **Copiar atributos**. Selecciona otro clip de vídeo y **Pegar atributos**. Debe copiar encuadre, orden, parámetros y estado de los efectos, sin mover ni reemplazar el medio. **Ctrl+Z** revierte el pegado.
6. Copia atributos de un clip, selecciona varios destinos y en Instrucciones elige **Cargar atributos copiados**. Revisa la tarjeta y confirma que omite clips de audio al pegar atributos visuales, indicando por qué. Prueba lo mismo con volumen, balance y efectos de un clip de audio.
7. Si copias un efecto LUT, comprueba que el archivo asociado sigue funcionando en el destino. Comprueba también que los enlaces web y los espacios reservados de 0.5.5 conservan su comportamiento.

Desde 0.5.8, la Bandeja puede aportar tarjetas a Instrucciones. Todavía no hay interpretación de lenguaje natural dentro de Prometheus.
