using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class TimelinePanelView
{
    private sealed record ShortcutSpec(string Id, string Name, Key Key, KeyModifiers Modifiers = KeyModifiers.None);
    private static readonly ShortcutSpec[] ShortcutSpecs =
    {
        new("tool.select", "Herramienta selección", Key.V), new("tool.razor", "Herramienta cuchilla", Key.C),
        new("tool.direction", "Selección direccional", Key.A), new("tool.direction.backward", "Selección hacia atrás", Key.A, KeyModifiers.Shift),
        new("tool.hand", "Herramienta mano", Key.H), new("tool.zoom", "Herramienta zoom", Key.Z),
        new("tool.sam", "Máscara", Key.G),
        new("tool.text", "Herramienta texto", Key.T),
        new("timeline.snap", "Encaje magnético", Key.S), new("edit.divide", "Dividir en el lector", Key.K, KeyModifiers.Control),
        new("edit.copy", "Copiar", Key.C, KeyModifiers.Control), new("edit.cut", "Cortar", Key.X, KeyModifiers.Control),
        new("edit.paste", "Pegar", Key.V, KeyModifiers.Control), new("edit.delete", "Eliminar", Key.Delete),
        new("edit.delete.back", "Eliminar (alternativo)", Key.Back),
        new("edit.undo", "Deshacer", Key.Z, KeyModifiers.Control), new("edit.redo", "Rehacer", Key.Z, KeyModifiers.Control | KeyModifiers.Shift),
        new("marker.add", "Añadir marcador", Key.M), new("range.in", "Marcar entrada", Key.I), new("range.out", "Marcar salida", Key.O),
        new("marker.previous", "Marcador anterior", Key.PageUp), new("marker.next", "Marcador siguiente", Key.PageDown),
        new("marker.previous.ctrl", "Marcador anterior (alternativo)", Key.Left, KeyModifiers.Control),
        new("marker.next.ctrl", "Marcador siguiente (alternativo)", Key.Right, KeyModifiers.Control),
        new("frame.previous", "Fotogramas atrás", Key.Left), new("frame.next", "Fotogramas adelante", Key.Right),
        new("frame.previous.fast", "Fotogramas atrás ×5", Key.Left, KeyModifiers.Shift),
        new("frame.next.fast", "Fotogramas adelante ×5", Key.Right, KeyModifiers.Shift),
        new("cut.previous", "Corte anterior", Key.Up), new("cut.next", "Corte siguiente", Key.Down),
        new("timeline.start", "Inicio del montaje", Key.Home), new("timeline.end", "Final del montaje", Key.End),
        new("shelf.capture", "Capturar selección a Bandeja de enlaces (global)", Key.X, KeyModifiers.Control | KeyModifiers.Alt)
    };

    private static readonly string ShortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prometheus", "shortcuts-v2.json");
    private readonly Dictionary<string, (Key Key, KeyModifiers Modifiers)> _shortcutBindings = LoadShortcutBindings();
    public static event Action<Key, KeyModifiers>? ShelfShortcutChanged;
    public static (Key Key, KeyModifiers Modifiers) GetShelfShortcut() => LoadShortcutBindings()["shelf.capture"];

    private static Dictionary<string, (Key, KeyModifiers)> LoadShortcutBindings()
    {
        var result = ShortcutSpecs.ToDictionary(x => x.Id, x => (x.Key, x.Modifiers));
        try
        {
            if (!File.Exists(ShortcutPath)) return result;
            var data = JsonSerializer.Deserialize<Dictionary<string, int[]>>(File.ReadAllText(ShortcutPath));
            if (data == null) return result;
            foreach (var pair in data)
                if (result.ContainsKey(pair.Key) && pair.Value.Length == 2)
                    result[pair.Key] = ((Key)pair.Value[0], (KeyModifiers)pair.Value[1]);
        }
        catch { /* Un archivo de preferencias dañado no debe impedir abrir el editor. */ }
        return result;
    }

    private void SaveShortcutBindings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);
        var data = _shortcutBindings.ToDictionary(x => x.Key, x => new[] { (int)x.Value.Key, (int)x.Value.Modifiers });
        var temp = ShortcutPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data));
        File.Move(temp, ShortcutPath, true);
        UpdateShortcutTooltips();
        var shelf = _shortcutBindings["shelf.capture"];
        ShelfShortcutChanged?.Invoke(shelf.Key, shelf.Modifiers);
    }

    private void UpdateShortcutTooltips()
    {
        ToolTip.SetTip(ToolVBtn, $"Selección ({ShortcutLabel(_shortcutBindings["tool.select"])})");
        ToolTip.SetTip(ToolCBtn, $"Cuchilla ({ShortcutLabel(_shortcutBindings["tool.razor"])})");
        ToolTip.SetTip(ToolABtn, $"Selección direccional ({ShortcutLabel(_shortcutBindings["tool.direction"])})");
        ToolTip.SetTip(ToolHBtn, $"Mano ({ShortcutLabel(_shortcutBindings["tool.hand"])})");
        ToolTip.SetTip(ToolZBtn, $"Zoom ({ShortcutLabel(_shortcutBindings["tool.zoom"])})");
        var maskName = _selectedMaskTool switch
        {
            "rectangle" => "Rectángulo", "ellipse" => "Elipse",
            "polygon" => "Pluma", _ => "Recorte inteligente SAM"
        };
        ToolTip.SetTip(ToolSamBtn, $"Máscara: {maskName} ({ShortcutLabel(_shortcutBindings["tool.sam"])}) · la flecha cambia de herramienta");
        ToolTip.SetTip(ToolTextBtn, $"Texto ({ShortcutLabel(_shortcutBindings["tool.text"])}) · clic o arrastre en Previsualización");
        ToolTip.SetTip(SnapToggle, $"Encaje magnético ({ShortcutLabel(_shortcutBindings["timeline.snap"])})");
    }

    private static bool IsReserved(Key key, KeyModifiers modifiers) =>
        (key == Key.Space && modifiers == KeyModifiers.None) ||
        (modifiers == KeyModifiers.Control && key is Key.N or Key.O or Key.S) ||
        (modifiers == (KeyModifiers.Control | KeyModifiers.Shift) && key == Key.S);

    private static bool ValidGlobalShelfShortcut(Key key, KeyModifiers modifiers)
    {
        var name = key.ToString();
        return name.Length == 1 && name[0] is >= 'A' and <= 'Z' &&
            modifiers.HasFlag(KeyModifiers.Control) && modifiers.HasFlag(KeyModifiers.Alt) &&
            (modifiers & ~(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift)) == 0;
    }

    private static string ShortcutLabel((Key Key, KeyModifiers Modifiers) binding)
    {
        if (binding.Key == Key.None) return "Sin asignar";
        var parts = new List<string>();
        if (binding.Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (binding.Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (binding.Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Mayús");
        parts.Add(binding.Key.ToString());
        return string.Join("+", parts);
    }

    private bool TryHandleTimelineShortcut(KeyEventArgs e)
    {
        var match = ShortcutSpecs.FirstOrDefault(s => _shortcutBindings[s.Id] == (e.Key, e.KeyModifiers));
        if (match == null) return false;
        if (match.Id == "shelf.capture") return false;
        switch (match.Id)
        {
            case "tool.select": SetTool(Prometheus.Core.Timeline.TimelineTool.Selection); break;
            case "tool.razor": SetTool(Prometheus.Core.Timeline.TimelineTool.Razor); break;
            case "tool.direction": SetTool(Prometheus.Core.Timeline.TimelineTool.SelectForward); break;
            case "tool.direction.backward": SetTool(Prometheus.Core.Timeline.TimelineTool.SelectBackward); break;
            case "tool.hand": SetTool(Prometheus.Core.Timeline.TimelineTool.Hand); break;
            case "tool.zoom": SetTool(Prometheus.Core.Timeline.TimelineTool.Zoom); break;
            case "tool.text": SetTool(TimelineTool.Text); break;
            case "tool.sam": SetTool(TimelineCanvas.ActiveTool == Prometheus.Core.Timeline.TimelineTool.Sam
                ? Prometheus.Core.Timeline.TimelineTool.Selection : Prometheus.Core.Timeline.TimelineTool.Sam); break;
            case "timeline.snap": SnapToggle.IsChecked = !SnapToggle.IsChecked; break;
            case "edit.divide": DivideAtPlayhead(); break;
            case "edit.copy": CopySelection(); break;
            case "edit.cut": CopySelection(); DeleteSelection(); break;
            case "edit.paste": PasteContextual(); break;
            case "edit.delete": case "edit.delete.back":
                if (TimelineCanvas.Selection.HasGapSelection) TimelineCanvas.RippleDeleteSelectedGap();
                else if (TimelineCanvas.Selection.SelectedClipIds.Count > 0) TimelineCanvas.DeleteSelectedClips();
                break;
            case "edit.undo": _controller?.Undo(); break;
            case "edit.redo": _controller?.Redo(); break;
            case "marker.add": AddMarkerAt(TimelineCanvas.PlayheadSeconds); break;
            case "range.in": SetRangeMark(true, TimelineCanvas.PlayheadSeconds); break;
            case "range.out": SetRangeMark(false, TimelineCanvas.PlayheadSeconds); break;
            case "marker.previous": case "marker.previous.ctrl": JumpMarker(false); break;
            case "marker.next": case "marker.next.ctrl": JumpMarker(true); break;
            case "frame.previous": _transport?.StepFrames(-_precisionFrames); break;
            case "frame.next": _transport?.StepFrames(_precisionFrames); break;
            case "frame.previous.fast": _transport?.StepFrames(-5 * _precisionFrames); break;
            case "frame.next.fast": _transport?.StepFrames(5 * _precisionFrames); break;
            case "cut.previous": JumpCut(false); break;
            case "cut.next": JumpCut(true); break;
            case "timeline.start": _transport?.GoToStart(); break;
            case "timeline.end": _transport?.GoToEnd(); break;
        }
        return true;
    }

    private void DivideAtPlayhead()
    {
        var seq = _controller?.CurrentProject.ActiveSequence;
        if (seq == null || _controller == null) return;
        var time = TimelineCanvas.PlayheadSeconds;
        var selected = TimelineCanvas.Selection.SelectedClipIds.ToHashSet();
        var anchors = seq.Clips.Where(c => (selected.Count == 0 || selected.Contains(c.Id)) &&
            c.TimelineStart.Seconds < time - 1.0 / 60 && c.EndSeconds > time + 1.0 / 60 &&
            seq.Tracks.Any(t => t.Id == c.TrackId && !t.Locked)).Select(c => c.Id).ToArray();
        if (anchors.Length == 0) return;
        _controller.UndoRedo.Commit(project =>
        {
            var active = project.ActiveSequence!;
            foreach (var id in anchors)
            {
                var clip = active.Clips.FirstOrDefault(c => c.Id == id);
                if (clip != null && clip.TimelineStart.Seconds < time - 1.0 / 60 && clip.EndSeconds > time + 1.0 / 60)
                    CollisionEngine.SplitLinkedClips(active, id, time);
            }
        }, "Dividir en el lector");
    }

    private void AttachShortcutMenu(Button button, string id)
    {
        var menu = new ContextMenu();
        var assign = new MenuItem { Header = "Asignar atajo…" };
        assign.Click += (_, _) => ShowShortcutSettings(id);
        var remove = new MenuItem { Header = "Quitar atajo" };
        remove.Click += (_, _) => { _shortcutBindings[id] = (Key.None, KeyModifiers.None); SaveShortcutBindings(); };
        var reset = new MenuItem { Header = "Restaurar predeterminado" };
        reset.Click += (_, _) =>
        {
            var spec = ShortcutSpecs.First(s => s.Id == id);
            if (ShortcutSpecs.Any(s => s.Id != id && _shortcutBindings[s.Id] == (spec.Key, spec.Modifiers)))
            { ShowShortcutSettings(id); return; }
            _shortcutBindings[id] = (spec.Key, spec.Modifiers);
            SaveShortcutBindings();
        };
        menu.ItemsSource = new[] { assign, remove, reset };
        button.ContextMenu = menu;
    }

    public void OpenShortcutSettings(Window owner) => ShowShortcutSettings(null, owner);

    private async void ShowShortcutSettings(string? focusId = null, Window? parent = null)
    {
        if (parent == null && Avalonia.VisualTree.VisualExtensions.GetVisualRoot(this) is Window visualOwner) parent = visualOwner;
        if (parent == null) return;
        var dialog = new Window { Title = "Atajos de teclado", Width = 550, Height = 560, MinWidth = 440, MinHeight = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var list = new ListBox();
        var status = new TextBlock { Text = "Selecciona una acción y pulsa Asignar.", Margin = new Thickness(8) };
        var search = new TextBox { Watermark = "Buscar acción…", Margin = new Thickness(8) };
        string[] shown = Array.Empty<string>();
        void Refresh()
        {
            var filter = search.Text ?? "";
            shown = ShortcutSpecs.Where(s => s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).Select(s => s.Id).ToArray();
            list.ItemsSource = shown.Select(id => $"{ShortcutSpecs.First(s => s.Id == id).Name}    ·    {ShortcutLabel(_shortcutBindings[id])}").ToArray();
            if (focusId != null) { list.SelectedIndex = Array.IndexOf(shown, focusId); focusId = null; }
        }
        search.TextChanged += (_, _) => Refresh();
        var assign = new Button { Content = "Asignar…" };
        var remove = new Button { Content = "Quitar" };
        var reset = new Button { Content = "Restaurar" };
        var close = new Button { Content = "Cerrar" };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8) };
        row.Children.Add(assign); row.Children.Add(remove); row.Children.Add(reset); row.Children.Add(close);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
        Grid.SetRow(search, 0); Grid.SetRow(list, 1); Grid.SetRow(status, 2); Grid.SetRow(row, 3);
        layout.Children.Add(search); layout.Children.Add(list); layout.Children.Add(status); layout.Children.Add(row);
        dialog.Content = layout;
        string? Selected() => list.SelectedIndex >= 0 && list.SelectedIndex < shown.Length ? shown[list.SelectedIndex] : null;
        bool capturing = false;
        assign.Click += (_, _) => { if (Selected() != null) { capturing = true; status.Text = "Pulsa la nueva combinación (Escape cancela)."; dialog.Focus(); } };
        dialog.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (!capturing) return;
            e.Handled = true;
            if (e.Key == Key.Escape) { capturing = false; status.Text = "Asignación cancelada."; return; }
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt) return;
            var id = Selected();
            if (id == null) return;
            if (id == "shelf.capture" && !ValidGlobalShelfShortcut(e.Key, e.KeyModifiers))
            { status.Text = "La captura global requiere Ctrl+Alt y una letra (Mayús es opcional)."; capturing = false; return; }
            if (IsReserved(e.Key, e.KeyModifiers)) { status.Text = "Esa combinación está reservada para proyecto o reproducción."; capturing = false; return; }
            var conflict = ShortcutSpecs.FirstOrDefault(s => s.Id != id && _shortcutBindings[s.Id] == (e.Key, e.KeyModifiers));
            if (conflict != null) { status.Text = $"{ShortcutLabel((e.Key, e.KeyModifiers))} ya pertenece a «{conflict.Name}». Quítalo primero."; capturing = false; return; }
            _shortcutBindings[id] = (e.Key, e.KeyModifiers);
            SaveShortcutBindings();
            capturing = false;
            status.Text = $"Asignado: {ShortcutLabel((e.Key, e.KeyModifiers))}.";
            var index = list.SelectedIndex; Refresh(); list.SelectedIndex = index;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        remove.Click += (_, _) => { var id = Selected(); if (id == null) return; _shortcutBindings[id] = (Key.None, KeyModifiers.None); SaveShortcutBindings(); var index = list.SelectedIndex; Refresh(); list.SelectedIndex = index; };
        reset.Click += (_, _) => { var id = Selected(); if (id == null) return; var spec = ShortcutSpecs.First(s => s.Id == id); var conflict = ShortcutSpecs.FirstOrDefault(s => s.Id != id && _shortcutBindings[s.Id] == (spec.Key, spec.Modifiers)); if (conflict != null) { status.Text = $"Primero quita el atajo de «{conflict.Name}»."; return; } _shortcutBindings[id] = (spec.Key, spec.Modifiers); SaveShortcutBindings(); var index = list.SelectedIndex; Refresh(); list.SelectedIndex = index; };
        close.Click += (_, _) => dialog.Close();
        Refresh();
        await dialog.ShowDialog(parent);
    }
}
