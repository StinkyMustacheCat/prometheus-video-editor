using Avalonia.Controls;
using Avalonia.Input;
using Prometheus.Client.Services;
using Xunit;

namespace Prometheus.UnitTests;

public class SpaceShortcutPolicyTests
{
    [Fact]
    public void ClosedDropdownLetsSpaceControlPlayback()
    {
        var dropdown = new ComboBox();
        Assert.True(SpaceShortcutPolicy.ShouldToggle(Key.Space, KeyModifiers.None, dropdown));
        dropdown.IsDropDownOpen = true;
        Assert.False(SpaceShortcutPolicy.ShouldToggle(Key.Space, KeyModifiers.None, dropdown));
    }

    [Fact]
    public void WritingAndMenuNavigationKeepSpace()
    {
        Assert.False(SpaceShortcutPolicy.ShouldToggle(Key.Space, KeyModifiers.None, new TextBox()));
        Assert.False(SpaceShortcutPolicy.ShouldToggle(Key.Space, KeyModifiers.None, new NumericUpDown()));
        Assert.False(SpaceShortcutPolicy.ShouldToggle(Key.Space, KeyModifiers.None, new MenuItem()));
        Assert.False(SpaceShortcutPolicy.ShouldToggle(Key.Space, KeyModifiers.Control, new Button()));
    }

    [Fact]
    public void ReadOnlyTextAndOtherPanelsDoNotCapturePlayback()
    {
        Assert.True(SpaceShortcutPolicy.ShouldToggle(Key.Space, KeyModifiers.None, new TextBox { IsReadOnly = true }));
        Assert.True(SpaceShortcutPolicy.ShouldToggle(Key.Space, KeyModifiers.None, new Slider()));
        Assert.True(SpaceShortcutPolicy.ShouldToggle(Key.Space, KeyModifiers.None, new Button()));
        Assert.True(SpaceShortcutPolicy.IsTextInput(new TextBox()));
        Assert.False(SpaceShortcutPolicy.IsTextInput(new TextBox { IsReadOnly = true }));
    }
}
