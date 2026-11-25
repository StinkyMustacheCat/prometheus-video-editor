using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Prometheus.Client.Panels;

/// <summary>Shared color editor: swatch menu, visual picker, optional hex entry and eyedropper.</summary>
public sealed class ColorField : Grid
{
    private readonly TextBox _hex;
    private readonly Button _swatch;
    private readonly ColorView _picker;
    private bool _syncing;
    private bool _allowAlpha;

    public event EventHandler? HexChanged;
    public event EventHandler? PickRequested;

    public string Hex
    {
        get => _hex.Text ?? "";
        set => SetHex(value, false);
    }

    public bool AllowAlpha
    {
        get => _allowAlpha;
        set
        {
            _allowAlpha = value;
            _picker.IsAlphaEnabled = value;
            _picker.IsAlphaVisible = value;
        }
    }

    public ColorField()
    {
        ColumnDefinitions = new ColumnDefinitions("28,*,58");
        Height = 26;
        _swatch = new Button { Width = 26, Height = 25, Padding = new Thickness(0),
            BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left };
        ToolTip.SetTip(_swatch, "Abrir menú de colores");
        Children.Add(_swatch);

        _hex = new TextBox { FontSize = 10, Height = 25, Watermark = "#RRGGBB",
            Margin = new Thickness(2, 0, 4, 0) };
        Grid.SetColumn(_hex, 1);
        Children.Add(_hex);

        var eyedropper = new Button { Content = "Gotero", FontSize = 10, Height = 25,
            Padding = new Thickness(4, 1) };
        ToolTip.SetTip(eyedropper, "Elegir un color en la previsualización; Esc o clic derecho cancela");
        Grid.SetColumn(eyedropper, 2);
        Children.Add(eyedropper);
        eyedropper.Click += (_, _) => PickRequested?.Invoke(this, EventArgs.Empty);

        _picker = new ColorView { Width = 290, MinHeight = 300,
            IsAlphaEnabled = false, IsAlphaVisible = false,
            IsColorSpectrumVisible = true, IsColorPaletteVisible = true,
            IsColorComponentsVisible = true, IsColorPreviewVisible = true,
            IsColorModelVisible = true, IsHexInputVisible = true };
        FlyoutBase.SetAttachedFlyout(_swatch, new Flyout { Content = _picker,
            Placement = PlacementMode.Bottom });
        _swatch.Click += (_, _) => FlyoutBase.ShowAttachedFlyout(_swatch);
        _picker.ColorChanged += (_, args) =>
        {
            if (_syncing) return;
            var alpha = _allowAlpha && (args.NewColor.A != 255 || Hex.Length == 9)
                ? $"{args.NewColor.A:X2}" : "";
            SetHex($"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}{alpha}", true);
        };
        _hex.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            RefreshSwatch();
            HexChanged?.Invoke(this, EventArgs.Empty);
        };
        SetHex("#FFFFFF", false);
    }

    private void SetHex(string value, bool notify)
    {
        _syncing = true;
        try
        {
            _hex.Text = value;
            RefreshSwatch();
        }
        finally { _syncing = false; }
        if (notify) HexChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshSwatch()
    {
        Color color;
        if (_allowAlpha && Hex.Length == 9 && Hex.StartsWith('#') &&
            uint.TryParse(Hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var rgba))
            color = Color.FromArgb((byte)rgba, (byte)(rgba >> 24),
                (byte)(rgba >> 16), (byte)(rgba >> 8));
        else if (!Color.TryParse(Hex, out color)) return;
        _swatch.Background = new SolidColorBrush(color);
        if (_picker.Color == color) return;
        var wasSyncing = _syncing;
        _syncing = true;
        _picker.Color = color;
        _syncing = wasSyncing;
    }
}
