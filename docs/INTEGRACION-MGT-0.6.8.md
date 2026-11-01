# Integración MGT · Prometheus 0.6.8

- Motor incluido: MGT 1.6.4, contrato de assets v2 sin cambios de esquema.
- Detección: `.mgt.json`, `.mgtproj` y sufijos de copia de Windows `.mgt (N).json`. El nombre identifica candidatos; importación/preflight valida el documento real.
- Espacios reservados: archivo local MGT o documento JSON pegado. Se conservan inicio y duración del clip, usando la misma regla de cola corta que otros medios.
- Portapapeles: Ctrl+V reconoce un documento MGT JSON completo en Medios o línea de tiempo. La importación ocurre en segundo plano mediante el motor MGT. No se interpretan fragmentos sueltos de HTML/JS como un medio.
- Duración: al reconectar o resolver un MGT se actualizan `encodedDuration`, los streams y las franjas de contenido faltante. El clip de la línea de tiempo no cambia de duración automáticamente.
- La duración propia nueva de un clip interno de `.mgtproj` no se expone individualmente por el contrato v2. Prometheus recibe y renderiza la duración final del proyecto completo.

Verificación: compilación completa, importación real de `.mgt (1).json`, conexión de un MGT de 2 s a un espacio reservado de 5 s, guardado con extensión canónica, cambio de fuente de 10 a 6 y 20 s, render de vista ligera y exportación con MGT 1.6.4.
