# Compilar las fuentes

Requisitos: Windows x64, PowerShell y SDK .NET 8.0.427, fijado en `global.json` para utilizar la misma versiÃƒÂ³n comprobada y los mismos paquetes de runtime del archivo sin conexiÃƒÂ³n. El SDK necesita red para restaurar paquetes la primera vez si no se usa el archivo de dependencias; sus versiones se fijan en los archivos `packages.lock.json`. El portable publicado no necesita el SDK. Esta fijaciÃƒÂ³n no amplÃƒÂ­a el soporte del proveedor de .NET 8.

```powershell
./Build.ps1
./Test.ps1
```

Para producir una entrega con herramientas y modelos ya disponibles:

```powershell
./Build.ps1 -ToolsDirectory 'RUTA-HERRAMIENTAS' -RuntimeDirectory 'RUTA-RUNTIME'
./Test.ps1 -RuntimeDirectory 'RUTA-RUNTIME'
```

La carpeta de herramientas contiene `yt-dlp.exe` y una subcarpeta `ffmpeg/` con los ejecutables y todas sus DLL. TambiÃ©n se admite el formato anterior de ejecutables sueltos para herramientas estÃ¡ticas. Para reutilizar la entrega completa basta pasar la carpeta `Runtime` del portable a `-RuntimeDirectory` y la carpeta del portable a `-ToolsDirectory`; FFmpeg permanece en `Runtime/ffmpeg/`. La de runtime contiene `whisper/python`, `whisper/model` y `sam2` con su repositorio y checkpoint. No deben incluirse cookies ni cuentas. La procedencia y las sumas de los componentes de la entrega estÃƒÂ¡n en los inventarios de publicaciÃƒÂ³n.

`Build.ps1` publica Client, McpBridge y MgtLive por separado como aplicaciones autÃƒÂ³nomas Windows x64; sin recorte de ensamblados ni empaquetado en un solo ejecutable. CEF requiere sus archivos auxiliares. Las versiones originales se conservan para evitar alterar render y cÃƒÂ³decs durante la preparaciÃƒÂ³n de licencias.

`Test.ps1` no inicia MainWindow ni cambia el diseÃƒÂ±o guardado del usuario. Las pruebas que necesitan Python deben ejecutarse con el runtime indicado; una suite en un entorno sin IA no valida la carga real de los modelos. Las comprobaciones GPU requieren Windows y un adaptador compatible; un resultado en CI no certifica compatibilidad con otro hardware.

Los avisos se regeneran con `python tools/Collect-Licenses.py --python RUTA-PYTHON` despuÃƒÂ©s de restaurar. El inventario por sÃƒÂ­ solo no cierra las fuentes ni los avisos que falten: revisa la lista de publicaciÃƒÂ³n antes de distribuir.

El flujo de GitHub Actions ejecuta un subconjunto que excluye las familias con comprobaciones grÃƒÂ¡ficas nativas. La suite local completa mantiene esas pruebas. CI no ha sido ejecutado en GitHub durante esta preparaciÃƒÂ³n local.

Para compilar con el archivo de dependencias adjunto, extrae `Dependencies` junto a `Build.ps1` y ejecuta `./Build.ps1 -Offline`. El SDK de .NET sigue siendo un requisito de compilaciÃƒÂ³n; no se incluye un SDK instalable en el portable.
