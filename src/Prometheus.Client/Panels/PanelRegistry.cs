using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Prometheus.Common.Docking;

namespace Prometheus.Client.Panels;

public static class PanelRegistry
{
    private static readonly Dictionary<string, Func<Control>> Factories = new(StringComparer.OrdinalIgnoreCase)
    {
        { "media_pool", () => new MediaPoolPanelView() },
        { "monitor", () => new MonitorPanelView() },
        { "timeline", () => new TimelinePanelView() },
        { "properties", () => new PropertiesPanelView() },
        { "effects", () => new EffectControlsPanelView() },
        { "audio_mixer", () => new AudioMixerPanelView() },
        { "color", () => new ColorPanelView() },
        { "instructions", () => new InstructionsPanelView() },
        { "transcript", () => new TranscriptPanelView() }
    };

    private static readonly Dictionary<string, Control> _panelCache = new(StringComparer.OrdinalIgnoreCase);

    public static Control? GetExistingPanelContent(string panelId) =>
        _panelCache.TryGetValue(panelId, out var content) ? content : null;

    /// <summary>
    /// Obtiene o reutiliza la instancia única del panel para evitar reconstruir vistas
    /// y preservar estado de UI (zoom, reproducción, scroll) durante el ciclo de vida de acople.
    /// </summary>
    public static Control GetOrCreatePanelContent(string panelId)
    {
        if (_panelCache.TryGetValue(panelId, out var existing))
        {
            // Desvincular de su contenedor anterior si lo tuviera
            if (existing.Parent is Decorator decorator)
            {
                decorator.Child = null;
            }
            else if (existing.Parent is Panel panel)
            {
                panel.Children.Remove(existing);
            }
            else if (existing.Parent is ContentControl cc)
            {
                cc.Content = null;
            }

            return existing;
        }

        var instance = CreatePanelContent(panelId);
        _panelCache[panelId] = instance;
        return instance;
    }

    public static Control CreatePanelContent(string panelId)
    {
        if (Factories.TryGetValue(panelId, out var factory))
        {
            return factory();
        }

        // Fallback placeholder
        return new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0E0E0E")),
            Child = new TextBlock
            {
                Text = $"Panel: {panelId}",
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#888888")),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };
    }

    public static PanelDefinition? GetDefinition(string panelId)
    {
        foreach (var def in FactoryPresets.StandardPanels)
        {
            if (string.Equals(def.Id, panelId, StringComparison.OrdinalIgnoreCase))
                return def;
        }

        return null;
    }

    public static string GetPanelTitle(string panelId)
    {
        var def = GetDefinition(panelId);
        return def?.Title ?? panelId;
    }
}
