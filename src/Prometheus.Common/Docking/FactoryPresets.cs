using System.Collections.Generic;

namespace Prometheus.Common.Docking;

public static class FactoryPresets
{
    public const string PresetEquilibrado = "Equilibrado";
    public const string PresetMontaje = "Montaje";
    public const string PresetBRoll = "B-Roll";
    public const string PresetRevision = "Revisión";
    public const string PresetEdicionCompleta = "Edición Completa";
    public const string PresetPersonalizado = "Personalizado";

    public static readonly IReadOnlyList<PanelDefinition> StandardPanels = new List<PanelDefinition>
    {
        new("media_pool", "Bandeja de Medios", "Medios", "FolderIcon", 320, 300),
        new("monitor", "Previsualización", "Visualización", "MonitorIcon", 640, 360),
        new("timeline", "Línea de Tiempo", "Montaje", "TimelineIcon", 800, 300),
        new("properties", "Controles de medio", "Inspector", "TuneIcon", 320, 400),
        new("effects", "Efectos", "Efectos", "PaletteIcon", 320, 400),
        new("audio_mixer", "Mezclador de Audio", "Audio", "VolumeIcon", 250, 300),
        new("color", "Color y Gradación", "Color", "PaletteIcon", 320, 300),
        new("instructions", "Instrucciones", "MCP", "TuneIcon", 380, 400),
        new("transcript", "Transcripción", "Texto", "TimelineIcon", 380, 420)
    };

    public static WorkspaceLayout CreateEquilibrado()
    {
        // 1. Equilibrado (Default)
        // Top: Left (media_pool / effect_controls) | Right (monitor)
        // Bottom: timeline
        var leftCol = LayoutNode.CreateSplit(
            SplitOrientation.Vertical,
            LayoutNode.CreateLeaf("media_pool"),
            LayoutNode.CreateLeaf("properties"),
            0.50);

        var topArea = LayoutNode.CreateSplit(
            SplitOrientation.Horizontal,
            leftCol,
            LayoutNode.CreateLeaf("monitor"),
            0.30);

        var root = LayoutNode.CreateSplit(
            SplitOrientation.Vertical,
            topArea,
            LayoutNode.CreateLeaf("timeline"),
            0.65);

        return new WorkspaceLayout
        {
            Name = PresetEquilibrado,
            IsFactoryPreset = true,
            RootNode = root,
            ClosedPanels = new List<string> { "audio_mixer", "color" }
        };
    }

    public static WorkspaceLayout CreateMontaje()
    {
        // 2. Montaje: Timeline ampliada y monitor maximizado con bandeja de medios compacta
        var topArea = LayoutNode.CreateSplit(
            SplitOrientation.Horizontal,
            LayoutNode.CreateLeaf("media_pool"),
            LayoutNode.CreateLeaf("monitor"),
            0.22);

        var root = LayoutNode.CreateSplit(
            SplitOrientation.Vertical,
            topArea,
            LayoutNode.CreateLeaf("timeline"),
            0.50);

        return new WorkspaceLayout
        {
            Name = PresetMontaje,
            IsFactoryPreset = true,
            RootNode = root,
            ClosedPanels = new List<string> { "properties", "audio_mixer", "color" }
        };
    }

    public static WorkspaceLayout CreateBRoll()
    {
        // 3. B-Roll: Franja superior dedicada a biblioteca de medios con visor de miniaturas grande
        var topArea = LayoutNode.CreateSplit(
            SplitOrientation.Horizontal,
            LayoutNode.CreateLeaf("media_pool"),
            LayoutNode.CreateLeaf("monitor"),
            0.60);

        var root = LayoutNode.CreateSplit(
            SplitOrientation.Vertical,
            topArea,
            LayoutNode.CreateLeaf("timeline"),
            0.65);

        return new WorkspaceLayout
        {
            Name = PresetBRoll,
            IsFactoryPreset = true,
            RootNode = root,
            ClosedPanels = new List<string> { "properties", "audio_mixer", "color" }
        };
    }

    public static WorkspaceLayout CreateRevision()
    {
        // 4. Revisión: Monitor ampliado al 60% para inspección de color y detalle
        var rightCol = LayoutNode.CreateSplit(
            SplitOrientation.Vertical,
            LayoutNode.CreateLeaf("color"),
            LayoutNode.CreateLeaf("audio_mixer"),
            0.50);

        var topArea = LayoutNode.CreateSplit(
            SplitOrientation.Horizontal,
            LayoutNode.CreateLeaf("monitor"),
            rightCol,
            0.65);

        var root = LayoutNode.CreateSplit(
            SplitOrientation.Vertical,
            topArea,
            LayoutNode.CreateLeaf("timeline"),
            0.75);

        return new WorkspaceLayout
        {
            Name = PresetRevision,
            IsFactoryPreset = true,
            RootNode = root,
            ClosedPanels = new List<string> { "media_pool", "properties" }
        };
    }

    public static WorkspaceLayout CreateEdicionCompleta()
    {
        // 5. Edición Completa: Monitor, Efectos, Propiedades, Mezclador, Color y Timeline
        var rightCol = LayoutNode.CreateSplit(
            SplitOrientation.Vertical,
            LayoutNode.CreateLeaf("properties"),
            LayoutNode.CreateLeaf("color"),
            0.50);

        var topCenterAndRight = LayoutNode.CreateSplit(
            SplitOrientation.Horizontal,
            LayoutNode.CreateLeaf("monitor"),
            rightCol,
            0.65);

        var topRow = LayoutNode.CreateSplit(
            SplitOrientation.Horizontal,
            LayoutNode.CreateLeaf("media_pool"),
            topCenterAndRight,
            0.22);

        var bottomRow = LayoutNode.CreateSplit(
            SplitOrientation.Horizontal,
            LayoutNode.CreateLeaf("timeline"),
            LayoutNode.CreateLeaf("audio_mixer"),
            0.80);

        var root = LayoutNode.CreateSplit(
            SplitOrientation.Vertical,
            topRow,
            bottomRow,
            0.60);

        return new WorkspaceLayout
        {
            Name = PresetEdicionCompleta,
            IsFactoryPreset = true,
            RootNode = root
        };
    }

    public static IReadOnlyList<WorkspaceLayout> GetAllPresets()
    {
        return new List<WorkspaceLayout>
        {
            CreateEquilibrado(),
            CreateMontaje(),
            CreateBRoll(),
            CreateRevision(),
            CreateEdicionCompleta()
        };
    }

    public static WorkspaceLayout GetPresetByName(string name)
    {
        return name switch
        {
            PresetMontaje => CreateMontaje(),
            PresetBRoll => CreateBRoll(),
            PresetRevision => CreateRevision(),
            PresetEdicionCompleta => CreateEdicionCompleta(),
            _ => CreateEquilibrado()
        };
    }
}
