# Prometheus 0.5.5 · Panel de instrucciones

Abre **Paneles → Instrucciones**. **Copiar estado** entrega a una IA los IDs, posiciones, clips, pistas, efectos y transiciones del proyecto, sin incrustar archivos multimedia. Pide como respuesta un documento JSON `prometheus-instructions` versión 1. En **Pegar instrucciones**, reemplaza el contenido del cuadro por el JSON completo, pulsa **Cargar tarjetas**, luego **Revisar** y finalmente **Aplicar**. Cargar tarjetas no edita el proyecto. Aplicar crea un único paso de deshacer.

Usa siempre el `project_id`, la `revision` y los IDs del estado recién copiado. Si el proyecto cambia, copia el estado de nuevo. Los tiempos se expresan en segundos decimales. Una referencia como `$corte.right` señala el segmento creado por una acción anterior **del mismo documento**. Los nombres de resultado no persisten después de aplicar; copia el estado para obtener los nuevos IDs.

## Ejemplo: dividir y cambiar solo la mitad derecha

```json
{
  "format": "prometheus-instructions",
  "version": 1,
  "project_id": "ID_DEL_PROYECTO",
  "revision": 12,
  "actions": [
    {"op":"clip.cut","clip_id":"ID_DEL_CLIP","at_seconds":5,"result":"corte"},
    {"op":"effect.apply","scope":"clip","clip_id":"$corte.right","catalog_id":"prometheus.color.basic","result":"color"},
    {"op":"effect.set_parameter","clip_id":"$corte.right","effect_id":"$color","parameter_id":"saturation","number":1.4}
  ]
}
```

Los cortes conservan el vínculo de audio y vídeo salvo que la instrucción indique `"include_linked":false`. `clip.delete` usa `"ripple":false` para dejar hueco o `true` para cerrarlo. La revisión muestra los clips que aparecen, salen, se mueven o cambian duración; un lote inválido no altera el proyecto.

## Ejemplo: crear estructura e importar un enlace

```json
{
  "format":"prometheus-instructions",
  "version":1,
  "project_id":"ID_DEL_PROYECTO",
  "revision":12,
  "actions":[
    {"op":"sequence.create","name":"Vídeo vertical","width":1080,"height":1920,"fps":60,"result":"vertical"},
    {"op":"track.create","sequence_id":"$vertical","source_kind":"video","name":"Entrevista","result":"entrevista"},
    {"op":"media.import_url","sequence_id":"$vertical","url":"https://www.youtube.com/watch?v=EJEMPLO","source_kind":"video","track_id":"$entrevista","at_seconds":20,"placement":"empty","result":"video"}
  ]
}
```

`media.import_url` sin `track_id` añade el enlace solo al panel de medios. Con `track_id` lo coloca en la línea de tiempo. `source_kind` es `video` o `audio`; al usar `audio`, Prometheus obtiene solo el sonido. `placement` admite `empty` (busca pista libre), `overwrite` o `insert`. La revisión consulta título y duración del enlace, sin descargarlo. Al aplicar se crea el medio virtual y continúa la preparación de la vista ligera en segundo plano. Se admiten las plataformas web compatibles con la importación normal de Prometheus; los enlaces de imagen no forman parte de esta acción.

Las secuencias nuevas pueden elegir dimensiones, pero el núcleo de secuencias **aún admite únicamente 60 FPS**. La exportación puede convertir a otras tasas.

## Ejemplo: rellenar un espacio reservado

```json
{
  "format":"prometheus-instructions",
  "version":1,
  "project_id":"ID_DEL_PROYECTO",
  "revision":12,
  "actions":[
    {"op":"placeholder.fill_url","clip_id":"ID_DEL_ESPACIO","url":"https://www.youtube.com/watch?v=EJEMPLO"}
  ]
}
```

La acción conserva posición y duración del espacio. Si está vinculado a otro espacio de audio o vídeo, rellena ambos de forma predeterminada. Usa `"include_linked":false` para afectar solo al señalado. Un espacio de audio sin vídeo vinculado importa únicamente sonido. Si el medio es más corto, la cola mantiene las franjas de medio faltante. La revisión comprueba compatibilidad y bloqueo de pistas antes de aplicar.

## Acciones disponibles

| Grupo | Operaciones |
|---|---|
| Montaje | `clip.move`, `clip.trim`, `clip.duplicate`, `clip.delete`, `clip.cut`, `clip.range_trim`, `range.trim`, `clip.slip`, `clip.roll`, `clip.link`, `clip.unlink`, `gap.close` |
| Efectos | `effect.apply`, `effect.remove`, `effect.set_parameter`, `effect.set_enabled`, `effect.move`, `effect.remove_instance`, `clip.inherit_track_effects` |
| Transiciones | `transition.apply`, `transition.remove`, `transition.set_duration` |
| Proyecto | `sequence.create`, `sequence.rename`, `sequence.activate`, `track.create`, `track.rename`, `track.set_locked` |
| Medios web | `media.import_url`, `placeholder.fill_url` |

El estado copiado incluye `supported_actions` con los campos de cada familia. Para editar una tarjeta de una acción nueva, haz doble clic: se abre su JSON individual. Los efectos y transiciones arrastrados desde el catálogo conservan su editor visual de tarjetas. Los parámetros de efecto requieren el ID de la **instancia**; puedes obtenerlo del estado o asignar `result` a `effect.apply` y usar `$result` después. `effect.move` usa `index` desde cero.

Esta versión no interpreta lenguaje natural dentro de Prometheus. La IA transforma tu petición y el estado copiado en el JSON; Prometheus valida y ejecuta únicamente las operaciones admitidas. MGT se añadirá como medio en una fase posterior.
