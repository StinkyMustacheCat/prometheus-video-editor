# Prometheus 0.5.3 · Panel de instrucciones

Esta versión añade una bandeja de cambios por lotes. Se abre desde **Paneles → Instrucciones** y puede acoplarse o flotar como los otros paneles. Arrastra un efecto o una transición desde su catálogo hasta la bandeja. Haz doble clic en la tarjeta para elegir **aplicar/quitar**, alcance y parámetros. Las tarjetas se pueden reordenar, revisar y aplicar juntas. **Aplicar** crea un único paso de deshacer.

**Copiar estado ▾** ofrece la secuencia activa o el proyecto completo. Copia IDs y datos de edición, no archivos multimedia, imágenes incrustadas ni rutas de origen. Puedes dar ese texto a una IA y pedirle un documento `prometheus-instructions` versión 1. Pégalo con **Pegar instrucciones**; Prometheus lo convertirá en tarjetas. El programa no interpreta prosa libre ni ejecuta código recibido.

Ejemplo mínimo (sustituye IDs y revisión por los que figuren en el estado copiado):

```json
{
  "format": "prometheus-instructions",
  "version": 1,
  "project_id": "ID_DEL_PROYECTO",
  "revision": 12,
  "actions": [
    {
      "op": "effect.apply",
      "scope": "track",
      "sequence_id": "ID_DE_SECUENCIA",
      "track_id": "ID_DE_PISTA",
      "catalog_id": "prometheus.color.basic"
    },
    {
      "op": "clip.inherit_track_effects",
      "sequence_id": "ID_DE_SECUENCIA",
      "clip_id": "ID_DE_CLIP",
      "value": false
    }
  ]
}
```

Acciones admitidas:

| Operación | Campos indispensables |
|---|---|
| `effect.apply`, `effect.remove` | `catalog_id`; `scope`: `clip`, `selection`, `track` o `sequence`. Para `clip`, `clip_id`; para `selection`, `clip_ids`; para `track`, `track_id`. LUT 3D al aplicar: `resource_path` a un `.cube` local. |
| `transition.apply` | `catalog_id`, `duration_frames`; `scope`: `junction` con `left_clip_id` y `right_clip_id`, o `sequence` para todos los cortes válidos. |
| `transition.remove` | Igual alcance que arriba; `catalog_id` del tipo a quitar o `"*"` para todas las transiciones del alcance. |
| `clip.move` | `clip_id`, `at_seconds`, `track_id`, `placement`: `empty`, `overwrite` o `insert`. Conserva el comportamiento habitual de medios vinculados. |
| `clip.trim` | `clip_id`, `edge`: `left` o `right`, `duration_seconds`, `ripple`: `true` o `false`. `false` deja hueco. |
| `clip.duplicate` | `clip_id`, `at_seconds`, `track_id`, `placement`: `empty` o `overwrite`. Duplica también los componentes vinculados. |
| `clip.inherit_track_effects` | `clip_id`, `value`: `true` o `false`. |

Los tiempos `at_seconds` y `duration_seconds` son segundos decimales. En el editor de tarjetas también se acepta `HH:MM:SS:FF`. Los nombres son solo ayuda visual: **las acciones señalan IDs**, por lo que dos clips con el mismo nombre no se confunden. La selección se captura al crear la tarjeta; no cambia al seleccionar otros clips después.

**Revisar** simula el lote en una copia del proyecto y muestra los cambios y destinos omitidos. Las pistas bloqueadas, los tipos incompatibles y las transiciones sin margen suficiente no se alteran. Una instrucción inválida detiene el lote sin cambiar el proyecto. Si la revisión del proyecto cambió desde la copia del estado, prepara un documento nuevo.

MGT aún no se renderiza en Prometheus. Cuando se integre como medio, las acciones generales sobre clips y pistas podrán usarse sin cambiar este formato. Los parámetros internos de MGT y sus transiciones tendrán acciones propias más adelante.
