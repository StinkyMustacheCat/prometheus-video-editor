# Revisión manual de Prometheus 0.5.8

1. Abre un proyecto con una transición. Comprueba que los cuartos de círculo salen de los dos extremos del rango, dentro de la pista; selecciona la transición y pulsa **Supr**. Debe desaparecer sin borrar los clips. **Ctrl+Z** debe recuperarla.
2. Añade un enlace a Bandeja. Arrastra el elemento a Instrucciones o usa **clic derecho → Añadir desde Bandeja** en el panel. Debe aparecer una tarjeta gris. Revísala: si el medio existe debe ponerse verde. Aplicar debe guardarlo en Medios y quitarlo de Bandeja. **Ctrl+Z** debe restaurar ambos estados.
3. Abre **Editar instrucción** para esa tarjeta. Cambia el destino a una pista compatible y una posición. Revisa y aplica. Debe aparecer un clip en la pista. Si eliges una pista incompatible o bloqueada, la tarjeta debe aparecer roja y explicar el motivo.
4. Comprueba la edición visual de tarjetas `clip.delete`, `clip.cut`, `sequence.create`, `track.create`, `media.import_url` y `placeholder.fill_url`. Los cambios de tarjeta exigen volver a revisar; abrir el editor no bloquea la línea de tiempo.
5. Usa los atajos de reproducir, avanzar y retroceder fotogramas, ir al siguiente o anterior límite de clip y navegar marcadores. Deben conservar el comportamiento previo. La navegación no debe crear pasos de deshacer.

Pruebas automatizadas ejecutadas para esta entrega: `InstructionShelfTests058`, `InstructionShelfUiTests058`, `TransitionDeleteUiTests058`, `EditorActionUiTests058`, `InstructionTests056`, `InstructionTypeFilterTests0562` e `InstructionRevisionTests0561`.
