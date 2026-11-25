using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Prometheus.Client.Mcp;
using Prometheus.Client.Services;

namespace Prometheus.Client.Panels;

public sealed class InstructionsPanelView : UserControl
{
    private readonly TextBlock _summary = new() { Foreground = Brush("#BDBDBD"), FontSize = 11 };
    private readonly StackPanel _cards = new() { Spacing = 5 };
    private readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxHeight = 150 };
    private readonly List<(CheckBox Select, JsonElement Action)> _items = new();
    private Func<JsonElement, System.Threading.Tasks.Task<object>>? _evaluate;
    private Func<JsonElement, object>? _apply;

    public InstructionsPanelView()
    {
        var root = new Grid { Background = Brush("#000000"),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto") };
        var title = new TextBlock { Text = "Instrucciones", FontWeight = FontWeight.SemiBold,
            FontSize = 13, Foreground = Brush("#FF9400"), Margin = new Thickness(10, 10, 10, 5) };
        root.Children.Add(title);
        _summary.Text = "Las instrucciones enviadas en modo review aparecerán aquí.";
        _summary.Margin = new Thickness(10, 0, 10, 9);
        Grid.SetRow(_summary, 1); root.Children.Add(_summary);
        var scroll = new ScrollViewer { Content = _cards, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Margin = new Thickness(8, 0, 8, 4) };
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
            Margin = new Thickness(8, 4, 8, 7) };
        var evaluate = new Button { Content = "Evaluar selección", Padding = new Thickness(9, 5) };
        evaluate.Click += (_, _) => EvaluateSelected();
        var apply = new Button { Content = "Aplicar selección", Padding = new Thickness(9, 5),
            Background = Brush("#9C5200") };
        apply.Click += (_, _) => ApplySelected();
        buttons.Children.Add(evaluate); buttons.Children.Add(apply);
        Grid.SetRow(buttons, 3); root.Children.Add(buttons);
        _log.Margin = new Thickness(8, 0, 8, 8);
        Grid.SetRow(_log, 4); root.Children.Add(_log);
        Content = root;
    }

    public void Configure(Func<JsonElement, System.Threading.Tasks.Task<object>> evaluate,
        Func<JsonElement, object> apply)
    {
        _evaluate = evaluate; _apply = apply;
    }

    public void ShowReview(JsonElement args, object evaluation)
    {
        _items.Clear(); _cards.Children.Clear();
        if (!args.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array) return;
        var index = 0;
        foreach (var action in actions.EnumerateArray())
        {
            index++;
            var name = McpJson.String(action, "op") ?? "acción";
            var check = new CheckBox { IsChecked = true, Content = $"{index}. {name}",
                FontSize = 11, Foreground = Brush("#E9E9E9") };
            var border = new Border { Background = Brush("#151515"), BorderBrush = Brush("#3A3A3A"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 7), Child = check };
            _cards.Children.Add(border);
            _items.Add((check, action.Clone()));
        }
        var result = JsonSerializer.SerializeToElement(evaluation);
        var valid = result.TryGetProperty("valid", out var v) && v.GetBoolean();
        _summary.Text = valid
            ? $"{_items.Count} acciones evaluadas · ninguna aplicada todavía"
            : "La evaluación encontró un conflicto; revisa la bitácora.";
        AppendLog(valid ? "Lote recibido para revisión." : result.GetRawText());
    }

    public void AppendLog(string text)
    {
        var lines = (_log.Text ?? "").Split('\n').TakeLast(35).ToList();
        lines.Add($"{DateTime.Now:HH:mm:ss}  {text}");
        _log.Text = string.Join("\n", lines).TrimStart('\n');
        _log.CaretIndex = _log.Text.Length;
    }

    private JsonElement SelectedRequest()
    {
        var selected = _items.Where(x => x.Select.IsChecked == true).Select(x => x.Action).ToArray();
        if (selected.Length == 0) throw new InvalidOperationException("Selecciona al menos una acción.");
        var revision = ProjectController.Instance?.CurrentProject.Revision
            ?? throw new InvalidOperationException("No hay proyecto abierto.");
        return JsonSerializer.SerializeToElement(new { actions = selected, expected_revision = revision });
    }

    private async void EvaluateSelected()
    {
        try
        {
            var result = JsonSerializer.SerializeToElement(await _evaluate!(SelectedRequest()));
            var valid = result.GetProperty("valid").GetBoolean();
            _summary.Text = valid ? "Selección válida y lista para aplicar." : "Selección inválida.";
            AppendLog(valid ? "Evaluación correcta; el proyecto sigue intacto." : result.GetRawText());
        }
        catch (Exception ex) { AppendLog("Error: " + ex.Message); }
    }

    private async void ApplySelected()
    {
        try
        {
            var evaluated = JsonSerializer.SerializeToElement(await _evaluate!(SelectedRequest()));
            if (!evaluated.GetProperty("valid").GetBoolean())
            {
                AppendLog("No se aplicó: " + evaluated.GetRawText()); return;
            }
            var result = JsonSerializer.SerializeToElement(_apply!(JsonSerializer.SerializeToElement(new
            { plan_id = evaluated.GetProperty("plan_id").GetString(),
              expected_revision = evaluated.GetProperty("base_revision").GetInt32() })));
            _summary.Text = "Lote aplicado. Puedes deshacerlo en un paso.";
            AppendLog(result.GetRawText());
        }
        catch (Exception ex) { AppendLog("Error: " + ex.Message); }
    }

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}
