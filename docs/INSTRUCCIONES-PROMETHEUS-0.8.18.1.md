# Instrucciones de Prometheus 0.8.18.1

El panel **Instrucciones** prepara un lote de cambios, revisa cada tarjeta y lo aplica como una sola operación que se puede deshacer. También acepta documentos creados por una IA externa: copia el estado desde el panel, entrégalo a la IA y pega su respuesta en **Pegar instrucciones**. La IA debe devolver JSON con `format: "prometheus-instructions"`, `version: 1`, el `project_id` y la `revision` del estado copiado, y una lista `actions` de hasta 200 elementos.

## Elegir cuánto contexto copiar

**Clips seleccionados** incluye únicamente la selección y sus medios. **Tramo In/Out** incluye los clips que cruzan el intervalo y los medios que usan, además de marcadores y transiciones pertinentes. **Secuencia activa** incluye toda la secuencia; **Proyecto completo** incluye todas las secuencias. Los tiempos siempre son absolutos en la secuencia: un clip situado en 00:05:00 no pasa a 00:00:00 al copiar un tramo que comienza allí.

El estado copiado contiene `instruction_example`, `supported_actions` y `usage_notes`. Para un tramo In/Out, el documento que devuelva la IA debe conservar `context_sequence_id`, `context_in_frame` y `context_out_frame` del ejemplo. La revisión rechaza un cambio que afecte clips o marcadores fuera del tramo, incluidos los clips que cruzan una marca. Para solicitar deliberadamente cambios globales, el documento puede añadir `"allow_outside_context": true`; el panel lo indica antes de aplicar.

## Ejemplo

```json
{
  "format": "prometheus-instructions",
  "version": 1,
  "project_id": "ID_DEL_PROYECTO",
  "revision": 12,
  "context_sequence_id": "ID_DE_LA_SECUENCIA",
  "context_in_frame": 300,
  "context_out_frame": 600,
  "actions": [
    {"op": "marker.create", "sequence_id": "ID_DE_LA_SECUENCIA", "at_seconds": 6, "name": "Revisar", "result": "marca"},
    {"op": "text.create", "sequence_id": "ID_DE_LA_SECUENCIA", "track_id": "ID_DE_PISTA", "at_seconds": 6, "duration_seconds": 2, "text": "Título", "result": "titulo"},
    {"op": "clip.set_transform", "sequence_id": "ID_DE_LA_SECUENCIA", "clip_id": "$titulo", "parameter_id": "scale", "number": 1.2}
  ]
}
```

`result` da nombre a un objeto creado en el lote. Un paso posterior puede usarlo como `$nombre`. Antes de aplicar, cada tarjeta muestra si es válida, si requiere atención o por qué no puede ejecutarse. Las tarjetas inválidas se omiten y no alteran las demás; el resumen muestra el resultado previsto. Al aplicar se comprueba de nuevo que el proyecto y la revisión coincidan.

## Acciones disponibles

El menú **Añadir acción** muestra los formularios agrupados por Clips, Efectos, Transiciones, Secuencias, Pistas, Medios, Texto, Subtítulos, Marcadores y MGT. Incluye acciones como `clip.set_speed`, `clip.group`, `clip.set_transform`, `media.import_local`, `media.place_asset`, `text.create`, `subtitle.create`, `marker.create` y `mgt.set_parameter`, además de las acciones anteriores. La lista exacta de operaciones y campos aceptados se genera en `supported_actions` al copiar el estado; úsala como referencia para documentos de IA. **Editar JSON avanzado** sigue disponible para casos que necesiten valores complejos.

Las acciones de curvas de fotogramas clave y máscaras animadas, y la copia automática de referencias para créditos, pertenecen a la siguiente cota.
