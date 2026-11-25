using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using System.Collections.Generic;

namespace Prometheus.Client.Services;

public static class SpaceShortcutPolicy
{
    public static bool ShouldToggle(Key key, KeyModifiers modifiers, object? source)
    {
        if (key != Key.Space || modifiers != KeyModifiers.None) return false;
        foreach (var control in Ancestors(source))
        {
            if (control is TextBox { IsReadOnly: false } or NumericUpDown or MenuItem) return false;
            if (control is ComboBox { IsDropDownOpen: true }) return false;
        }
        return true;
    }

    public static bool IsTextInput(object? source)
    {
        foreach (var control in Ancestors(source))
            if (control is TextBox { IsReadOnly: false } or NumericUpDown) return true;
        return false;
    }

    private static IEnumerable<Control> Ancestors(object? source)
    {
        // Routed events can originate in a template's visual child rather than its logical control.
        var visited = new HashSet<Control>();
        for (var visual = source as Avalonia.Visual; visual != null; visual = visual.GetVisualParent())
            if (visual is Control control && visited.Add(control)) yield return control;
        for (var control = source as Control; control != null; control = control.Parent as Control)
            if (visited.Add(control)) yield return control;
    }
}
