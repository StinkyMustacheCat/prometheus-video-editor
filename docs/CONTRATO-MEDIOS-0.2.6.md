# Contrato de medios 0.2.6

`Asset.id` identifica el medio de forma estable. `Clip.asset_id` y `Clip.stream_id` no contienen rutas. `source` guarda `kind`, `uri`, `relative`, `page_url`, `imported_at` y `resolver_type` opcionales. `representations[]` guarda `role` (original/preview/proxy/final), `locator`, `size_bytes`, `modified_utc`, `sha256` opcional y `stream_ordinals` para mapear streams lógicos a archivos físicos. Un archivo local se importa con una sola representación `original`.

`StreamInfo` mantiene sus marcas de tiempo, base temporal, duración y FPS racional. Añade dimensiones, formato de píxeles, disposición de canales y etiquetas de color cuando FFprobe las informa. `recommended_duration` y `extensions` permanecen disponibles para medios generados y módulos futuros.

Los campos web/MGT y los roles preview/proxy/final son datos serializables, no procesos activos. No hay funciones vacías de descarga, resolución ni render MGT. La disponibilidad se comprueba contra los archivos reales; una representación ausente no cambia la identidad del Asset. Esta entrega no interpreta efectos ni extensiones de render desconocidas.
