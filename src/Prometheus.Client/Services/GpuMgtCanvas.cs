using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct2D1.D2D1;

namespace Prometheus.Client.Services;

/// <summary>Executes MGT canvas commands directly on the compositor's D3D11 device.</summary>
internal sealed class GpuMgtCanvas : IDisposable
{
    private readonly ID2D1Factory1 _factory;
    private readonly ID2D1Device _device;
    private readonly ID2D1DeviceContext6 _canvas;
    private readonly ID2D1Bitmap1 _target;
    private readonly ID2D1Bitmap1 _scratch;
    private readonly ID3D11Texture2D _output;
    private readonly ID2D1SolidColorBrush _brush;
    private readonly int _width;
    private readonly int _height;
    private readonly Dictionary<(string Id, int Version, string Render), MgtGpuCanvasProgram> _programs = new();
    public ID3D11ShaderResourceView OutputView { get; }

    public GpuMgtCanvas(ID3D11Device device, int width, int height)
    {
        _width = width;
        _height = height;
        _factory = D2D1CreateFactory<ID2D1Factory1>();
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        _device = _factory.CreateDevice(dxgi);
        using var context = _device.CreateDeviceContext(DeviceContextOptions.None);
        _canvas = context.QueryInterface<ID2D1DeviceContext6>();
        _brush = _canvas.CreateSolidColorBrush(new Color4(0, 0, 0, 1));
        _output = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
            (uint)width, (uint)height, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource));
        OutputView = device.CreateShaderResourceView(_output);
        _target = Wrap(_output, BitmapOptions.Target);
        _scratch = _canvas.CreateBitmap(new SizeI(width, height), IntPtr.Zero, 0, BitmapProps(BitmapOptions.Target));
    }

    private static BitmapProperties1 BitmapProps(BitmapOptions options) =>
        new(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            96, 96, options);

    private ID2D1Bitmap1 Wrap(ID3D11Texture2D texture, BitmapOptions options)
    {
        using var dxgi = texture.QueryInterface<IDXGISurface>();
        return _canvas.CreateBitmapFromDxgiSurface(dxgi, BitmapProps(options));
    }

    private sealed record PaintState(Matrix3x2 Transform, float Alpha, string Blend,
        JsonElement Fill, JsonElement Stroke, float LineWidth, int Clips, float Shadow, JsonElement ShadowColor,
        Vector2 ShadowOffset);

    private sealed class Figure
    {
        public List<Vector2> Points { get; } = [];
        public bool Closed { get; set; }
    }

    public void Draw(VideoTransitionFrame frame, ID3D11Texture2D left, ID3D11Texture2D right,
        int designWidth, int designHeight)
    {
        var transition = frame.Window.Transition;
        var definition = transition.Mgt!;
        var key = (definition.Id, definition.Version, definition.Render);
        if (!_programs.TryGetValue(key, out var program))
        {
            if (_programs.Count >= 4) _programs.Remove(_programs.Keys.First());
            _programs[key] = program = new MgtGpuCanvasProgram(definition);
        }
        var window = frame.Window;
        var index = (int)Math.Round((frame.Time - window.Cut) * window.Fps) + window.BeforeFrames;
        using var commands = program.Run(designWidth, designHeight, window.Fps, transition.DurationFrames, index, transition.MgtOverrides);
        using var a = Wrap(left, BitmapOptions.None);
        using var b = Wrap(right, BitmapOptions.None);
        var state = new PaintState(Matrix3x2.CreateScale((float)_width / designWidth, (float)_height / designHeight),
            1, "source-over", default, default, 1, 0, 0, default, default);
        var states = new Stack<PaintState>();
        var clips = new List<ID2D1PathGeometry>();
        var path = new List<Figure>();
        _canvas.Target = _target;
        _canvas.BeginDraw();
        try
        {
            _canvas.Transform = Matrix3x2.Identity;
            _canvas.Clear(new Color4(0, 0, 0, 0));
            foreach (var command in commands.RootElement.EnumerateArray()) Execute(command);
        }
        finally
        {
            foreach (var clip in clips) clip.Dispose();
            _canvas.Target = _target;
            try { _canvas.EndDraw().CheckError(); }
            finally { _canvas.Target = null; }
        }

        void Execute(JsonElement c)
        {
            var op = c[0].GetString();
            float N(int i) => c[i].GetSingle();
            Vector2 Point(float x, float y) => Vector2.Transform(new Vector2(x, y), state.Transform);
            switch (op)
            {
                case "save": states.Push(state); return;
                case "restore":
                    if (!states.TryPop(out var saved)) return;
                    while (clips.Count > saved.Clips)
                    {
                        clips[^1].Dispose();
                        clips.RemoveAt(clips.Count - 1);
                    }
                    state = saved;
                    return;
                case "translate": state = state with { Transform = Matrix3x2.CreateTranslation(N(1), N(2)) * state.Transform }; return;
                case "rotate": state = state with { Transform = Matrix3x2.CreateRotation(N(1)) * state.Transform }; return;
                case "scale": state = state with { Transform = Matrix3x2.CreateScale(N(1), N(2)) * state.Transform }; return;
                case "transform":
                    state = state with { Transform = new Matrix3x2(N(1), N(2), N(3), N(4), N(5), N(6)) * state.Transform };
                    return;
                case "globalAlpha": state = state with { Alpha = Math.Clamp(N(1), 0, 1) }; return;
                case "globalCompositeOperation": state = state with { Blend = c[1].GetString()! }; return;
                case "fillStyle": state = state with { Fill = c[1] }; return;
                case "strokeStyle": state = state with { Stroke = c[1] }; return;
                case "lineWidth": state = state with { LineWidth = Math.Max(0.01f, N(1)) }; return;
                case "shadowBlur": state = state with { Shadow = Math.Clamp(N(1), 0, 250) }; return;
                case "shadowColor": state = state with { ShadowColor = c[1] }; return;
                case "shadowOffsetX": state = state with { ShadowOffset = new Vector2(N(1), state.ShadowOffset.Y) }; return;
                case "shadowOffsetY": state = state with { ShadowOffset = new Vector2(state.ShadowOffset.X, N(1)) }; return;
                case "beginPath": path.Clear(); return;
                case "closePath": if (path.Count > 0) path[^1].Closed = true; return;
                case "moveTo": AddPoint(Point(N(1), N(2)), true); return;
                case "lineTo": AddPoint(Point(N(1), N(2))); return;
                case "rect":
                    AddPoint(Point(N(1), N(2)), true);
                    AddPoint(Point(N(1) + N(3), N(2)));
                    AddPoint(Point(N(1) + N(3), N(2) + N(4)));
                    AddPoint(Point(N(1), N(2) + N(4)));
                    path[^1].Closed = true;
                    return;
                case "arc":
                    var first = N(4);
                    var radius = Math.Max(0, N(3));
                    var reverse = c.GetArrayLength() > 6 && c[6].ValueKind == JsonValueKind.True;
                    var sweep = N(5) - first;
                    var circle = MathF.Tau;
                    sweep = Math.Abs(sweep) >= circle - 0.000001f ? (reverse ? -circle : circle) :
                        reverse ? -((-sweep % circle + circle) % circle) : (sweep % circle + circle) % circle;
                    var segments = Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 72)), 1, 144);
                    for (var i = 0; i <= segments; i++)
                    {
                        var angle = first + sweep * i / segments;
                        AddPoint(Point(N(1) + radius * MathF.Cos(angle), N(2) + radius * MathF.Sin(angle)));
                    }
                    return;
                case "clip":
                    if (clips.Count >= 24) throw new InvalidOperationException("Demasiadas máscaras MGT.");
                    clips.Add(Geometry(path, true));
                    state = state with { Clips = clips.Count };
                    return;
                case "clearRect":
                    if (N(1) == 0 && N(2) == 0 && N(3) == designWidth && N(4) == designHeight &&
                        clips.Count == 0 && states.Count == 0)
                        _canvas.Clear(new Color4(0, 0, 0, 0));
                    else
                        DrawClipped(() => {
                            _canvas.PrimitiveBlend = PrimitiveBlend.Copy;
                            _brush.Color = new Color4(0, 0, 0, 0);
                            _canvas.FillRectangle(Rect(N(1), N(2), N(3), N(4)), _brush);
                            _canvas.PrimitiveBlend = PrimitiveBlend.SourceOver;
                        });
                    return;
                case "fillRect":
                    var rectangle = Rect(N(1), N(2), N(3), N(4));
                    WithShadow(() => WithBrush(state.Fill, brush => _canvas.FillRectangle(rectangle, brush)));
                    return;
                case "image":
                    var source = N(1) == 0 ? a : b;
                    var count = c.GetArrayLength();
                    var src = count == 10 ? Rect(N(2), N(3), N(4), N(5)) : Rect(0, 0, designWidth, designHeight);
                    var dst = count == 10 ? Rect(N(6), N(7), N(8), N(9)) :
                        Rect(N(2), N(3), count == 6 ? N(4) : designWidth, count == 6 ? N(5) : designHeight);
                    if (!CropImage(ref src, ref dst, designWidth, designHeight)) return;
                    src = new RawRectF(src.Left * _width / designWidth, src.Top * _height / designHeight,
                        src.Right * _width / designWidth, src.Bottom * _height / designHeight);
                    WithShadow(() => _canvas.DrawBitmap(source, dst, state.Alpha, InterpolationMode.Linear, src, null));
                    return;
                case "stroke":
                case "fill":
                    var stroke = op == "stroke";
                    using (var geometry = Geometry(path, !stroke))
                    {
                        Composite(() =>
                        {
                            _canvas.Transform = Matrix3x2.Identity;
                            WithBrush(stroke ? state.Stroke : state.Fill, brush =>
                            {
                                if (stroke) _canvas.DrawGeometry(geometry, brush,
                                    state.LineWidth * new Vector2(state.Transform.M11, state.Transform.M12).Length());
                                else _canvas.FillGeometry(geometry, brush);
                            });
                        });
                    }
                    return;
                default: throw new InvalidOperationException("Operación de dibujo MGT desconocida.");
            }
        }

        void AddPoint(Vector2 point, bool newFigure = false)
        {
            if (newFigure || path.Count == 0) path.Add(new Figure());
            path[^1].Points.Add(point);
        }

        void DrawClipped(Action draw)
        {
            _canvas.Transform = Matrix3x2.Identity;
            foreach (var clip in clips)
            {
                var layer = new LayerParameters1
                {
                    ContentBounds = new RawRectF(0, 0, _width, _height), GeometricMask = clip,
                    MaskAntialiasMode = AntialiasMode.PerPrimitive, MaskTransform = Matrix3x2.Identity, Opacity = 1
                };
                _canvas.PushLayer(ref layer, null);
            }
            _canvas.Transform = state.Transform;
            try { draw(); }
            finally { foreach (var _ in clips) _canvas.PopLayer(); }
        }

        void Composite(Action draw)
        {
            if (state.Alpha <= 0) return;
            if (state.Blend is "source-over" or "lighter")
            {
                _canvas.PrimitiveBlend = state.Blend == "lighter" ? PrimitiveBlend.Add : PrimitiveBlend.SourceOver;
                try { DrawClipped(draw); }
                finally { _canvas.PrimitiveBlend = PrimitiveBlend.SourceOver; }
                return;
            }

            _canvas.Target = _scratch;
            _canvas.Clear(new Color4(0, 0, 0, 0));
            DrawClipped(draw);
            _canvas.Target = _target;
            _canvas.Transform = Matrix3x2.Identity;
            var mode = state.Blend == "screen" ? BlendMode.Screen :
                state.Blend == "multiply" ? BlendMode.Multiply : BlendMode.Difference;
            _canvas.BlendImage(_scratch, mode, null, null, InterpolationMode.NearestNeighbor);
        }

        void WithShadow(Action draw)
        {
            if (state.Shadow > 0 && state.ShadowColor.ValueKind == JsonValueKind.String)
            {
                var color = Color(state.ShadowColor.GetString(), 1);
                if (color.A > 0)
                {
                    _canvas.Target = _scratch;
                    _canvas.Clear(new Color4(0, 0, 0, 0));
                    _canvas.Transform = state.Transform;
                    draw();
                    _canvas.Target = _target;
                    using var shadow = new ID2D1Effect(_canvas.CreateEffect(EffectGuids.Shadow));
                    shadow.SetValue((uint)ShadowProperties.BlurStandardDeviation, state.Shadow * _width / designWidth / 2);
                    shadow.SetValue((uint)ShadowProperties.Color, new Vector4(color.R, color.G, color.B, color.A));
                    shadow.SetInput(0, _scratch, true);
                    using var output = shadow.Output;
                    DrawClipped(() =>
                    {
                        _canvas.Transform = Matrix3x2.CreateTranslation(
                            state.ShadowOffset.X * _width / designWidth, state.ShadowOffset.Y * _height / designHeight);
                        _canvas.DrawImage(output, Vector2.Zero, new RawRectF(0, 0, _width, _height),
                            InterpolationMode.Linear, CompositeMode.SourceOver);
                    });
                }
            }
            Composite(draw);
        }

        void WithBrush(JsonElement style, Action<ID2D1Brush> draw)
        {
            if (style.ValueKind != JsonValueKind.Array)
            {
                _brush.Color = Color(style.ValueKind == JsonValueKind.String ? style.GetString() : "#000", state.Alpha);
                draw(_brush);
                return;
            }
            var coordinates = style[1];
            var radial = style[0].GetInt32() == 2;
            float At(int i) => coordinates[i].GetSingle();
            var inner = radial && At(5) > 0 ? Math.Clamp(At(2) / At(5), 0, 1) : 0;
            var stops = style[2].EnumerateArray().Select(stop =>
                new GradientStop(inner + stop[0].GetSingle() * (1 - inner), Color(stop[1].GetString(), state.Alpha)))
                .OrderBy(stop => stop.Position).ToArray();
            if (stops.Length == 0) return;
            using var collection = _canvas.CreateGradientStopCollection(stops);
            using ID2D1Brush gradient = radial
                ? _canvas.CreateRadialGradientBrush(new RadialGradientBrushProperties(
                    new Vector2(At(3), At(4)), new Vector2(At(0) - At(3), At(1) - At(4)), At(5), At(5)), collection)
                : _canvas.CreateLinearGradientBrush(new LinearGradientBrushProperties(
                    new Vector2(At(0), At(1)), new Vector2(At(2), At(3))), collection);
            draw(gradient);
        }
    }

    private ID2D1PathGeometry Geometry(List<Figure> path, bool fill)
    {
        var geometry = _factory.CreatePathGeometry();
        try
        {
            using var sink = geometry.Open();
            sink.SetFillMode(Vortice.Direct2D1.FillMode.Winding);
            foreach (var figure in path.Where(f => f.Points.Count > 0))
            {
                sink.BeginFigure(figure.Points[0], fill ? FigureBegin.Filled : FigureBegin.Hollow);
                if (figure.Points.Count > 1) sink.AddLines(figure.Points.Skip(1).ToArray());
                sink.EndFigure(figure.Closed || fill ? FigureEnd.Closed : FigureEnd.Open);
            }
            sink.Close();
            return geometry;
        }
        catch { geometry.Dispose(); throw; }
    }

    private static RawRectF Rect(float x, float y, float w, float h) =>
        new(Math.Min(x, x + w), Math.Min(y, y + h), Math.Max(x, x + w), Math.Max(y, y + h));

    private static bool CropImage(ref RawRectF src, ref RawRectF dest, int width, int height)
    {
        var sw = src.Right - src.Left;
        var sh = src.Bottom - src.Top;
        if (sw <= 0 || sh <= 0) return false;
        var a = Math.Max(0, src.Left);
        var b = Math.Max(0, src.Top);
        var c = Math.Min(width, src.Right);
        var d = Math.Min(height, src.Bottom);
        if (c <= a || d <= b) return false;
        var fx = (dest.Right - dest.Left) / sw;
        var fy = (dest.Bottom - dest.Top) / sh;
        dest = new RawRectF(dest.Left + (a - src.Left) * fx, dest.Top + (b - src.Top) * fy,
            dest.Right - (src.Right - c) * fx, dest.Bottom - (src.Bottom - d) * fy);
        src = new RawRectF(a, b, c, d);
        return true;
    }

    private static Color4 Color(string? text, float opacity)
    {
        if (text is null or "transparent") return new Color4(0, 0, 0, 0);
        if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) && text.IndexOf('(') is >= 0 and var open)
        {
            var values = text[(open + 1)..].TrimEnd(')').Split(',');
            if (values.Length is 3 or 4)
            {
                float Parse(int i) => float.Parse(values[i], CultureInfo.InvariantCulture);
                return new Color4(Math.Clamp(Parse(0) / 255, 0, 1), Math.Clamp(Parse(1) / 255, 0, 1),
                    Math.Clamp(Parse(2) / 255, 0, 1), opacity * (values.Length == 4 ? Math.Clamp(Parse(3), 0, 1) : 1));
            }
        }
        if (text.StartsWith('#') && text.Length == 9)
            text = "#" + text[^2..] + text[1..^2]; // CSS #RRGGBBAA -> Avalonia #AARRGGBB
        var color = Avalonia.Media.Color.Parse(text);
        return new Color4(color.R / 255f, color.G / 255f, color.B / 255f, opacity * color.A / 255f);
    }

    public void Dispose()
    {
        _brush.Dispose();
        _scratch.Dispose();
        _target.Dispose();
        OutputView.Dispose();
        _output.Dispose();
        _canvas.Dispose();
        _device.Dispose();
        _factory.Dispose();
    }
}
