# Prometheus 0.3.3 — parche incremental

Aplica los parches 0.3.1 y 0.3.2 sobre la entrega completa 0.3.0. Después extrae este ZIP sobre la misma carpeta `Prometheus-0.3.0-win-x64`, con la aplicación cerrada y aceptando reemplazos.

**Importar enlace** reconoce vídeos individuales públicos de YouTube, Instagram (publicaciones y Reels), TikTok, Facebook (vídeos y Reels), X/Twitter (publicaciones con vídeo) y Twitch (clips y VOD terminados). El medio virtual aparece con título, duración y metadatos disponibles. La vista ligera se prepara en segundo plano; **Resolver medios utilizados** obtiene el archivo completo y la exportación también lo resuelve si hace falta. Las imágenes web directas siguen funcionando.

No se aceptan listas, perfiles ni directos de Twitch. Si el enlace exige inicio de sesión, está restringido o yt-dlp no puede leerlo, Prometheus muestra el fallo y no crea un medio incompleto. La disponibilidad de cada enlace depende de la plataforma.

Prueba sugerida: pega un vídeo público de X y un clip o VOD público de Twitch, comprueba duración y miniatura, colócalos en la timeline, edita con la vista ligera y resuelve el medio final. Repite con Instagram, TikTok y Facebook si usas esas plataformas.

Validación automática: clasificación de URLs, rechazo de perfiles/directos/listas y creación de medios virtuales para X y Twitch. La red del entorno de compilación no resolvió dominios externos durante las pruebas en vivo; esas plataformas necesitan tu prueba manual con enlaces públicos vigentes.
