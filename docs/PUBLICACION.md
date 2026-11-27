# Entrega de revisión — Prometheus 3.22.2

Primera entrega conjunta de la integración 3.22.0–3.22.2. Incluye portable completo y fuentes para revisar detección, selección y ajustes de procesamiento. La publicación consolidada para NVIDIA/APU/híbridos corresponde a 3.22.4, después de probar este paquete en esos equipos. No se ha subido esta integración automáticamente a GitHub.

El estado exacto y los resultados se registran en `release-readiness.json` y `Validacion/`. La base multimedia conserva FFmpeg `9.1-prometheus-3.19.9` y Rubber Band 4.0.0, con sus fuentes y recetas; no incluye el ejecutable Gyan antiguo. La IA conserva CUDA y añade los proveedores y el runtime CPU de la rama AMD, aislados y con sus avisos originales. El inventario adicional figura en `licenses/processing-components.json`.

Los ZIP de revisión comparten una carpeta raíz; extraer Portable y todos los AI-XX juntos. Source y Dependencies son independientes de los binarios. Los ZIP numerados son archivos ZIP autónomos. Se conservan las fuentes correspondientes de terceros de la base y los avisos/procedencia de los nuevos componentes. Antes de una publicación nueva, revisar este inventario junto al cierre multiplataforma previsto en 3.22.4.

La carpeta `GitHub` contiene fuentes, pruebas, documentación y scripts. Dependencias grandes, proyectos personales, credenciales, cookies, ajustes del equipo y carpetas de trabajo se excluyen. Cada paquete tiene una suma SHA-256. Las comprobaciones en un PC no certifican otro modelo de GPU.
