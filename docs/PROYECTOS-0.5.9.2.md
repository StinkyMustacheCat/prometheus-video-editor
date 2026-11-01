# Proyectos Prometheus 0.5.9.2

## Archivo y recursos

`Nombre.pthproj` es el archivo principal de edición: contiene secuencias, clips, enlaces, efectos, IDs estables y ajustes. Es JSON con el contrato interno de proyecto 0.5.0; la versión del programa y la extensión no implican un cambio del esquema interno.

`Nombre.assets` se crea al guardar o generar el primer recurso gestionado. Las subcarpetas aparecen cuando hacen falta:

- `medios/web`: descargas web completas e imágenes web que ya pertenecen al proyecto.
- `medios/importados`: imágenes locales o pegadas incorporadas al proyecto.
- `medios/mgt`: reservado para distinguir originales `.mgtproj` y `.mgt.json` en la integración 0.6; todavía no se importan.
- `recursos/luts`: tablas de color incorporadas.
- `cache/vistas-ligeras`: representaciones regenerables. La carpeta puede incluir archivos de edición que no son el original.

Los medios locales de vídeo y audio permanecen enlazados por defecto. Sus archivos no se copian dentro de `Nombre.assets`. Las rutas de recursos situados junto al proyecto se guardan de forma relativa para que el par `.pthproj` + `.assets` pueda trasladarse. Los tramos web descargados también conservan rutas relativas.

## Apertura y recuperación

Al abrir, Prometheus comprueba los medios y LUT. Si alguno falta o ha cambiado, muestra una ventana de resolución antes de entrar al editor. Permite buscar archivos, buscar los medios locales por nombre dentro de una carpeta, conectar un archivo descargado a un medio web, preparar otra vista ligera, volver a descargarlo completo o sustituir su enlace. Si se cancela, permanece abierto el proyecto anterior. Una URL guardada no garantiza que la publicación siga disponible.

Los archivos de recuperación automática de proyectos `.pthproj` se guardan en el almacenamiento local de Prometheus, separados de `Nombre.assets`; las copias del archivo anterior usan `.bak`. «Guardar como» escribe otro `.pthproj` y traslada los recursos gestionados a la carpeta correspondiente sin mover los originales locales externos.

## Asociación en Windows

Prometheus registra el icono de documento y se anuncia como aplicación capaz de abrir `.pthproj` para el usuario actual. Windows conserva la elección de aplicación predeterminada. Al arrancar desde otra ubicación, Prometheus actualiza silenciosamente las rutas de su icono y de su comando de apertura. Hasta ese arranque, el icono puede aparecer genérico si la ruta anterior dejó de existir.

## Límites de esta entrega

No existe todavía «Recopilar proyecto», importación MGT, render MGT ni importación de un proyecto Prometheus dentro de otro. Copiar solamente el `.pthproj` permite abrir la edición únicamente cuando los recursos gestionados y los originales enlazados siguen accesibles o pueden recuperarse.
