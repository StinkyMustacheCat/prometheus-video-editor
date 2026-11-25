# Entrega preparada — Prometheus 3.19.9

Esta entrega sustituye la preparación 3.19.8. FFmpeg 9.1 y Rubber Band 4.0.0 se compilan desde las fuentes incluidas; el ejecutable Gyan 9.0.1 no se distribuye en el portable. Los archivos Gyan conservados en ThirdPartySources son evidencia de adquisición, no fuentes correspondientes del binario actual.

El estado por componente se registra en `release-readiness.json`, las evidencias en `VERIFICACION_LICENCIAS_3_19_9.md` y las comprobaciones de los ZIP en `VERIFICACION_DEL_PAQUETE.json`. Los inventarios y avisos originales se mantienen separados por componente.

Para publicar, conserva todos los ZIP de la misma versión, `SHA256SUMS.txt` y el documento de entrega. Los archivos grandes están divididos para permanecer por debajo del límite por archivo de GitHub. No sustituyas archivos después de calcular sus sumas. Las fuentes de terceros deben acompañar a los binarios: no basta publicar solamente el portable.

La carpeta `GitHub` contiene las fuentes públicas del editor, pruebas, documentación y scripts. Las dependencias grandes pertenecen a los adjuntos de la release y se excluyen del historial Git. Las carpetas de trabajo, credenciales, cookies y proyectos reales no se incluyen.

El avance 3.19.9 se publica en la rama `release/3.19.9` y el tag `v3.19.9`, sin sustituir `main` ni el tag `v1.0.0`. Las limitaciones de hardware y del framework se describen en LIMITACIONES.md; no son una promesa de compatibilidad con equipos aún no probados.
