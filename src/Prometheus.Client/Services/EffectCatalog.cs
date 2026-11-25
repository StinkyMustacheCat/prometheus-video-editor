using System.Collections.Generic;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Services;

public sealed record EffectCatalogEntry(string Id, string Name, string Kind, string Family, string Description)
{
    public string FamilyKey => Kind + ":" + Family;
}

public static class EffectCatalog
{
    // Only effects that can be applied and previewed in the current client appear here.
    public static IReadOnlyList<EffectCatalogEntry> Available { get; } = new[]
    {
        new EffectCatalogEntry(BasicColorEffect.Id, "Corrección básica", "video", "Color", "Brillo, contraste y saturación"),
        new EffectCatalogEntry(ExposureEffect.Id, "Exposición", "video", "Color", "Pasos de luz y nivel de negro"),
        new EffectCatalogEntry(LevelsEffect.Id, "Niveles", "video", "Color", "Negro, blanco y gamma"),
        new EffectCatalogEntry(MonochromeEffect.Id, "Blanco y negro", "video", "Color", "Escala de grises"),
        new EffectCatalogEntry(DynamicMotionEffect.ZoomInId, "Zoom In dinámico", "video", "Movimiento", "Acercamiento durante el clip"),
        new EffectCatalogEntry(DynamicMotionEffect.ZoomOutId, "Zoom Out dinámico", "video", "Movimiento", "Alejamiento durante el clip"),
        new EffectCatalogEntry(DynamicMotionEffect.PanId, "Paneo dinámico", "video", "Movimiento", "Desplazamiento en una dirección"),
        new EffectCatalogEntry(DynamicMotionEffect.JourneyId, "Recorrido", "video", "Movimiento", "Posición y escala entre dos encuadres"),
        new EffectCatalogEntry(ColorFinishingEffect.WhiteBalanceId, "Temperatura y matiz", "video", "Color", "Balance de blancos"),
        new EffectCatalogEntry(ColorFinishingEffect.ShadowsHighlightsId, "Sombras y luces", "video", "Color", "Detalle en zonas oscuras y claras"),
        new EffectCatalogEntry(ColorFinishingEffect.HueId, "Tono", "video", "Color", "Girar el tono de color"),
        new EffectCatalogEntry(ColorFinishingEffect.VibranceId, "Intensidad de color", "video", "Color", "Saturación adaptativa"),
        new EffectCatalogEntry(ColorFinishingEffect.VignetteId, "Viñeta", "video", "Color", "Oscurecer bordes del cuadro"),
        new EffectCatalogEntry(ImageGeometryEffect.FlipHorizontalId, "Voltear horizontal", "video", "Imagen", "Reflejar de izquierda a derecha"),
        new EffectCatalogEntry(ImageGeometryEffect.FlipVerticalId, "Voltear vertical", "video", "Imagen", "Reflejar de arriba abajo"),
        new EffectCatalogEntry(ImageGeometryEffect.CropId, "Recorte de bordes", "video", "Imagen", "Recortar los cuatro bordes"),
        new EffectCatalogEntry(ImageGeometryEffect.PixelizeId, "Pixelar", "video", "Imagen", "Tamaño del bloque de píxeles"),
        new EffectCatalogEntry(GpuFilterEffect.BlurId, "Desenfoque gaussiano", "video", "Imagen", "Desenfoque separable en GPU"),
        new EffectCatalogEntry(GpuFilterEffect.ChromaId, "Croma por color", "video", "Imagen", "Eliminar un color elegido para mostrar las capas inferiores"),
        new EffectCatalogEntry(GpuFilterEffect.ChromaRgbId, "Croma RGB", "video", "Imagen", "Eliminar rojo, verde y azul de forma independiente"),
        new EffectCatalogEntry(GpuFilterEffect.ChromaMultiId, "Croma múltiple", "video", "Imagen", "Eliminar hasta tres colores con ajustes compartidos o individuales"),
        new EffectCatalogEntry(GpuDetailEffect.SharpenId, "Nitidez", "video", "Imagen", "Realzar detalles"),
        new EffectCatalogEntry(GpuDetailEffect.GlowId, "Resplandor suave", "video", "Imagen", "Halo sobre las zonas luminosas"),
        new EffectCatalogEntry(GpuLutEffect.Id, "LUT 3D", "video", "Color", "Aplicar archivo .cube en GPU"),
        new EffectCatalogEntry(VideoTransitionOperations.Dissolve, VideoTransitionOperations.Name(VideoTransitionOperations.Dissolve),
            "transitions", "Video", "Mezcla los fotogramas de ambos lados del corte"),
        new EffectCatalogEntry(VideoTransitionOperations.DipBlack, VideoTransitionOperations.Name(VideoTransitionOperations.DipBlack),
            "transitions", "Video", "Baja a negro y vuelve a la imagen en el corte"),
        new EffectCatalogEntry(VideoTransitionOperations.DipWhite, VideoTransitionOperations.Name(VideoTransitionOperations.DipWhite),
            "transitions", "Video", "Pasa por blanco en el corte"),
        new EffectCatalogEntry(AudioTransitionOperations.FadeIn, AudioTransitionOperations.Name(AudioTransitionOperations.FadeIn),
            "transitions", "Audio", "Sube el volumen desde el inicio del clip"),
        new EffectCatalogEntry(AudioTransitionOperations.FadeOut, AudioTransitionOperations.Name(AudioTransitionOperations.FadeOut),
            "transitions", "Audio", "Baja el volumen al final del clip"),
        new EffectCatalogEntry(AudioTransitionOperations.Crossfade, AudioTransitionOperations.Name(AudioTransitionOperations.Crossfade),
            "transitions", "Audio", "Cruza el audio de dos clips contiguos en el corte"),
        new EffectCatalogEntry(AudioEffects.HighPassId, "Corte de graves", "audio", "Filtros", "Paso alto"),
        new EffectCatalogEntry(AudioEffects.LowPassId, "Corte de agudos", "audio", "Filtros", "Paso bajo"),
        new EffectCatalogEntry(AudioEffects.EqualizerId, "Ecualizador de tres bandas", "audio", "Filtros", "Graves, medios y agudos"),
        new EffectCatalogEntry(AudioEffects.CompressorId, "Compresor", "audio", "Dinámica", "Control de rango dinámico"),
        new EffectCatalogEntry(AudioEffects.LimiterId, "Limitador", "audio", "Dinámica", "Limitar picos"),
        new EffectCatalogEntry(AudioEffects.GateId, "Puerta de ruido", "audio", "Dinámica", "Reducir ruido entre frases"),
        new EffectCatalogEntry(AudioEffects.DeEsserId, "Reducción de sibilancias", "audio", "Voz", "Atenuar las eses"),
        new EffectCatalogEntry(AudioEffects.VoiceCleanId, "Limpiar y nivelar voz", "audio", "Voz", "Filtro, limpieza y nivelado"),
        new EffectCatalogEntry(AudioEffects.PitchId, "Tono sin cambiar duración", "audio", "Creativos", "Cambiar altura de voz o música"),
        new EffectCatalogEntry(AudioEffects.EchoId, "Eco", "audio", "Creativos", "Repeticiones con decaimiento"),
        new EffectCatalogEntry(AudioEffects.ChorusId, "Coro", "audio", "Creativos", "Modulación y duplicación"),
        new EffectCatalogEntry(AudioEffects.TremoloId, "Trémolo", "audio", "Creativos", "Oscilación de amplitud")
    }.Concat(MgtTransitionCatalog.Factory.Select(MgtTransitionCatalog.Entry)).ToArray();
}
