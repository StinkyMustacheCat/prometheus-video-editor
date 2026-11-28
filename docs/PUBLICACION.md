# Publicación de Prometheus

## Código fuente

El repositorio contiene fuentes, pruebas, documentación y scripts de compilación. Para 3.23.6, publicar la rama `release/3.23.6` y la etiqueta `v3.23.6`. El SDK fijado en `global.json` y los archivos `packages.lock.json` permiten verificar el código desde una copia limpia en Windows.

El portable 3.23.6 se generó desde el commit `a104a31`. La documentación de publicación y los pendientes se añadieron después de esa compilación.

## Distribución del portable

Los binarios, modelos, registros de prueba y paquetes de dependencias se preparan fuera del árbol de código. Antes de distribuirlos, comprobar que cada componente tiene sus avisos, las fuentes correspondientes y la procedencia que le aplica. Los archivos de la entrega se publican con sus sumas SHA-256. [Inventario de terceros](../licenses/TERCEROS.md) · [Fuentes de terceros](FUENTES_TERCEROS.md)

La entrega de integración previa y su matriz de hardware siguen documentadas en [Prometheus 3.22.2](ENTREGA_3_22_2.md).
