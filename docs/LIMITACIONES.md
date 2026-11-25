# Alcance y limitaciones

- Distribución probada para Windows x64 y el hardware de desarrollo NVIDIA. La interfaz Avalonia no implica compatibilidad de todo el motor con Linux, macOS, AMD o cualquier GPU NVIDIA.
- SAM 2.1 utiliza CUDA y autocast BF16. No se promete que funcione en tarjetas antiguas sin soporte adecuado, aunque otras funciones del editor puedan funcionar.
- H.264/HEVC aprovechan NVENC cuando está disponible. ProRes y ciertos formatos con alfa recurren a CPU en parte del flujo. La compatibilidad depende del códec, profundidad, perfil y contenedor del archivo.
- MGT ejecuta HTML/CSS/JavaScript. Importa plantillas de confianza. Las transiciones GPU utilizan el contrato específico de Prometheus; no cualquier transición HTML se transforma automáticamente en un shader.
- Las grabaciones HDR de iPhone y la igualdad exacta de color entre reproductores requieren validación con el material y la pantalla de destino. No se promete equivalencia universal de color.
- El motor actual usa .NET 8. Su soporte del proveedor terminó el 10 de noviembre de 2026. Esta entrega conserva la versión funcional existente; la migración a una versión con soporte queda como trabajo separado. Fuente: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- Los binarios no están firmados con un certificado comercial. La procedencia se comprueba con las sumas publicadas por separado; una suma entregada por el mismo canal no sustituye una firma.
- La entrega autónoma incluye dependencias, pero algunas cachés se guardan en la raíz del disco y los ajustes en datos locales del usuario. Se requiere permiso de escritura.
- Autónomo respecto a .NET no equivale a un Windows sin prerrequisitos: las bibliotecas nativas requieren Visual C++ Redistributable x64. El instalador oficial se adjunta; su instalación puede requerir permisos de administrador. No se copian DLL desconocidas del directorio del sistema.
- Las pruebas automáticas verifican casos concretos y no equivalen a una campaña de edición real en otros equipos. La lectura del inventario de licencias tampoco certifica derechos que no se hayan podido acreditar.
