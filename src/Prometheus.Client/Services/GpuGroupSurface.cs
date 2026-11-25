using Prometheus.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;
using SkiaSharp;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;

namespace Prometheus.Client.Services;

/// <summary>Composes nested clip groups entirely in D3D11; readback occurs only for explicit PNG capture.</summary>
public sealed class GpuGroupSurface : IDisposable
{
    private const string PlacementShader = GpuBlendShader.Functions + """
        Texture2D sourceTexture : register(t0);
        Texture2D backdropTexture : register(t1);
        SamplerState sourceSampler : register(s0);
        cbuffer Placement : register(b0) {
            float4 rect;
            float opacity; float flipX; float flipY; float cosine;
            float2 pivot; float sine; float blendMode;
        };
        struct VertexOut { float4 position : SV_Position; float2 uv : TEXCOORD0; };
        VertexOut VSMain(uint id : SV_VertexID) {
            VertexOut o;
            o.uv = float2((id << 1) & 2, id & 2);
            o.position = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }
        float4 PSMain(VertexOut input) : SV_Target {
            float2 delta = input.position.xy - pivot;
            float2 unrotated = float2(delta.x * cosine + delta.y * sine,
                -delta.x * sine + delta.y * cosine) + pivot;
            float2 uv = (unrotated - rect.xy) / rect.zw;
            if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) discard;
            if (flipX > 0.5) uv.x = 1.0 - uv.x;
            if (flipY > 0.5) uv.y = 1.0 - uv.y;
            float4 source = sourceTexture.Sample(sourceSampler, uv) * opacity;
            if (blendMode < .5) return source;
            float4 backdrop = backdropTexture.Load(int3(int2(input.position.xy),0));
            return BlendOver(backdrop, source, (int)blendMode);
        }
        """;

    private const string TransitionShader = """
        Texture2D leftImage : register(t0);
        Texture2D rightImage : register(t1);
        SamplerState imageSampler : register(s0);
        cbuffer Transition : register(b0) { float4 weights; }
        float4 PSMain(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target {
            return leftImage.Sample(imageSampler, uv) * weights.x
                + rightImage.Sample(imageSampler, uv) * weights.y
                + float4(weights.z, weights.z, weights.z, 1) * weights.w;
        }
        """;
    private static readonly Lazy<byte[]> TransitionBytecode = new(() =>
        Compiler.Compile(TransitionShader, "PSMain", "VideoTransition", "ps_5_0",
            ShaderFlags.None, EffectFlags.None).ToArray());

    private sealed class SharedInput : IDisposable
    {
        public required ID3D11Texture2D Texture { get; init; }
        public required ID3D11ShaderResourceView View { get; init; }
        public required IDXGIKeyedMutex Mutex { get; init; }
        public void Dispose() { Mutex.Dispose(); View.Dispose(); Texture.Dispose(); }
    }

    private sealed class GroupTargets : IDisposable
    {
        public required ID3D11Texture2D Raw { get; init; }
        public required ID3D11RenderTargetView RawTarget { get; init; }
        public required ID3D11ShaderResourceView RawView { get; init; }
        public required ID3D11Texture2D Processed { get; init; }
        public required ID3D11RenderTargetView ProcessedTarget { get; init; }
        public required ID3D11ShaderResourceView ProcessedView { get; init; }
        public void Dispose()
        {
            RawView.Dispose(); RawTarget.Dispose(); Raw.Dispose();
            ProcessedView.Dispose(); ProcessedTarget.Dispose(); Processed.Dispose();
        }
    }

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Texture2D _output;
    private readonly ID3D11RenderTargetView _outputTarget;
    private readonly IDXGIResource _resource;
    private readonly IDXGIKeyedMutex _mutex;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _placeShader;
    private readonly ID3D11PixelShader _effectShader;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11Buffer _placeBuffer;
    private readonly ID3D11Buffer _effectBuffer;
    private readonly ID3D11BlendState _blend;
    private readonly ID3D11BlendState _opaque;
    private ID3D11PixelShader? _transitionShader;
    private ID3D11Texture2D? _blendBackdrop;
    private ID3D11ShaderResourceView? _blendBackdropView;
    private ID3D11Buffer? _transitionBuffer;
    private GpuMgtCanvas? _mgtTransitions;
    public int TransitionDesignWidth { get; set; }
    public int TransitionDesignHeight { get; set; }
    private GpuBlurPass? _blurPass;
    private GpuBlurPass? _glowPass;
    private readonly Dictionary<string, GpuLutTexture> _luts = new(StringComparer.OrdinalIgnoreCase);
    public Func<Clip, double, IReadOnlyDictionary<int, IReadOnlyList<GpuMaskLayerFrame>>>? MasksForClip { get; set; }
    private readonly Dictionary<int, GpuMaskStackPass> _maskPasses = new();
    public Func<Clip, string?>? LutPathForClip { get; set; }
    public Func<Clip, ClipVisualState?>? VisualOverrideForClip { get; set; }
    private readonly Dictionary<IntPtr, SharedInput> _inputs = new();
    private readonly Dictionary<string, GroupTargets> _groups = new();
    private ICompositionImportedGpuImage? _imported;
    private CompositionDrawingSurface? _drawingSurface;
    private bool _disposed;
    private readonly object _gpuGate = new();
    private Task _pendingPresentation = Task.CompletedTask;
    private bool _hasFrame;

    public int Width { get; }
    public int Height { get; }
    public IntPtr SharedHandle => _resource.SharedHandle;

    /// <summary>Hands the composed texture to a GPU consumer while holding its keyed mutex.</summary>
    public void WithOutputTexture(Action<ID3D11Device, ID3D11DeviceContext, ID3D11Texture2D> consume)
    {
        lock (_gpuGate)
        {
            if (_disposed || !_hasFrame) throw new InvalidOperationException("El compositor no tiene fotograma.");
            GpuTextureSync.Acquire(_mutex!, 0);
            try { consume(_device, _context, _output); }
            finally { _mutex.ReleaseSync(0); }

        }
    }

    public GpuGroupSurface(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out _device, out _, out _context).CheckError();
        using var multithread = _device.QueryInterface<ID3D11Multithread>();
        multithread.SetMultithreadProtected(true);
        _output = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
            (uint)Width, (uint)Height, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource,
            miscFlags: ResourceOptionFlags.SharedKeyedMutex));
        _outputTarget = _device.CreateRenderTargetView(_output);
        _resource = _output.QueryInterface<IDXGIResource>();
        _mutex = _output.QueryInterface<IDXGIKeyedMutex>();
        var vertex = Compiler.Compile(PlacementShader, "VSMain", "GroupPlacement", "vs_5_0",
            ShaderFlags.None, EffectFlags.None).ToArray();
        var pixel = Compiler.Compile(PlacementShader, "PSMain", "GroupPlacement", "ps_5_0",
            ShaderFlags.None, EffectFlags.None).ToArray();
        _vertexShader = _device.CreateVertexShader(vertex);
        _placeShader = _device.CreatePixelShader(pixel);
        _effectShader = _device.CreatePixelShader(GpuVideoSurface.EffectBytecode.Pixel);
        _sampler = _device.CreateSamplerState(SamplerDescription.LinearClamp);
        _placeBuffer = _device.CreateBuffer(48, BindFlags.ConstantBuffer);
        _effectBuffer = _device.CreateBuffer(352, BindFlags.ConstantBuffer);
        _blend = _device.CreateBlendState(BlendDescription.AlphaBlend);
        _opaque = _device.CreateBlendState(BlendDescription.Opaque);
    }

    public async Task AttachAsync(Compositor compositor, CompositionDrawingSurface drawingSurface)
    {
        var interop = await compositor.TryGetCompositionGpuInterop()
            ?? throw new NotSupportedException("El compositor no permite texturas D3D11 compartidas.");
        if (!interop.SupportedImageHandleTypes.Contains(KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle))
            throw new NotSupportedException("El compositor no acepta texturas D3D11 compartidas.");
        lock (_gpuGate)
        {
            if (_disposed) return;
            _imported = interop.ImportImage(
                new PlatformHandle(SharedHandle, KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle),
                new PlatformGraphicsExternalImageProperties
                {
                    Width = Width,
                    Height = Height,
                    Format = PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm
                });
            _drawingSurface = drawingSurface;
        }
    }

    public async Task RenderAsync(IReadOnlyList<NestedRenderNode> nodes,
        Func<string, GpuFrameReference?> sourceForClip)
    {
        Task presentation;
        lock (_gpuGate)
        {
            if (_disposed || _imported == null || _drawingSurface == null) return;
            Render(nodes, sourceForClip, 1);
            presentation = _pendingPresentation = _drawingSurface.UpdateWithKeyedMutexAsync(_imported, 1, 0);
        }
        await presentation.WaitAsync(TimeSpan.FromSeconds(3));
    }

    /// <summary>Renders without Avalonia presentation for native pipeline verification.</summary>
    public void RenderOffscreen(IReadOnlyList<NestedRenderNode> nodes,
        Func<string, GpuFrameReference?> sourceForClip) => Render(nodes, sourceForClip, 0);

    private void Render(IReadOnlyList<NestedRenderNode> nodes,
        Func<string, GpuFrameReference?> sourceForClip, ulong releaseKey)
    {
        lock (_gpuGate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GpuGroupSurface));
            var usedInputs = new HashSet<IntPtr>();
            var usedGroups = new HashSet<string>(StringComparer.Ordinal);
            GpuTextureSync.Acquire(_mutex!, 0);
            try
            {
                // Conservar el canal alfa para capturas PNG; el monitor aporta su fondo negro.
                _context.ClearRenderTargetView(_outputTarget, new Color4(0, 0, 0, 0));
                foreach (var node in nodes) DrawNode(node, _outputTarget, sourceForClip, usedInputs, usedGroups, "root");
                _context.Flush();
            }
            finally { _mutex!.ReleaseSync(releaseKey); }
            _hasFrame = true;
            foreach (var key in _inputs.Keys.Where(key => !usedInputs.Contains(key)).ToArray())
            { _inputs[key].Dispose(); _inputs.Remove(key); }
            foreach (var key in _groups.Keys.Where(key => !usedGroups.Contains(key)).ToArray())
            { _groups[key].Dispose(); _groups.Remove(key); }

        }
    }

    private void DrawNode(NestedRenderNode node, ID3D11RenderTargetView target,
        Func<string, GpuFrameReference?> sourceForClip, HashSet<IntPtr> usedInputs, HashSet<string> usedGroups, string parentPath)
    {
        var groupKey = parentPath + "/" + node.Clip.Id.Length + ":" + node.Clip.Id;
        if (node.Transition is { } transition)
        {
            DrawTransition(node, transition, target, sourceForClip, usedInputs, usedGroups, groupKey);
            return;
        }
        if (node.Clip.Id.StartsWith("track-fx:", StringComparison.Ordinal) && node.Children.Count == 1 &&
            node.Children[0].Transition is { } trackTransition &&
            node.Children[0].Children.Select(c => c.BlendModeOverride ?? c.Clip.Visual.BlendMode).Distinct().Count() > 1)
        {
            DrawTransition(node.Children[0], trackTransition, target, sourceForClip, usedInputs, usedGroups,
                groupKey, node.Clip);
            return;
        }
        var state = VisualOverrideForClip?.Invoke(node.Clip) ??
            DynamicMotionEffect.Evaluate(node.Clip, node.LocalTimelineSeconds);
        if (!node.IsGroup)
        {
            var source = sourceForClip(node.Clip.Id);
            if (source == null || source.Value.SharedTextureHandle == IntPtr.Zero) return;
            var frame = source.Value;
            var input = GetInput(frame.SharedTextureHandle);
            usedInputs.Add(frame.SharedTextureHandle);
            GpuTextureSync.Acquire(input.Mutex!, 0);
            try { DrawPlaced(input.View, frame.Width, frame.Height, target, state, node.BlendModeOverride); }
            finally { input.Mutex.ReleaseSync(0); }
            return;
        }
        usedGroups.Add(groupKey);
        var group = GetGroup(groupKey);
        _context.ClearRenderTargetView(group.RawTarget, new Color4(0, 0, 0, 0));
        foreach (var child in node.Children) DrawNode(child, group.RawTarget, sourceForClip, usedInputs, usedGroups, groupKey);
        DrawEffect(group.RawView, group.ProcessedTarget,
            KeyframeAnimation.EvaluateEffects(node.Clip, node.LocalTimelineSeconds),
            MasksForClip?.Invoke(node.Clip, node.LocalTimelineSeconds));
        DrawPlaced(group.ProcessedView, Width, Height, target, state, node.BlendModeOverride);
    }

    private void DrawTransition(NestedRenderNode node, VideoTransitionFrame transition, ID3D11RenderTargetView target,
        Func<string, GpuFrameReference?> sourceForClip, HashSet<IntPtr> usedInputs, HashSet<string> usedGroups, string groupKey,
        Clip? endpointEffects = null)
    {
        usedGroups.Add(groupKey);
        var group = GetGroup(groupKey);
        _context.ClearRenderTargetView(group.RawTarget, new Color4(0, 0, 0, 0));
        _context.ClearRenderTargetView(group.ProcessedTarget, new Color4(0, 0, 0, 0));
        var forceNormal = node.BlendModeOverride == "normal";
        var blendInputs = endpointEffects != null || !forceNormal && node.Children.Any(child =>
            ClipBlendModes.GpuId(child.BlendModeOverride ?? child.Clip.Visual.BlendMode) != 0);
        if (blendInputs && endpointEffects == null)
        {
            // Fuse each endpoint with the same underlying image, then transition
            // the complete frames. Replacing the target avoids counting its alpha twice.
            using var backdrop = target.Resource;
            _context.CopyResource(group.Raw, backdrop);
            _context.CopyResource(group.Processed, backdrop);
        }
        foreach (var child in node.Children)
        {
            var inputTarget = child.Clip.Id == transition.Window.Left.Id ? group.RawTarget : group.ProcessedTarget;
            DrawNode(forceNormal || endpointEffects != null ? child with { BlendModeOverride = "normal" } : child,
                inputTarget, sourceForClip, usedInputs, usedGroups, groupKey);
        }
        if (endpointEffects != null)
        {
            var effectKey = groupKey + "/endpoint-effects";
            usedGroups.Add(effectKey);
            var graded = GetGroup(effectKey);
            var evaluated = KeyframeAnimation.EvaluateEffects(endpointEffects, node.LocalTimelineSeconds);
            var masks = MasksForClip?.Invoke(endpointEffects, node.LocalTimelineSeconds);
            DrawEffect(group.RawView, graded.RawTarget, evaluated, masks);
            DrawEffect(group.ProcessedView, graded.ProcessedTarget, evaluated, masks);
            using var backdrop = target.Resource;
            _context.CopyResource(group.Raw, backdrop);
            _context.CopyResource(group.Processed, backdrop);
            foreach (var child in node.Children)
            {
                var left = child.Clip.Id == transition.Window.Left.Id;
                DrawPlaced(left ? graded.RawView : graded.ProcessedView, Width, Height,
                    left ? group.RawTarget : group.ProcessedTarget,
                    new ClipVisualState { BlendMode = child.BlendModeOverride ?? child.Clip.Visual.BlendMode });
            }
        }

        if (transition.Window.Transition.Mgt != null)
        {
            _mgtTransitions ??= new GpuMgtCanvas(_device, Width, Height);
            _mgtTransitions.Draw(transition, group.Raw, group.Processed,
                TransitionDesignWidth > 0 ? TransitionDesignWidth : Width,
                TransitionDesignHeight > 0 ? TransitionDesignHeight : Height);
            DrawPlaced(_mgtTransitions.OutputView, Width, Height, target, new ClipVisualState(), replace: blendInputs);
            return;
        }

        _transitionShader ??= _device.CreatePixelShader(TransitionBytecode.Value);
        _transitionBuffer ??= _device.CreateBuffer(16, BindFlags.ConstantBuffer);
        _context.UpdateSubresource([new Vector4((float)transition.LeftWeight, (float)transition.RightWeight,
            transition.Window.Transition.Kind == VideoTransitionOperations.DipWhite ? 1 : 0,
            (float)transition.SolidWeight)], _transitionBuffer);
        _context.OMSetRenderTargets(target);
        _context.OMSetBlendState(blendInputs ? _opaque : _blend);
        _context.RSSetViewport(0, 0, Width, Height);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(_transitionShader);
        _context.PSSetShaderResources(0, [group.RawView, group.ProcessedView]);
        _context.PSSetSamplers(0, [_sampler]);
        _context.PSSetConstantBuffers(0, [_transitionBuffer]);
        _context.Draw(3, 0);
        _context.PSSetShaderResources(0, [null!, null!]);
        _context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
    }

    private SharedInput GetInput(IntPtr handle)
    {
        if (_inputs.TryGetValue(handle, out var existing)) return existing;
        var texture = _device.OpenSharedResource<ID3D11Texture2D>(handle);
        var input = new SharedInput
        {
            Texture = texture,
            View = _device.CreateShaderResourceView(texture),
            Mutex = texture.QueryInterface<IDXGIKeyedMutex>()
        };
        _inputs.Add(handle, input);
        return input;
    }

    private GroupTargets GetGroup(string id)
    {
        if (_groups.TryGetValue(id, out var existing)) return existing;
        ID3D11Texture2D Texture() => _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
            (uint)Width, (uint)Height, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource));
        var raw = Texture(); var processed = Texture();
        var group = new GroupTargets
        {
            Raw = raw,
            RawTarget = _device.CreateRenderTargetView(raw),
            RawView = _device.CreateShaderResourceView(raw),
            Processed = processed,
            ProcessedTarget = _device.CreateRenderTargetView(processed),
            ProcessedView = _device.CreateShaderResourceView(processed)
        };
        _groups.Add(id, group);
        return group;
    }

    private void DrawPlaced(ID3D11ShaderResourceView source, int sourceWidth, int sourceHeight,
        ID3D11RenderTargetView target, ClipVisualState state, string? blendModeOverride = null, bool replace = false)
    {
        if (state.Opacity <= 0) return;
        var rect = PreviewGeometry.PlaceLayer(Width, Height, sourceWidth, sourceHeight, state);
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var blendMode = ClipBlendModes.GpuId(blendModeOverride ?? state.BlendMode);
        if (blendMode != 0)
        {
            _blendBackdrop ??= _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
                (uint)Width, (uint)Height, 1, 1, BindFlags.ShaderResource));
            _blendBackdropView ??= _device.CreateShaderResourceView(_blendBackdrop);
            using var backdrop = target.Resource;
            _context.CopyResource(_blendBackdrop, backdrop);
        }
        var anchorX = state.FlipX ? 1 - state.AnchorX : state.AnchorX;
        var anchorY = state.FlipY ? 1 - state.AnchorY : state.AnchorY;
        var angle = state.RotationDegrees * Math.PI / 180;
        _context.UpdateSubresource([
            new Vector4((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height),
            new Vector4((float)Math.Clamp(state.Opacity, 0, 1), state.FlipX ? 1 : 0,
                state.FlipY ? 1 : 0, (float)Math.Cos(angle)),
            new Vector4((float)(rect.X + anchorX * rect.Width),
                (float)(rect.Y + anchorY * rect.Height), (float)Math.Sin(angle), blendMode)
        ], _placeBuffer);
        _context.OMSetRenderTargets(target);
        _context.OMSetBlendState(blendMode == 0 && !replace ? _blend : _opaque);
        _context.RSSetViewport(0, 0, Width, Height);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(_placeShader);
        _context.PSSetShaderResources(0, [source, blendMode == 0 ? null! : _blendBackdropView!]);
        _context.PSSetSamplers(0, [_sampler]);
        _context.PSSetConstantBuffers(0, [_placeBuffer]);
        _context.Draw(3, 0);
        _context.PSSetShaderResources(0, [null!, null!]);
        _context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
    }

    private void DrawEffect(ID3D11ShaderResourceView source, ID3D11RenderTargetView target, Clip clip,
        IReadOnlyDictionary<int, IReadOnlyList<GpuMaskLayerFrame>>? masks)
    {
        var lutPath = LutPathForClip?.Invoke(clip);
        GpuLutTexture? lut = null;
        if (!string.IsNullOrWhiteSpace(lutPath))
        {
            if (!_luts.TryGetValue(lutPath, out lut))
            {
                lut = new GpuLutTexture(_device, _context, GpuLutEffect.ParseCube(lutPath));
                _luts.Add(lutPath, lut);
            }
        }
        var filter = GpuFilterEffect.Read(clip);
        var detail = GpuDetailEffect.Read(clip);
        var original = source;
        if (filter.BlurRadius >= 0.5)
            source = (_blurPass ??= new GpuBlurPass(_device, _context, Width, Height))
                .Apply(source, filter.BlurRadius);
        var glow = detail.GlowAmount > 0.001
            ? (_glowPass ??= new GpuBlurPass(_device, _context, Width, Height))
                .Apply(original, detail.GlowRadius, brightOnly: true)
            : original;
        var values = GpuVideoSurface.EffectConstants(BasicColorEffect.Read(clip), ExposureEffect.Read(clip),
            LevelsEffect.Read(clip), MonochromeEffect.IsEnabled(clip), ColorFinishingEffect.Read(clip),
            ImageGeometryEffect.Read(clip), filter, detail, Width, Height, Width, Height, lut?.Data,
            GpuLutEffect.IsTetrahedral(clip));
        var maskViews = new ID3D11ShaderResourceView?[GpuMaskStages.Count];
        if (masks != null)
            foreach (var (stage, frames) in masks)
            {
                if (stage < 0 || stage >= GpuMaskStages.Count || frames.Count == 0) continue;
                if (!_maskPasses.TryGetValue(stage, out var pass))
                    _maskPasses[stage] = pass = new GpuMaskStackPass(_device, _context, Width, Height);
                maskViews[stage] = pass.Apply(frames);
                var index = 18 + stage / 4;
                var flags = values[index];
                if (stage % 4 == 0) flags.X = 1;
                else if (stage % 4 == 1) flags.Y = 1;
                else if (stage % 4 == 2) flags.Z = 1;
                else flags.W = 1;
                values[index] = flags;
            }
        _context.UpdateSubresource(values, _effectBuffer);
        _context.OMSetRenderTargets(target);
        _context.OMSetBlendState(_opaque);
        _context.RSSetViewport(0, 0, Width, Height);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(_effectShader);
        var resources = new ID3D11ShaderResourceView[21];
        resources[0] = source; resources[1] = glow; resources[2] = lut?.View!;
        for (var stage = 0; stage < maskViews.Length; stage++)
            resources[4 + stage] = maskViews[stage]!;
        resources[20] = original;
        _context.PSSetShaderResources(0, resources);
        _context.PSSetSamplers(0, [_sampler]);
        _context.PSSetConstantBuffers(0, [_effectBuffer]);
        _context.Draw(3, 0);
        _context.PSSetShaderResources(0, new ID3D11ShaderResourceView[21]);
        _context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
    }

    public SKBitmap ReadbackBitmap()
    {
        lock (_gpuGate)
        {
            if (_disposed || !_hasFrame) throw new InvalidOperationException("El grupo aún no tiene fotograma.");
            using var staging = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
                (uint)Width, (uint)Height, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
            GpuTextureSync.Acquire(_mutex!, 0);
            try { _context.CopyResource(staging, _output); _context.Flush(); }
            finally { _mutex.ReleaseSync(0); }
            var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            var mapped = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                var row = new byte[Width * 4];
                for (var y = 0; y < Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(mapped.DataPointer, y * (int)mapped.RowPitch), row, 0, row.Length);
                    Marshal.Copy(row, 0, IntPtr.Add(bitmap.GetPixels(), y * bitmap.RowBytes), row.Length);
                }
            }
            finally { _context.Unmap(staging, 0); }
            return bitmap;

        }
    }

    public void Dispose()
    {
        lock (_gpuGate)
        {
            if (_disposed) return;
            _disposed = true;
            var imported = _imported as IDisposable;
            _imported = null;
            if (_pendingPresentation.IsCompleted) imported?.Dispose();
            else _ = _pendingPresentation.ContinueWith(_ => imported?.Dispose(), TaskScheduler.Default);
            foreach (var input in _inputs.Values) input.Dispose();
            foreach (var group in _groups.Values) group.Dispose();
            foreach (var pass in _maskPasses.Values) pass.Dispose();
            _blurPass?.Dispose();
            _glowPass?.Dispose();
            _mgtTransitions?.Dispose();
            _blendBackdropView?.Dispose(); _blendBackdrop?.Dispose();
            foreach (var lut in _luts.Values) lut.Dispose();
            _blend.Dispose(); _opaque.Dispose(); _effectBuffer.Dispose(); _placeBuffer.Dispose();
            _sampler.Dispose(); _effectShader.Dispose(); _transitionBuffer?.Dispose(); _transitionShader?.Dispose();
            _placeShader.Dispose(); _vertexShader.Dispose();
            _mutex.Dispose(); _resource.Dispose(); _outputTarget.Dispose(); _output.Dispose();
            _context.Dispose(); _device.Dispose();

        }
    }
}
