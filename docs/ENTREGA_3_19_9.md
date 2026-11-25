# Entrega completa — Prometheus 3.19.9

La entrega reúne el portable Windows x64 con IA y multimedia, las fuentes del editor, los paquetes NuGet sin conexión, las fuentes de terceros, licencias, inventarios y sumas de comprobación. Se preparó en D y se conserva la entrega anterior como respaldo.

FFmpeg 9.1 y Rubber Band 4.0.0 se compilan desde fuentes incluidas. Se reemplaza el ejecutable estático que tenía materiales de construcción sin acreditar. El componente multimedia y sus DLL están en `Runtime/ffmpeg`; no dependen de la instalación anterior del equipo. Las rutas explícitas de configuración siguen funcionando.

## Validación

- unit_tests: {"passed": 507, "failed": 0, "skipped": 0}
- exports: {"passed": true, "formats": [{"file": "h264.mp4", "codec": "h264", "duration": 1.0, "pixel_format": "yuv420p", "frames": "30", "color_space": "bt709"}, {"file": "h264-cpu.mp4", "codec": "h264", "duration": 1.0, "pixel_format": "yuv420p", "frames": "30", "color_space": "bt709"}, {"file": "hevc.mp4", "codec": "hevc", "duration": 1.0, "pixel_format": "yuv420p", "frames": "30", "color_space": "bt709"}, {"file": "hevc-cpu.mp4", "codec": "hevc", "duration": 1.0, "pixel_format": "yuv420p", "frames": "30", "color_space": "bt709"}, {"file": "422.mov", "codec": "prores", "duration": 1.0, "pixel_format": "yuv422p10le", "frames": "30", "color_space": "bt709"}, {"file": "4444.mov", "codec": "prores", "duration": 1.0, "pixel_format": "yuva444p12le", "frames": "30", "color_space": "bt709"}, {"file": "audio.wav", "codec": "pcm_s16le", "duration": 1.0, "pixel_format": null, "frames": null, "color_space": null}], "prores4444_alpha_range": [0, 129], "gpu_vs_cpu_color_mean_absolute_error": 0.001851851851851852, "rubberband_input_hz": 440, "rubberband_output_hz": 660.0, "ffmpeg_nvenc_encoders": ["h264_nvenc", "hevc_nvenc"]}
- portable: {"passed": true, "checks": ["self-contained MCP: 11 tools", "bundled FFmpeg with Windows-only PATH", "CEF GPU texture, alpha, live parameters and interprocess sharing", "relocated Python imports, CUDA detection and local Whisper load"]}
- source_build: {"passed": true, "sdk": "8.0.427", "empty_nuget_cache": true, "local_feed_only": true, "components": ["Client", "McpBridge", "MgtLive"], "offline_nuget_packages": 175, "rebuilt_mcp_and_multimedia_startup": true}

Las fuentes auxiliares incluyen 134 paquetes MSYS2, 470 checksums de código/parches verificados y dependencias Rust originales. Se conservan por separado los términos de NVIDIA, Microsoft, modelos y demás proveedores. El inventario del portable identifica los archivos efectivos; el inventario de fuentes incluye también material auxiliar para reconstrucción.

## Archivos

- `Portable/Prometheus-3.19.9`: programa completo ya extraído.
- `GitHub`: fuentes públicas del editor y documentación.
- `Dependencies`: paquetes NuGet originales para compilar sin descargarlos.
- `ThirdPartySources`: fuentes, parches, recetas, entradas y manifiestos de terceros.
- `Release-assets`: ZIP divididos para GitHub y SHA256SUMS.txt.

Para ejecutar desde los ZIP, extrae Portable y todos los AI-XX en la misma ubicación: comparten la raíz Prometheus-3.19.9. Para compilar el editor, extrae Source y Dependencies juntos. Para conservar o publicar los materiales correspondientes, extrae todos los ThirdPartySources-XX juntos. Los ZIP numerados son archivos ZIP independientes, no fragmentos binarios de un único ZIP.

Las comprobaciones se realizaron con material sintético sin abrir MainWindow ni proyectos del usuario. Las limitaciones de hardware/framework siguen documentadas en LIMITACIONES.md. La preparación local no incluye una publicación remota.
