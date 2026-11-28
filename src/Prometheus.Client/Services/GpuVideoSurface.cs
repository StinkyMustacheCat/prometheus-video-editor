using Prometheus.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Vortice.Direct3D;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using static Vortice.Direct3D11.D3D11;
using static Vortice.MediaFoundation.MediaFactory;
using Prometheus.Core.Model;
using SkiaSharp;

namespace Prometheus.Client.Services;

/// <summary>
/// Owns the D3D11 surface. Presentation and future GPU consumers borrow this surface;
/// neither receives CPU pixels or takes ownership of the graphics device.
/// </summary>
public sealed class GpuVideoSurface : IGpuFrameSource, IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private IMFDXGIDeviceManager? _manager;
    private IMFMediaEngine? _engine;
    private MFByteStream? _stream;
    private IMFSourceReader? _exactReader;
    private FfmpegVideoReader? _softwareReader;
    private bool _mediaFoundationStarted;
    private long _uploadedSoftwareFrame;
    private ID3D11VideoDevice? _exactVideoDevice;
    private ID3D11VideoContext? _exactVideoContext;
    private ID3D11VideoContext1? _exactVideoContext1;
    private ID3D11VideoProcessorEnumerator? _exactEnumerator;
    private ID3D11VideoProcessor? _exactProcessor;
    private ID3D11VideoProcessorOutputView? _exactOutputView;
    private double _exactTimestampOffset = double.NaN;
    private double _exactDecodedTime = double.NaN;
    private IMFSample? _exactPendingSample;
    private long _exactPendingTimestamp;
    private readonly bool _still;
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private int _displayRotation;
    private bool _hlgInput;
    private bool _matchPreviewHlg;
    private bool _sdrBt601Input;
    private readonly ManualResetEventSlim _ready = new(false);
    private double _pendingSeekSeconds = double.NaN;
    private ID3D11Texture2D? _texture;
    private ID3D11Texture2D? _decodeTexture;
    private IDXGISurface? _decodeSurface;
    private ID3D11ShaderResourceView? _decodeView;
    private ID3D11RenderTargetView? _targetView;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11SamplerState? _sampler;
    private ID3D11Buffer? _colorBuffer;
    private GpuBlurPass? _blurPass;
    private GpuBlurPass? _glowPass;
    private IDXGIKeyedMutex? _mutex;
    private IDXGISurface? _dxgiSurface;
    private IDXGIResource? _resource;
    private ICompositionImportedGpuImage? _imported;
    private CompositionDrawingSurface? _drawingSurface;
    private volatile bool _disposed;
    private readonly object _gpuGate = new();
    private Task _pendingPresentation = Task.CompletedTask;
    private long _frameGeneration;
    private long _surfaceFrameGeneration;
    private double _presentedTime;
    private bool _wasPlaying;
    private BasicColorSettings _renderedColor = BasicColorSettings.Neutral;
    private ExposureSettings _renderedExposure = ExposureSettings.Neutral;
    private LevelsSettings _renderedLevels = LevelsSettings.Neutral;
    private bool _renderedMonochrome;
    private ColorFinishingSettings _renderedFinishing = ColorFinishingSettings.Neutral;
    private ImageGeometrySettings _renderedGeometry = ImageGeometrySettings.Neutral;
    private GpuFilterSettings _renderedFilter = GpuFilterSettings.Neutral;
    private GpuDetailSettings _renderedDetail = GpuDetailSettings.Neutral;
    private string? _renderedLutPath;
    private bool _renderedLutTetrahedral;
    private GpuLutTexture? _lutTexture;
    private string? _loadedLutPath;
    private GpuSamMaskTexture? _samMaskTexture;
    private GpuMaskBlendPass? _maskBlendPass;
    private GpuMaskStackPass? _maskStackPass;
    private readonly Dictionary<int, GpuMaskStackPass> _maskGroupPasses = new();
    private GpuMaskRefinePass? _samRefinePass;
    private ID3D11ShaderResourceView? _refinedMaskView;
    private string? _refinedMaskId;
    private SamMaskRefinement _refinedMaskSettings;
    private SamMaskRefinement _renderedMaskRefinement;
    private int _refineWidth;
    private int _refineHeight;
    private string? _loadedSamMaskId;
    private string? _renderedSamMaskId;
    private GpuEffectProfile? _renderedMaskBaseProfile;
    private string? _renderedMaskStackSignature;

    internal double? SourceFrameRate { get; }
    internal bool HasReusableMovAlpha { get; private set; }
    public IntPtr SharedHandle { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public double CurrentTime { get => _engine?.CurrentTime ?? 0; set { if (_engine != null) _engine.CurrentTime = value; } }
    public BasicColorSettings ColorSettings { get; set; } = BasicColorSettings.Neutral;
    public ExposureSettings ExposureSettings { get; set; } = ExposureSettings.Neutral;
    public LevelsSettings LevelsSettings { get; set; } = LevelsSettings.Neutral;
    public bool MonochromeEnabled { get; set; }
    public ColorFinishingSettings FinishingSettings { get; set; } = ColorFinishingSettings.Neutral;
    public ImageGeometrySettings GeometrySettings { get; set; } = ImageGeometrySettings.Neutral;
    public GpuFilterSettings FilterSettings { get; set; } = GpuFilterSettings.Neutral;
    public GpuDetailSettings DetailSettings { get; set; } = GpuDetailSettings.Neutral;
    public string? LutPath { get; set; }
    public bool LutTetrahedral { get; set; }
    internal ShapeMask? LegacyShapeMask { get; set; }
    public SamMaskKeyframe? SamMask { get; set; }
    public IReadOnlyList<GpuMaskLayerFrame>? MaskStack { get; set; }
    public IReadOnlyDictionary<int, IReadOnlyList<GpuMaskLayerFrame>>? MaskGroups { get; set; }
    public SamMaskRefinement SamMaskRefinement { get; set; } = SamMaskRefinement.Neutral;
    public GpuEffectProfile? MaskBaseProfile { get; set; }

    public void PauseForWarmup()
    {
        if (_disposed || _engine == null) return;
        _engine.Pause();
        _wasPlaying = false;
        if (_frameGeneration == 0) _pendingSeekSeconds = double.NaN;
    }

    internal bool PrepareForReuse()
    {
        lock (_gpuGate)
        {
            if (_disposed || _softwareReader != null && !HasReusableMovAlpha) return false;
            PauseForWarmup();
            if (_engine != null)
            {
                _pendingSeekSeconds = double.NaN;
                _presentedTime = double.NegativeInfinity;
            }
            DetachDrawingSurface();
            return true;
        }
    }

    private void DetachDrawingSurface()
    {
        var imported = _imported as IDisposable;
        _imported = null;
        if (_pendingPresentation.IsCompleted) imported?.Dispose();
        else _ = _pendingPresentation.ContinueWith(_ => imported?.Dispose(), TaskScheduler.Default);
        _drawingSurface = null;
        _surfaceFrameGeneration = 0;
    }

    private const string ColorShader = """
        Texture2D sourceTexture : register(t0);
        Texture2D glowTexture : register(t1);
        Texture3D lutTexture : register(t2);
        Texture2D samMaskTexture : register(t3);
        Texture2D maskTextures[16] : register(t4);
        Texture2D originalTexture : register(t20);
        SamplerState sourceSampler : register(s0);
        cbuffer ColorParams : register(b0) {
            float brightness; float contrast; float saturation; float padding;
            float exposureStops; float exposureBlack; float2 reserved;
            float levelsBlack; float levelsWhite; float levelsGamma; float monochrome;
            float temperature; float tint; float shadows; float highlights;
            float hueDegrees; float vibrance; float vignette; float finishPadding;
            float sourceWidth; float sourceHeight; float pixelBlock; float flipHorizontal;
            float flipVertical; float cropLeft; float cropTop; float cropRight;
            float cropBottom; float3 geometryPadding;
            float blurRadius; float3 keyColor;
            float keyTolerance; float keySoftness; float keySpill; float keyEnabled;
            float sharpenAmount; float glowAmount; float renderWidth; float renderHeight;
            float lutEnabled; float lutSize; float lutTetrahedral; float lutPadding;
            float4 lutDomainMin;
            float4 lutDomainMax;
            float4 keySecond;
            float4 keyThird;
            float4 keyExtras;
            float4 keyExtras2;
            float4 maskFlags[4];
        };
        float MaskAt(int stage, float2 uv) {
            if (maskFlags[stage / 4][stage % 4] < 0.5) return 1.0;
            return saturate(maskTextures[stage].Sample(sourceSampler, uv).r);
        }
        struct VertexOut { float4 position : SV_Position; float2 uv : TEXCOORD0; };
        VertexOut VSMain(uint id : SV_VertexID) {
            VertexOut o;
            o.uv = float2((id << 1) & 2, id & 2);
            o.position = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }
        float3 LutSample(float3 rgb) {
            float3 inputColor = saturate((rgb - lutDomainMin.xyz) /
                max(float3(0.00001, 0.00001, 0.00001), lutDomainMax.xyz - lutDomainMin.xyz));
            float3 scaled = inputColor * (lutSize - 1.0);
            if (lutTetrahedral < 0.5)
                return lutTexture.Sample(sourceSampler, (scaled + 0.5) / lutSize).rgb;
            int3 low = int3(floor(scaled));
            int3 high = min(low + 1, (int)lutSize - 1);
            float3 f = scaled - low;
            float3 c000 = lutTexture.Load(int4(low, 0)).rgb;
            float3 c111 = lutTexture.Load(int4(high, 0)).rgb;
            float3 c100 = lutTexture.Load(int4(high.x, low.y, low.z, 0)).rgb;
            float3 c010 = lutTexture.Load(int4(low.x, high.y, low.z, 0)).rgb;
            float3 c001 = lutTexture.Load(int4(low.x, low.y, high.z, 0)).rgb;
            float3 c110 = lutTexture.Load(int4(high.x, high.y, low.z, 0)).rgb;
            float3 c101 = lutTexture.Load(int4(high.x, low.y, high.z, 0)).rgb;
            float3 c011 = lutTexture.Load(int4(low.x, high.y, high.z, 0)).rgb;
            if (f.x >= f.y && f.y >= f.z) return c000 + f.x * (c100 - c000) + f.y * (c110 - c100) + f.z * (c111 - c110);
            if (f.x >= f.z && f.z >= f.y) return c000 + f.x * (c100 - c000) + f.z * (c101 - c100) + f.y * (c111 - c101);
            if (f.z >= f.x && f.x >= f.y) return c000 + f.z * (c001 - c000) + f.x * (c101 - c001) + f.y * (c111 - c101);
            if (f.y >= f.x && f.x >= f.z) return c000 + f.y * (c010 - c000) + f.x * (c110 - c010) + f.z * (c111 - c110);
            if (f.y >= f.z && f.z >= f.x) return c000 + f.y * (c010 - c000) + f.z * (c011 - c010) + f.x * (c111 - c011);
            return c000 + f.z * (c001 - c000) + f.y * (c011 - c001) + f.x * (c111 - c011);
        }
        float KeyOpacity(float3 sampleColor, float3 targetColor, float tolerance, float softness) {
            float strength = saturate((max(targetColor.r, max(targetColor.g, targetColor.b)) -
                min(targetColor.r, min(targetColor.g, targetColor.b))) * 2.0);
            float3 sampleChroma = sampleColor / max(0.05, max(sampleColor.r, max(sampleColor.g, sampleColor.b)));
            float3 targetChroma = targetColor / max(0.05, max(targetColor.r, max(targetColor.g, targetColor.b)));
            float distanceToKey = length(lerp(sampleColor - targetColor,
                sampleChroma - targetChroma, strength)) * 0.57735027;
            return smoothstep(tolerance, tolerance + max(softness, 0.001), distanceToKey);
        }
        float3 MatchHlgPreview(float3 value) {
            // The D3D11 processor has already converted HLG/BT.2020 to RGB/BT.709.
            // Match the SDR monitor curve measured from the Windows media engine.
            float3 mapped = max(0.0, value - 0.08);
            mapped -= 0.3714286 * max(0.0, value - 0.20);
            mapped += 0.6214286 * max(0.0, value - 0.55);
            mapped += 0.85 * max(0.0, value - 0.75);
            mapped -= 0.7666667 * max(0.0, value - 0.85);
            return saturate(mapped);
        }
        float4 PSMain(VertexOut input) : SV_Target {
            if (input.uv.x < cropLeft || input.uv.x > 1.0 - cropRight ||
                input.uv.y < cropTop || input.uv.y > 1.0 - cropBottom)
                return float4(0, 0, 0, 0);
            float2 sampleUv = input.uv;
            if (flipHorizontal > 0.5) sampleUv.x = 1.0 - sampleUv.x;
            if (flipVertical > 0.5) sampleUv.y = 1.0 - sampleUv.y;
            float2 originalUv = sampleUv;
            if (pixelBlock > 1.5) {
                float2 sourceSize = float2(max(1.0, sourceWidth), max(1.0, sourceHeight));
                sampleUv = (floor(sampleUv * sourceSize / pixelBlock) + 0.5) * pixelBlock / sourceSize;
            }
            if (geometryPadding.x > 2.5) { sampleUv = float2(sampleUv.y, 1.0 - sampleUv.x); originalUv = float2(originalUv.y, 1.0 - originalUv.x); }
            else if (geometryPadding.x > 1.5) { sampleUv = 1.0 - sampleUv; originalUv = 1.0 - originalUv; }
            else if (geometryPadding.x > 0.5) { sampleUv = float2(1.0 - sampleUv.y, sampleUv.x); originalUv = float2(1.0 - originalUv.y, originalUv.x); }
            float blurMask = MaskAt(10, input.uv);
            float4 pixel = lerp(originalTexture.Sample(sourceSampler, sampleUv),
                sourceTexture.Sample(sourceSampler, sampleUv), blurMask);
            if (pixelBlock > 1.5 && maskFlags[2].y > 0.5) {
                float4 originalPixel = lerp(originalTexture.Sample(sourceSampler, originalUv),
                    sourceTexture.Sample(sourceSampler, originalUv), blurMask);
                pixel = lerp(originalPixel, pixel, MaskAt(9, input.uv));
            }
            float3 straight = pixel.a > 0.00001 ? pixel.rgb / pixel.a : float3(0, 0, 0);
            if (keyExtras2.y > 1.5) straight = MatchHlgPreview(straight);
            else if (keyExtras2.y > 0.5) straight = pow(saturate(straight), 1.55);
            float keyAlpha = 1;
            float3 spillColor = keyColor;
            float spillAmount = keySpill;
            if (keyEnabled > 0.5) {
                keyAlpha = KeyOpacity(straight, keyColor, keyTolerance, keySoftness);
                if (keyExtras.x > 1.5) {
                    float second = KeyOpacity(straight, keySecond.xyz, keySecond.w, keyExtras.y);
                    if (second < keyAlpha) { keyAlpha = second; spillColor = keySecond.xyz; spillAmount = keyExtras.w; }
                }
                if (keyExtras.x > 2.5) {
                    float third = KeyOpacity(straight, keyThird.xyz, keyThird.w, keyExtras.z);
                    if (third < keyAlpha) { keyAlpha = third; spillColor = keyThird.xyz; spillAmount = keyExtras2.x; }
                }
            }
            float3 rgb = straight;
            float3 before = rgb;
            rgb = (straight - 0.5) * contrast + 0.5 + brightness;
            float luminance = dot(rgb, float3(0.2126, 0.7152, 0.0722));
            rgb = luminance.xxx + (rgb - luminance.xxx) * saturation;
            rgb = lerp(before, rgb, MaskAt(0, input.uv));
            before = rgb;
            rgb = rgb * exp2(exposureStops) + exposureBlack;
            rgb = lerp(before, rgb, MaskAt(1, input.uv));
            before = rgb;
            rgb = pow(saturate((rgb - levelsBlack) / max(0.01, levelsWhite - levelsBlack)),
                1.0 / max(0.1, levelsGamma));
            rgb = lerp(before, rgb, MaskAt(2, input.uv));
            before = rgb;
            float warmth = clamp((6500.0 - temperature) / 5500.0, -1.0, 1.0);
            rgb *= float3(1.0 + warmth * 0.12 + tint * 0.04,
                          1.0 - abs(warmth) * 0.03 - tint * 0.06,
                          1.0 - warmth * 0.12 + tint * 0.04);
            rgb = lerp(before, rgb, MaskAt(3, input.uv));
            before = rgb;
            float tonalLuma = dot(saturate(rgb), float3(0.2126, 0.7152, 0.0722));
            rgb += shadows * 0.30 * (1.0 - tonalLuma) * (1.0 - tonalLuma);
            rgb += highlights * 0.30 * tonalLuma * tonalLuma;
            rgb = lerp(before, rgb, MaskAt(4, input.uv));
            before = rgb;
            float angle = radians(hueDegrees);
            float3 axis = normalize(float3(1.0, 1.0, 1.0));
            rgb = rgb * cos(angle) + cross(axis, rgb) * sin(angle) +
                  axis * dot(axis, rgb) * (1.0 - cos(angle));
            rgb = lerp(before, rgb, MaskAt(5, input.uv));
            before = rgb;
            float chroma = max(rgb.r, max(rgb.g, rgb.b)) - min(rgb.r, min(rgb.g, rgb.b));
            float finishLuma = dot(rgb, float3(0.2126, 0.7152, 0.0722));
            rgb = finishLuma.xxx + (rgb - finishLuma.xxx) *
                  max(0.0, 1.0 + vibrance * (1.0 - saturate(chroma)) * 0.5);
            rgb = lerp(before, rgb, MaskAt(6, input.uv));
            before = rgb;
            if (monochrome > 0.5) {
                float gray = dot(rgb, float3(0.2126, 0.7152, 0.0722));
                rgb = gray.xxx;
            }
            rgb = lerp(before, rgb, MaskAt(8, input.uv));
            before = rgb;
            float2 edgeUv = (input.uv - 0.5) * 2.0;
            float edge = smoothstep(0.35, 0.95, length(edgeUv) * 0.70710678);
            rgb *= 1.0 - vignette * edge;
            rgb = lerp(before, rgb, MaskAt(7, input.uv));
            before = rgb;
            if (sharpenAmount > 0.001) {
                float2 step = 1.0 / float2(max(1.0, geometryPadding.x > 0.5 ? sourceWidth : renderWidth),
                    max(1.0, geometryPadding.x > 0.5 ? sourceHeight : renderHeight));
                float4 neighbors = sourceTexture.Sample(sourceSampler, sampleUv + float2(step.x, 0)) +
                    sourceTexture.Sample(sourceSampler, sampleUv - float2(step.x, 0)) +
                    sourceTexture.Sample(sourceSampler, sampleUv + float2(0, step.y)) +
                    sourceTexture.Sample(sourceSampler, sampleUv - float2(0, step.y));
                float3 average = neighbors.a > 0.00001 ? neighbors.rgb / neighbors.a : float3(0, 0, 0);
                rgb += sharpenAmount * (straight - average);
            }
            rgb = lerp(before, rgb, MaskAt(12, input.uv));
            if (keyEnabled > 0.5 && spillAmount > 0) {
                float keyValue = dot(rgb, spillColor) / max(0.001, dot(spillColor, 1.0.xxx));
                float otherValue = dot(rgb, 1.0.xxx - spillColor) /
                    max(0.001, dot(1.0.xxx - spillColor, 1.0.xxx));
                rgb -= spillColor * max(0, keyValue - otherValue) * spillAmount *
                    (1 - keyAlpha) * MaskAt(11, input.uv);
            }
            before = rgb;
            if (lutEnabled > 0.5) rgb = LutSample(rgb);
            rgb = lerp(before, rgb, MaskAt(14, input.uv));
            keyAlpha = lerp(1.0, keyAlpha, MaskAt(11, input.uv));
            float samAlpha = keyExtras2.z > 0.5 ?
                samMaskTexture.Sample(sourceSampler, input.uv).r : 1.0;
            samAlpha *= MaskAt(15, input.uv);
            float alpha = pixel.a * keyAlpha * samAlpha;
            float3 premul = saturate(rgb) * alpha;
            if (glowAmount > 0.001) {
                float4 bloom = glowTexture.Sample(sourceSampler,
                    sampleUv);
                bloom *= keyAlpha * samAlpha;
                float effectiveGlow = glowAmount * MaskAt(13, input.uv);
                float outAlpha = saturate(alpha + bloom.a * effectiveGlow * (1 - alpha));
                premul = min(saturate(premul + bloom.rgb * effectiveGlow), outAlpha.xxx);
                alpha = outAlpha;
            }
            return float4(premul, alpha);
        }
        """;
    private static readonly Lazy<(byte[] Vertex, byte[] Pixel)> ColorBytecode = new(() => (
        Compiler.Compile(ColorShader, "VSMain", "BasicColor", "vs_5_0", ShaderFlags.None, EffectFlags.None).ToArray(),
        Compiler.Compile(ColorShader, "PSMain", "BasicColor", "ps_5_0", ShaderFlags.None, EffectFlags.None).ToArray()));

    internal static (byte[] Vertex, byte[] Pixel) EffectBytecode => ColorBytecode.Value;

    internal static Vector4[] EffectConstants(BasicColorSettings color, ExposureSettings exposure,
        LevelsSettings levels, bool monochrome, ColorFinishingSettings finishing,
        ImageGeometrySettings geometry, GpuFilterSettings filter, GpuDetailSettings detail,
        int sourceWidth, int sourceHeight, int renderWidth, int renderHeight,
        CubeLut? lut = null, bool tetrahedral = false, int displayRotation = 0,
        bool hlgToSdr = false, bool samMaskEnabled = false, bool matchPreviewHlg = false) =>
    [
        new Vector4((float)color.Brightness, (float)color.Contrast, (float)color.Saturation, 0),
        new Vector4((float)exposure.Stops, (float)exposure.Black, 0, 0),
        new Vector4((float)levels.Black, (float)levels.White, (float)levels.Gamma, monochrome ? 1 : 0),
        new Vector4((float)finishing.Temperature, (float)finishing.Tint,
            (float)finishing.Shadows, (float)finishing.Highlights),
        new Vector4((float)finishing.HueDegrees, (float)finishing.Vibrance, (float)finishing.Vignette, 0),
        new Vector4(sourceWidth, sourceHeight, (float)geometry.PixelBlock, geometry.FlipHorizontal ? 1 : 0),
        new Vector4(geometry.FlipVertical ? 1 : 0, (float)geometry.CropLeft,
            (float)geometry.CropTop, (float)geometry.CropRight),
        new Vector4((float)geometry.CropBottom, displayRotation / 90, 0, 0),
        new Vector4((float)filter.BlurRadius, (float)filter.KeyRed, (float)filter.KeyGreen, (float)filter.KeyBlue),
        new Vector4((float)filter.Tolerance, (float)filter.Softness, (float)filter.Spill,
            filter.ChromaEnabled ? 1 : 0),
        new Vector4((float)detail.SharpenAmount, (float)detail.GlowAmount, renderWidth, renderHeight),
        new Vector4(lut != null ? 1 : 0, lut?.Size ?? 2, tetrahedral ? 1 : 0, 0),
        new Vector4(lut?.DomainMin ?? Vector3.Zero, 0),
        new Vector4(lut?.DomainMax ?? Vector3.One, 0),
        new Vector4((float)filter.Key2Red, (float)filter.Key2Green,
            (float)filter.Key2Blue, (float)filter.Tolerance2),
        new Vector4((float)filter.Key3Red, (float)filter.Key3Green,
            (float)filter.Key3Blue, (float)filter.Tolerance3),
        new Vector4(filter.KeyCount, (float)filter.Softness2,
            (float)filter.Softness3, (float)filter.Spill2),
        new Vector4((float)filter.Spill3, hlgToSdr ? (matchPreviewHlg ? 2 : 1) : 0, samMaskEnabled ? 1 : 0, 0),
        Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero
    ];

    public bool TryGetCurrentFrame(out GpuFrameReference frame)
    {
        if (_disposed || _frameGeneration == 0)
        {
            frame = default;
            return false;
        }
        frame = new GpuFrameReference(SharedHandle, Width, Height, _presentedTime, _frameGeneration, true);
        return true;
    }

    public GpuVideoSurface(string path, bool still = false, int previewLongEdge = 0,
        double initialSeconds = 0, bool exactDecode = false)
    {
        _still = still;
        D3D11CreateDevice(null, DriverType.Hardware,
            DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
            out _device, out _, out _context).CheckError();
        SKBitmap? pixels = null;
        try
        {
            using var multithread = _device.QueryInterface<ID3D11Multithread>();
            multithread.SetMultithreadProtected(true);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            VideoMetadata? videoMetadata = !still && (exactDecode || extension is ".mov" or ".webm" or ".mkv" or ".ts" or ".m2ts" or ".mts")
                ? ReadVideoMetadata(path) : null;
            SourceFrameRate = videoMetadata?.Fps;
            if (still)
            {
                using var codec = SKCodec.Create(path) ?? throw new InvalidOperationException("No se pudo abrir la imagen.");
                using var decoded = SKBitmap.Decode(codec) ?? throw new InvalidOperationException("No se pudo decodificar la imagen.");
                var origin = codec.EncodedOrigin;
                var swapped = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or
                    SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
                pixels = new SKBitmap(new SKImageInfo(swapped ? decoded.Height : decoded.Width,
                    swapped ? decoded.Width : decoded.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
                using var canvas = new SKCanvas(pixels);
                canvas.Clear(SKColors.Transparent);
                OrientImage(canvas, origin, decoded.Width, decoded.Height);
                canvas.DrawBitmap(decoded, 0, 0);
                Width = pixels.Width;
                Height = pixels.Height;
            }
            // Media Foundation may report TS as ready without yielding a frame at the clip's local start.
            else if (videoMetadata is { } metadata && (metadata.Alpha || metadata.MpegTransportStream))
                OpenSoftware(path, metadata, previewLongEdge);
            else
            {
                try
                {
                    MFStartup().CheckError();
                    _mediaFoundationStarted = true;
                    if (exactDecode) OpenExact(path, videoMetadata!.Value);
                    else OpenPreview(path);
                }
                catch (Exception error) when (error is SharpGen.Runtime.SharpGenException or InvalidOperationException
                    or IOException or NotSupportedException)
                {
                    ReleaseMediaFoundation();
                    OpenSoftware(path, videoMetadata ?? ReadVideoMetadata(path), previewLongEdge);
                }
            }

            if (previewLongEdge > 0 && Math.Max(Width, Height) > previewLongEdge)
            {
                var factor = (double)previewLongEdge / Math.Max(Width, Height);
                if (_softwareReader == null)
                {
                    _sourceWidth = _displayRotation is 90 or 270 ? Height : Width;
                    _sourceHeight = _displayRotation is 90 or 270 ? Width : Height;
                }
                Width = Math.Max(1, (int)Math.Round(Width * factor));
                Height = Math.Max(1, (int)Math.Round(Height * factor));
            }
            if (_sourceWidth == 0)
            {
                _sourceWidth = _displayRotation is 90 or 270 ? Height : Width;
                _sourceHeight = _displayRotation is 90 or 270 ? Width : Height;
            }
            _texture = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
                (uint)Width, (uint)Height, 1, 1,
                BindFlags.RenderTarget | BindFlags.ShaderResource,
                miscFlags: ResourceOptionFlags.SharedKeyedMutex));
            _decodeTexture = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
                (uint)_sourceWidth, (uint)_sourceHeight, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource));
            if (pixels != null)
            {
                _context.UpdateSubresource(_decodeTexture, 0, null, pixels.GetPixels(), (uint)pixels.RowBytes, 0);
            }
            _decodeSurface = _decodeTexture.QueryInterface<IDXGISurface>();
            _decodeView = _device.CreateShaderResourceView(_decodeTexture);
            _targetView = _device.CreateRenderTargetView(_texture);
            _vertexShader = _device.CreateVertexShader(ColorBytecode.Value.Vertex);
            _pixelShader = _device.CreatePixelShader(ColorBytecode.Value.Pixel);
            _sampler = _device.CreateSamplerState(SamplerDescription.LinearClamp);
            _colorBuffer = _device.CreateBuffer(352, BindFlags.ConstantBuffer);
            _mutex = _texture.QueryInterface<IDXGIKeyedMutex>();
            _dxgiSurface = _texture.QueryInterface<IDXGISurface>();
            _resource = _texture.QueryInterface<IDXGIResource>();
            SharedHandle = _resource.SharedHandle;
            if (_exactReader != null)
            {
                _exactVideoDevice = _device.QueryInterface<ID3D11VideoDevice>();
                _exactVideoContext = _context.QueryInterface<ID3D11VideoContext>();
                _exactVideoContext1 = _context.QueryInterface<ID3D11VideoContext1>();
                var content = new VideoProcessorContentDescription
                {
                    InputFrameFormat = VideoFrameFormat.Progressive,
                    InputFrameRate = new Vortice.DXGI.Rational(30, 1),
                    InputWidth = (uint)_sourceWidth,
                    InputHeight = (uint)_sourceHeight,
                    OutputFrameRate = new Vortice.DXGI.Rational(30, 1),
                    OutputWidth = (uint)_sourceWidth,
                    OutputHeight = (uint)_sourceHeight,
                    Usage = VideoUsage.PlaybackNormal
                };
                _exactVideoDevice.CreateVideoProcessorEnumerator(ref content, out _exactEnumerator).CheckError();
                _exactVideoDevice.CreateVideoProcessor(_exactEnumerator, 0, out _exactProcessor).CheckError();
                if (_hlgInput)
                {
                    _exactVideoContext1!.VideoProcessorSetStreamColorSpace1(_exactProcessor, 0,
                        ColorSpaceType.YcbcrStudioGhlgTopLeftP2020);
                    _exactVideoContext1.VideoProcessorSetOutputColorSpace1(_exactProcessor,
                        ColorSpaceType.RgbFullG22NoneP709);
                }
                else
                {
                    _exactVideoContext1.VideoProcessorSetStreamColorSpace1(_exactProcessor, 0,
                        _sdrBt601Input ? ColorSpaceType.YcbcrStudioG22LeftP601 :
                            ColorSpaceType.YcbcrStudioG22LeftP709);
                    _exactVideoContext1.VideoProcessorSetOutputColorSpace1(_exactProcessor,
                        ColorSpaceType.RgbFullG22NoneP709);
                }
            }
            if (_engine != null)
            {
                _engine.Muted = true; // Audio playback remains on the existing timeline transport.
                if (initialSeconds > 0.001) _pendingSeekSeconds = initialSeconds;
                _engine.CurrentTime = Math.Max(0, initialSeconds);
                _engine.Play();
            }
        }
        catch
        {
            Dispose();
            throw;
        }
        finally { pixels?.Dispose(); }
    }

    private void OpenExact(string path, VideoMetadata metadata)
    {
        _displayRotation = metadata.Rotation;
        _hlgInput = metadata.Hlg;
        _sdrBt601Input = metadata.SdrBt601;
        _manager = MFCreateDXGIDeviceManager();
        _manager.ResetDevice(_device).CheckError();
        using var attributes = MFCreateAttributes(3);
        attributes.Set(SourceReaderAttributeKeys.D3DManager, _manager).CheckError();
        attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u).CheckError();
        _exactReader = MFCreateSourceReaderFromURL(path, attributes);
        _exactReader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        _exactReader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);
        using var mediaType = MFCreateMediaType();
        mediaType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
        mediaType.Set(MediaTypeAttributeKeys.Subtype,
            _hlgInput ? VideoFormatGuids.P010 : VideoFormatGuids.NV12).CheckError();
        _exactReader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, mediaType);
        using var currentType = _exactReader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
        var frameSize = currentType.GetUInt64(MediaTypeAttributeKeys.FrameSize);
        Width = (int)(frameSize >> 32);
        Height = (int)(frameSize & 0xffffffff);
        if (_displayRotation is 90 or 270) (Width, Height) = (Height, Width);
        _exactReader.SetCurrentPosition(0);
        var foundFrame = false;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            using var first = _exactReader.ReadSample(SourceReaderIndex.FirstVideoStream,
                SourceReaderControlFlag.None, out _, out var flags, out var firstTimestamp);
            if (flags.HasFlag(SourceReaderFlag.Error) || flags.HasFlag(SourceReaderFlag.EndOfStream)) break;
            if (first == null) continue;
            using var buffer = first.GetBufferByIndex(0);
            using var dxgi = buffer.QueryInterface<IMFDXGIBuffer>();
            _exactTimestampOffset = firstTimestamp / 10_000_000.0;
            foundFrame = true;
            break;
        }
        if (!foundFrame) throw new InvalidOperationException("El lector de Windows no entregó un fotograma.");
        _exactReader.SetCurrentPosition(0);
    }

    private void OpenPreview(string path)
    {
        _manager = MFCreateDXGIDeviceManager();
        _manager.ResetDevice(_device).CheckError();
        using var factory = new IMFMediaEngineClassFactory();
        using var attributes = MFCreateAttributes(1);
        attributes.VideoOutputFormat = Format.B8G8R8A8_UNorm;
        attributes.DxgiManager = _manager;
        _engine = factory.CreateInstance(MediaEngineCreateFlags.None, attributes,
            (evt, _, _) =>
            {
                if (evt is MediaEngineEvent.CanPlay or MediaEngineEvent.Error)
                {
                    try { _ready.Set(); }
                    catch (ObjectDisposedException) { } // A final callback can outlive Shutdown.
                }
            });
        using var ex = _engine.QueryInterface<IMFMediaEngineEx>();
        _stream = new MFByteStream(path);
        ex.SetSourceFromByteStream(_stream, path);
        if (!_ready.Wait(3000)) throw new InvalidOperationException("El decodificador GPU no pudo abrir el medio.");
        var size = _engine.NativeVideoSize;
        if (size.Width <= 0 || size.Height <= 0)
            throw new InvalidOperationException("El lector de Windows no reconoció la pista de vídeo.");
        Width = size.Width;
        Height = size.Height;
    }

    private void OpenSoftware(string path, VideoMetadata metadata, int previewLongEdge)
    {
        if (metadata.Width <= 0 || metadata.Height <= 0)
            throw new InvalidDataException("No se encontró una pista de vídeo que se pueda reproducir.");

        _displayRotation = metadata.Rotation;
        _hlgInput = metadata.Hlg && !metadata.Alpha;
        Width = metadata.Width;
        Height = metadata.Height;
        if (_displayRotation is 90 or 270) (Width, Height) = (Height, Width);
        var scale = previewLongEdge > 0 ? Math.Min(1, (double)previewLongEdge / Math.Max(Width, Height)) : 1;
        var width = Math.Max(1, (int)Math.Round(metadata.Width * scale));
        var height = Math.Max(1, (int)Math.Round(metadata.Height * scale));
        HasReusableMovAlpha = metadata.MovAlphaDecoder != null;
        _softwareReader = new FfmpegVideoReader(path, width, height, metadata.Fps ?? 30,
            alpha: metadata.Alpha, timed: !metadata.Alpha || metadata.Vp9Alpha || HasReusableMovAlpha,
            decoder: metadata.Vp9Alpha ? "libvpx-vp9" : metadata.MovAlphaDecoder,
            packedMovAlpha: HasReusableMovAlpha, sourcePremultiplied: metadata.Premultiplied);
    }

    private void ReleaseMediaFoundation()
    {
        _engine?.Shutdown();
        _stream?.Dispose();
        _engine?.Dispose();
        _exactReader?.Dispose();
        _manager?.Dispose();
        _stream = null;
        _engine = null;
        _exactReader = null;
        _manager = null;
        if (!_mediaFoundationStarted) return;
        MFShutdown();
        _mediaFoundationStarted = false;
    }

    public async Task AttachAsync(Compositor compositor, CompositionDrawingSurface drawingSurface)
    {
        var interop = await compositor.TryGetCompositionGpuInterop()
            ?? throw new NotSupportedException("El backend gráfico de Avalonia no permite compartir texturas.");
        if (!interop.SupportedImageHandleTypes.Contains(KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle))
            throw new NotSupportedException("Este backend no acepta texturas D3D11 compartidas.");
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

    public async Task<bool> PresentAsync(double seconds, bool playing, double toleranceSeconds = 1.0 / 120,
        bool updateVisual = true, CancellationToken cancellation = default)
    {
        if (_disposed || updateVisual && (_imported == null || _drawingSurface == null)) return false;
        var before = _frameGeneration;
        var releaseKey = updateVisual ? 1UL : 0UL;
        bool ready;
        if (_softwareReader != null)
        {
            if (!await _softwareReader.ReadPreviewAtAsync(seconds, playing, cancellation) || _disposed) return false;
            cancellation.ThrowIfCancellationRequested();
            ready = DrawSoftwareFrame(_softwareReader, seconds, releaseKey);
        }
        else ready = _exactReader != null
            ? await Task.Run(() => RenderExactDecodedFrame(seconds, toleranceSeconds, releaseKey,
                keepPrecedingSample: true, cancellation: cancellation, allowLongSeek: true), cancellation)
            : RenderFrame(seconds, playing, releaseKey);
        if (!ready) return false;
        if (updateVisual)
            await PresentCurrentFrameAsync(alreadyReleasedForVisual: _frameGeneration != before);
        return true;
    }

    internal async Task PresentCurrentFrameAsync(bool alreadyReleasedForVisual = false)
    {
        Task presentation;
        long generation;
        lock (_gpuGate)
        {
            if (_disposed || _frameGeneration == _surfaceFrameGeneration || _imported == null || _drawingSurface == null) return;
            if (!alreadyReleasedForVisual)
            {
                GpuTextureSync.Acquire(_mutex!, 0);
                _mutex!.ReleaseSync(1);
            }
            generation = _frameGeneration;
            presentation = _pendingPresentation = _drawingSurface.UpdateWithKeyedMutexAsync(_imported, 1, 0);
        }
        await presentation;
        lock (_gpuGate) _surfaceFrameGeneration = generation;
    }

    /// <summary>Advances a frame without submitting it to the Avalonia compositor.</summary>
    public bool RenderOffscreen(double seconds, double toleranceSeconds, CancellationToken cancellation = default)
    {
        lock (_gpuGate)
        {
            if (_disposed) return false;
            if (_softwareReader != null) return _softwareReader.ReadAt(seconds, cancellation) && DrawSoftwareFrame(_softwareReader, seconds, 0);
            if (_exactReader != null)
            {
                _matchPreviewHlg = true;
                return RenderExactDecodedFrame(seconds, toleranceSeconds, 0, cancellation: cancellation, allowLongSeek: true);
            }
            return RenderFrame(seconds, false, 0);

        }
    }

    private bool DrawSoftwareFrame(FfmpegVideoReader reader, double seconds, ulong releaseKey)
    {
        lock (_gpuGate)
        {
            if (_disposed) return false;
            if (reader.FrameVersion != _uploadedSoftwareFrame)
            {
                var pinned = GCHandle.Alloc(reader.Frame, GCHandleType.Pinned);
                try
                {
                    _context.UpdateSubresource(_decodeTexture!, 0, null, pinned.AddrOfPinnedObject(),
                        (uint)(_sourceWidth * 4), 0);
                }
                finally { pinned.Free(); }
                _uploadedSoftwareFrame = reader.FrameVersion;
            }
            else if (!RenderSettingsChanged())
            {
                _presentedTime = seconds;
                return _frameGeneration > 0;
            }
            GpuTextureSync.Acquire(_mutex!, 0);
            try { DrawColor(_targetView!, Width, Height); }
            finally { _mutex!.ReleaseSync(releaseKey); }
            _presentedTime = seconds;
            RememberRenderSettings();
            _frameGeneration++;
            return true;

        }
    }

    private bool RenderExactDecodedFrame(double seconds, double toleranceSeconds, ulong releaseKey,
        bool keepPrecedingSample = true, CancellationToken cancellation = default, bool allowLongSeek = false)
    {
        lock (_gpuGate)
        {
            if (_disposed || _exactReader == null || _exactVideoDevice == null || _exactVideoContext == null ||
                _exactEnumerator == null || _exactProcessor == null) return false;
            if (Math.Abs(_exactDecodedTime - seconds) <= toleranceSeconds)
            {
                DrawExactOutput(seconds, releaseKey);
                return true;
            }
            var sought = double.IsNaN(_exactDecodedTime) || seconds < _exactDecodedTime - toleranceSeconds ||
                seconds > _exactDecodedTime + 1;
            if (sought)
            {
                _exactPendingSample?.Dispose();
                _exactPendingSample = null;
                _exactReader.SetCurrentPosition((long)Math.Round(Math.Max(0, seconds) * 10_000_000));
            }

            IMFSample? preceding = null;
            try
            {
                // An exact seek starts at a keyframe. Some sources need several hundred
                // decoded samples to reach the requested presentation time.
                var limit = sought && allowLongSeek ? 4096 : 120;
                for (var attempt = 0; attempt < limit; attempt++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    IMFSample? sample;
                    SourceReaderFlag flags;
                    long timestamp;
                    if (_exactPendingSample != null)
                    {
                        sample = _exactPendingSample;
                        timestamp = _exactPendingTimestamp;
                        flags = SourceReaderFlag.None;
                        _exactPendingSample = null;
                    }
                    else sample = _exactReader.ReadSample(SourceReaderIndex.FirstVideoStream,
                        SourceReaderControlFlag.None, out _, out flags, out timestamp);
                    if (sample == null)
                    {
                        if (!flags.HasFlag(SourceReaderFlag.EndOfStream)) continue;
                        if (preceding != null) UploadExactSample(preceding);
                        else if (_frameGeneration == 0 || sought) return false;
                        _exactDecodedTime = seconds;
                        DrawExactOutput(seconds, releaseKey);
                        return true;
                    }
                    try
                    {
                        var relativeTime = timestamp / 10_000_000.0 - _exactTimestampOffset;
                        if (relativeTime < seconds - toleranceSeconds)
                        {
                            if (keepPrecedingSample)
                            {
                                preceding?.Dispose();
                                preceding = sample;
                                sample = null;
                            }
                            continue;
                        }

                        if (relativeTime > seconds + toleranceSeconds &&
                            (preceding != null || _frameGeneration > 0 && !sought))
                        {
                            _exactPendingSample = sample;
                            _exactPendingTimestamp = timestamp;
                            sample = null;
                            if (preceding != null) UploadExactSample(preceding);
                        }
                        else UploadExactSample(sample);

                        _exactDecodedTime = seconds;
                        DrawExactOutput(seconds, releaseKey);
                        return true;
                    }
                    finally { sample?.Dispose(); }
                }
                throw new InvalidOperationException("El decodificador no entregó el fotograma solicitado.");
            }
            finally { preceding?.Dispose(); }

        }
    }

    private void UploadExactSample(IMFSample sample)
    {
        using var buffer = sample.GetBufferByIndex(0);
        using var dxgi = buffer.QueryInterface<IMFDXGIBuffer>();
        using var decoded = new ID3D11Texture2D(dxgi.GetResource(typeof(ID3D11Texture2D).GUID));
        var inputDescription = new VideoProcessorInputViewDescription
        {
            ViewDimension = VideoProcessorInputViewDimension.Texture2D,
            Texture2D = new Texture2DVideoProcessorInputView { ArraySlice = dxgi.SubresourceIndex }
        };
        var outputDescription = new VideoProcessorOutputViewDescription
        { ViewDimension = VideoProcessorOutputViewDimension.Texture2D };
        _exactVideoDevice!.CreateVideoProcessorInputView(decoded, _exactEnumerator!,
            inputDescription, out var inputView).CheckError();
        if (_exactOutputView == null)
            _exactVideoDevice.CreateVideoProcessorOutputView(_decodeTexture!, _exactEnumerator!,
                outputDescription, out _exactOutputView).CheckError();
        using (inputView)
        {
            var stream = new VideoProcessorStream { Enable = true, InputSurface = inputView };
            _exactVideoContext!.VideoProcessorBlt(_exactProcessor!, _exactOutputView, 0, 1, [stream]).CheckError();
        }
    }

    private void DrawExactOutput(double seconds, ulong releaseKey)
    {
        GpuTextureSync.Acquire(_mutex!, 0);
        try { DrawColor(_targetView!, Width, Height); }
        finally { _mutex!.ReleaseSync(releaseKey); }
        _presentedTime = seconds;
        _frameGeneration++;
    }

    private bool RenderFrame(double seconds, bool playing, ulong releaseKey = 1)
    {
        lock (_gpuGate)
        {
            if (_disposed || _mutex == null || _decodeSurface == null) return false;
            var difference = _engine == null ? 0 : Math.Abs(_engine.CurrentTime - seconds);
            if (_engine != null && playing)
            {
                if (!_wasPlaying) _engine.Play();
                if (difference > 0.25)
                {
                    _pendingSeekSeconds = seconds;
                    _engine.CurrentTime = seconds;
                    _wasPlaying = playing;
                    return false;
                }
            }
            else if (_engine != null && !playing && (_frameGeneration == 0 ||
                     Math.Abs(_presentedTime - seconds) > 1.0 / 60.0) &&
                     (double.IsNaN(_pendingSeekSeconds) || Math.Abs(_pendingSeekSeconds - seconds) > 1.0 / 60.0))
            {
                _pendingSeekSeconds = seconds;
                _engine.CurrentTime = seconds;
                _engine.Play();
                _wasPlaying = false;
                return false;
            }
            _wasPlaying = playing;
            long frameTime = 0;
            var hasFrame = _still && _frameGeneration == 0 || _engine?.OnVideoStreamTick(out frameTime) == true;
            if (hasFrame && _engine != null && !double.IsNaN(_pendingSeekSeconds))
            {
                // MediaEngine can yield a decoded frame from before an asynchronous seek.
                // Keep the previous frame visible until the decoded presentation time catches up.
                if (Math.Abs(frameTime / 10_000_000.0 - _pendingSeekSeconds) > 0.20) return false;
                _pendingSeekSeconds = double.NaN;
            }
            if (!hasFrame && !double.IsNaN(_pendingSeekSeconds)) return false;
            if (!hasFrame && (_frameGeneration == 0 || !RenderSettingsChanged()))
                return _frameGeneration > 0;
            if (hasFrame && _engine != null) _engine.TransferVideoFrame(_decodeSurface, null,
                new Vortice.RawRect(0, 0, _sourceWidth, _sourceHeight), null);
            if (hasFrame && _engine != null && !playing) _engine.Pause();
            GpuTextureSync.Acquire(_mutex!, 0);
            try
            {
                DrawColor(_targetView!, Width, Height);
            }
            finally { _mutex!.ReleaseSync(releaseKey); }
            _presentedTime = seconds;
            RememberRenderSettings();
            _frameGeneration++;
            return true;

        }
    }

    private bool RenderSettingsChanged() =>
        ColorSettings != _renderedColor || ExposureSettings != _renderedExposure || LevelsSettings != _renderedLevels ||
        MonochromeEnabled != _renderedMonochrome || FinishingSettings != _renderedFinishing ||
        GeometrySettings != _renderedGeometry || FilterSettings != _renderedFilter || DetailSettings != _renderedDetail ||
        LutPath != _renderedLutPath || LutTetrahedral != _renderedLutTetrahedral || SamMask?.Id != _renderedSamMaskId ||
        CurrentMaskSignature() != _renderedMaskStackSignature || MaskBaseProfile != _renderedMaskBaseProfile ||
        SamMaskRefinement != _renderedMaskRefinement;

    private void RememberRenderSettings()
    {
        _renderedColor = ColorSettings;
        _renderedExposure = ExposureSettings;
        _renderedLevels = LevelsSettings;
        _renderedMonochrome = MonochromeEnabled;
        _renderedFinishing = FinishingSettings;
        _renderedGeometry = GeometrySettings;
        _renderedFilter = FilterSettings;
        _renderedDetail = DetailSettings;
        _renderedLutPath = LutPath;
        _renderedLutTetrahedral = LutTetrahedral;
        _renderedSamMaskId = SamMask?.Id;
        _renderedMaskStackSignature = CurrentMaskSignature();
        _renderedMaskBaseProfile = MaskBaseProfile;
        _renderedMaskRefinement = SamMaskRefinement;
    }

    private void DrawColor(ID3D11RenderTargetView target, int width, int height,
        GpuBlurPass? captureBlur = null, GpuBlurPass? captureGlow = null,
        bool ignoreSamMask = false, bool captureFullMasks = false)
    {
        var captureMasks = captureFullMasks || width != Width || height != Height ? new List<GpuMaskStackPass>() : null;
        try
        {
            var sam = ignoreSamMask ? null : GetSamMaskView(width, height, captureMasks);
            if (!ignoreSamMask && MaskBaseProfile is { } baseline && sam == null)
                DrawColorPass(target, width, height, baseline, null, captureBlur, captureGlow, captureMasks);
            else if (sam != null && MaskBaseProfile is { } maskedBaseline)
            {
                GpuMaskBlendPass? temporary = null;
                try
                {
                    var blend = width == Width && height == Height
                        ? _maskBlendPass ??= new GpuMaskBlendPass(_device, _context, width, height)
                        : temporary = new GpuMaskBlendPass(_device, _context, width, height);
                    DrawColorPass(blend.PlainTarget, width, height, maskedBaseline,
                        null, captureBlur, captureGlow, captureMasks);
                    DrawColorPass(blend.EffectTarget, width, height, null,
                        null, captureBlur, captureGlow, captureMasks);
                    blend.Apply(target, sam);
                }
                finally { temporary?.Dispose(); }
            }
            else DrawColorPass(target, width, height, null, sam, captureBlur, captureGlow, captureMasks);
            _context.Flush();
        }
        finally
        {
            if (captureMasks != null)
                foreach (var pass in captureMasks) pass.Dispose();
        }
    }

    private void DrawColorPass(ID3D11RenderTargetView target, int width, int height,
        GpuEffectProfile? profile, ID3D11ShaderResourceView? sam,
        GpuBlurPass? captureBlur, GpuBlurPass? captureGlow, List<GpuMaskStackPass>? captureMasks)
    {
        var filter = profile?.Filter ?? FilterSettings;
        var detail = profile?.Detail ?? DetailSettings;
        var lut = profile?.LutPath == null && profile != null ? null : GetLutTexture();
        var color = EffectConstants(profile?.Color ?? ColorSettings,
            profile?.Exposure ?? ExposureSettings, profile?.Levels ?? LevelsSettings,
            profile?.Monochrome ?? MonochromeEnabled,
            profile?.Finishing ?? FinishingSettings, profile?.Geometry ?? GeometrySettings,
            filter, detail,
            _sourceWidth, _sourceHeight, width, height, lut?.Data,
            profile?.LutTetrahedral ?? LutTetrahedral,
            _displayRotation, _hlgInput, sam != null, _matchPreviewHlg);
        var groupViews = profile == null && sam == null ? GetMaskGroupViews(width, height, captureMasks) : null;
        if (groupViews != null)
            for (var stage = 0; stage < GpuMaskStages.Count; stage++)
                if (groupViews[stage] != null)
                {
                    var index = 18 + stage / 4;
                    var value = color[index];
                    if (stage % 4 == 0) value.X = 1;
                    else if (stage % 4 == 1) value.Y = 1;
                    else if (stage % 4 == 2) value.Z = 1;
                    else value.W = 1;
                    color[index] = value;
                }
        var source = _decodeView!;
        if (filter.BlurRadius >= 0.5)
        {
            var blur = captureBlur ?? (_blurPass ??= new GpuBlurPass(_device, _context, _sourceWidth, _sourceHeight));
            source = blur.Apply(source, filter.BlurRadius);
        }
        var glow = _decodeView!;
        if (detail.GlowAmount > 0.001)
        {
            var pass = captureGlow ?? (_glowPass ??= new GpuBlurPass(_device, _context, _sourceWidth, _sourceHeight));
            glow = pass.Apply(_decodeView!, detail.GlowRadius, brightOnly: true);
        }
        _context.UpdateSubresource(color, _colorBuffer!);
        _context.OMSetRenderTargets(target);
        _context.RSSetViewport(0, 0, width, height);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader!);
        _context.PSSetShader(_pixelShader!);
        var resources = new ID3D11ShaderResourceView[21];
        resources[0] = source; resources[1] = glow; resources[2] = lut?.View!;
        resources[3] = sam!;
        if (groupViews != null)
            for (var stage = 0; stage < GpuMaskStages.Count; stage++)
                resources[4 + stage] = groupViews[stage]!;
        resources[20] = _decodeView!;
        _context.PSSetShaderResources(0, resources);
        _context.PSSetSamplers(0, [_sampler!]);
        _context.PSSetConstantBuffers(0, [_colorBuffer!]);
        _context.Draw(3, 0);
        _context.PSSetShaderResources(0, new ID3D11ShaderResourceView[21]);
        _context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
    }

    private readonly record struct VideoMetadata(int Rotation, bool Hlg, bool SdrBt601, bool Alpha,
        int Width, int Height, double? Fps, bool Vp9Alpha, bool MpegTransportStream,
        string? MovAlphaDecoder, bool Premultiplied);

    private static VideoMetadata ReadVideoMetadata(string path)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(LocalMediaService.Tool("ffprobe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }
        };
        foreach (var arg in new[] { "-v", "error", "-select_streams", "V:0", "-show_entries",
            "stream=codec_name,width,height,pix_fmt,alpha_mode,avg_frame_rate,r_frame_rate,color_space,color_transfer,color_primaries:stream_side_data=rotation:stream_tags=rotate,alpha_mode:format=format_name",
            "-of", "json", path })
            process.StartInfo.ArgumentList.Add(arg);
        var result = ExternalProcess.RunAsync(process.StartInfo, TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        if (result.ExitCode != 0)
            throw new InvalidOperationException("No se pudo leer la orientación del vídeo. " + result.Error.Trim());
        var output = result.Output;
        using var json = JsonDocument.Parse(output);
        var streams = json.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() == 0) return default;
        var stream = streams[0];
        var hlg = stream.TryGetProperty("color_transfer", out var transfer) &&
            transfer.GetString() == "arib-std-b67" &&
            stream.TryGetProperty("color_primaries", out var primaries) &&
            primaries.GetString() == "bt2020";
        var matrix = stream.TryGetProperty("color_space", out var space) ? space.GetString() : null;
        var width = stream.TryGetProperty("width", out var sourceWidth) ? sourceWidth.GetInt32() : 0;
        var height = stream.TryGetProperty("height", out var sourceHeight) ? sourceHeight.GetInt32() : 0;
        var pixelFormat = stream.TryGetProperty("pix_fmt", out var format) ? format.GetString() ?? "" : "";
        var codecName = stream.TryGetProperty("codec_name", out var codec) ? codec.GetString() : null;
        var mpegTransportStream = json.RootElement.TryGetProperty("format", out var container) &&
            container.TryGetProperty("format_name", out var containerName) && containerName.GetString() == "mpegts";
        var vp9Alpha = codecName == "vp9" &&
            stream.TryGetProperty("tags", out var videoTags) && videoTags.TryGetProperty("alpha_mode", out var alphaMode) &&
            alphaMode.GetString() == "1";
        var alpha = vp9Alpha || pixelFormat.StartsWith("yuva", StringComparison.Ordinal) ||
            pixelFormat.StartsWith("gbrap", StringComparison.Ordinal) ||
            pixelFormat is "rgba" or "bgra" or "argb" or "abgr" or "ya8" or "ya16le";
        var movAlphaDecoder = alpha && (codecName is "prores" or "qtrle") &&
            Path.GetExtension(path).Equals(".mov", StringComparison.OrdinalIgnoreCase) ? codecName : null;
        var premultiplied = stream.TryGetProperty("alpha_mode", out var association) &&
            association.GetString() == "premultiplied";
        var fps = LocalMediaService.ReadVideoRate(stream)?.Seconds;
        var sdrBt601 = matrix is "smpte170m" or "bt470bg" or "fcc" ||
            (matrix is null or "unknown" && Math.Max(width, height) < 720);
        double degrees = 0;
        if (stream.TryGetProperty("side_data_list", out var sideData))
            foreach (var entry in sideData.EnumerateArray())
                if (entry.TryGetProperty("rotation", out var rotation) && rotation.TryGetDouble(out degrees)) break;
        if (degrees == 0 && stream.TryGetProperty("tags", out var tags) &&
            tags.TryGetProperty("rotate", out var tag))
            double.TryParse(tag.GetString(), System.Globalization.CultureInfo.InvariantCulture, out degrees);
        var normalized = ((degrees % 360) + 360) % 360;
        var quarterTurns = (int)Math.Round(normalized / 90) % 4;
        var displayRotation = Math.Abs(normalized - quarterTurns * 90) > 1 ? 0 : quarterTurns * 90;
        return new(displayRotation, hlg, sdrBt601, alpha, width, height, fps, vp9Alpha,
            mpegTransportStream, movAlphaDecoder, premultiplied);
    }

    private GpuLutTexture? GetLutTexture()
    {
        if (_loadedLutPath == LutPath) return _lutTexture;
        _lutTexture?.Dispose();
        _lutTexture = null;
        _loadedLutPath = LutPath;
        if (string.IsNullOrWhiteSpace(LutPath)) return null;
        _lutTexture = new GpuLutTexture(_device, _context, GpuLutEffect.ParseCube(LutPath));
        return _lutTexture;
    }

    private GpuSamMaskTexture? GetSamMaskTexture()
    {
        var id = SamMask?.Id;
        if (_loadedSamMaskId == id) return _samMaskTexture;
        _samMaskTexture?.Dispose();
        _samMaskTexture = null;
        _loadedSamMaskId = id;
        _refinedMaskId = null;
        if (SamMask != null) _samMaskTexture = new GpuSamMaskTexture(_device, _context, SamMask);
        return _samMaskTexture;
    }

    private ID3D11ShaderResourceView? GetSamMaskView(int width, int height, List<GpuMaskStackPass>? captureMasks)
    {
        if (MaskStack != null)
        {
            if (MaskStack.Count == 0) return null;
            var pass = captureMasks == null
                ? _maskStackPass ??= new GpuMaskStackPass(_device, _context, Width, Height)
                : new GpuMaskStackPass(_device, _context, width, height);
            captureMasks?.Add(pass);
            return pass.Apply(MaskStack);
        }
        if (captureMasks != null && LegacyShapeMask is { } shape)
        {
            var pass = new GpuMaskStackPass(_device, _context, width, height);
            captureMasks.Add(pass);
            return pass.Apply([new GpuMaskLayerFrame(shape.Id, null, SamMaskRefinement, "add", shape)]);
        }
        var mask = GetSamMaskTexture();
        if (mask == null) return null;
        var settings = SamMaskRefinement.Clamped();
        if (settings == SamMaskRefinement.Neutral) return mask.View;
        if (_samRefinePass == null || _refineWidth != mask.Width || _refineHeight != mask.Height)
        {
            _samRefinePass?.Dispose();
            _samRefinePass = new GpuMaskRefinePass(_device, _context, mask.Width, mask.Height);
            _refineWidth = mask.Width;
            _refineHeight = mask.Height;
            _refinedMaskId = null;
        }
        if (_refinedMaskId != SamMask!.Id || _refinedMaskSettings != settings)
        {
            _refinedMaskView = _samRefinePass.Apply(mask.View, settings);
            _refinedMaskId = SamMask.Id;
            _refinedMaskSettings = settings;
        }
        return _refinedMaskView;
    }

    private string CurrentMaskSignature() => MaskGroups == null
        ? GpuMaskStackPass.SignatureOf(MaskStack)
        : string.Join(";", MaskGroups.OrderBy(pair => pair.Key).Select(pair =>
            pair.Key + "=" + GpuMaskStackPass.SignatureOf(pair.Value)));

    private ID3D11ShaderResourceView?[]? GetMaskGroupViews(int width, int height, List<GpuMaskStackPass>? captureMasks)
    {
        if (MaskGroups == null) return null;
        var views = new ID3D11ShaderResourceView?[GpuMaskStages.Count];
        foreach (var stage in _maskGroupPasses.Keys.Where(stage => captureMasks == null && !MaskGroups.ContainsKey(stage)).ToArray())
        {
            _maskGroupPasses[stage].Dispose();
            _maskGroupPasses.Remove(stage);
        }
        foreach (var (stage, frames) in MaskGroups)
        {
            if (stage < 0 || stage >= GpuMaskStages.Count || frames.Count == 0) continue;
            GpuMaskStackPass pass;
            if (captureMasks != null)
            {
                pass = new GpuMaskStackPass(_device, _context, width, height);
                captureMasks.Add(pass);
            }
            else if (!_maskGroupPasses.TryGetValue(stage, out pass!))
                _maskGroupPasses[stage] = pass = new GpuMaskStackPass(_device, _context, Width, Height);
            views[stage] = pass.Apply(frames);
        }
        return views;
    }

    /// <summary>Applies the same GPU color shader at source resolution for a one-time PNG capture.</summary>
    public SKBitmap ReadbackBitmap()
    {
        lock (_gpuGate)
        {
            if (_disposed || _frameGeneration == 0 || _decodeTexture == null)
                throw new InvalidOperationException("Aún no hay un fotograma para capturar.");
            var captureWidth = _displayRotation is 90 or 270 ? _sourceHeight : _sourceWidth;
            var captureHeight = _displayRotation is 90 or 270 ? _sourceWidth : _sourceHeight;
            using var capture = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
                (uint)captureWidth, (uint)captureHeight, 1, 1, BindFlags.RenderTarget));
            using var target = _device.CreateRenderTargetView(capture);
            using var staging = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
                (uint)captureWidth, (uint)captureHeight, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
            using var captureBlur = FilterSettings.BlurRadius >= 0.5
                ? new GpuBlurPass(_device, _context, _sourceWidth, _sourceHeight) : null;
            using var captureGlow = DetailSettings.GlowAmount > 0.001
                ? new GpuBlurPass(_device, _context, _sourceWidth, _sourceHeight) : null;
            DrawColor(target, captureWidth, captureHeight, captureBlur, captureGlow, captureFullMasks: true);
            _context.CopyResource(staging, capture);
            _context.Flush();
            var bitmap = new SKBitmap(new SKImageInfo(captureWidth, captureHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
            var mapped = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                var row = new byte[captureWidth * 4];
                for (var y = 0; y < captureHeight; y++)
                {
                    Marshal.Copy(IntPtr.Add(mapped.DataPointer, y * (int)mapped.RowPitch), row, 0, row.Length);
                    Marshal.Copy(row, 0, IntPtr.Add(bitmap.GetPixels(), y * bitmap.RowBytes), row.Length);
                }
            }
            finally { _context.Unmap(staging, 0); }
            return bitmap;

        }
    }

    /// <summary>Downscales the visible source on the GPU for an in-memory SAM frame.</summary>
    public (int Width, int Height, byte[] Bgra) CaptureAnalysisFrame(int maxLongEdge = 1024)
    {
        lock (_gpuGate)
        {
            if (_disposed || _frameGeneration == 0)
                throw new InvalidOperationException("El fotograma aún no está listo para SAM.");
            var displayWidth = _displayRotation is 90 or 270 ? _sourceHeight : _sourceWidth;
            var displayHeight = _displayRotation is 90 or 270 ? _sourceWidth : _sourceHeight;
            var scale = Math.Min(1, (double)Math.Clamp(maxLongEdge, 128, 2048) /
                Math.Max(displayWidth, displayHeight));
            var width = Math.Max(1, (int)Math.Round(displayWidth * scale));
            var height = Math.Max(1, (int)Math.Round(displayHeight * scale));
            using var capture = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
                (uint)width, (uint)height, 1, 1, BindFlags.RenderTarget));
            using var target = _device.CreateRenderTargetView(capture);
            using var staging = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
                (uint)width, (uint)height, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
            DrawColor(target, width, height, ignoreSamMask: true);
            _context.CopyResource(staging, capture);
            _context.Flush();
            var bytes = new byte[checked(width * height * 4)];
            var mapped = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                for (var y = 0; y < height; y++)
                    Marshal.Copy(IntPtr.Add(mapped.DataPointer, y * (int)mapped.RowPitch), bytes,
                        y * width * 4, width * 4);
            }
            finally { _context.Unmap(staging, 0); }
            return (width, height, bytes);

        }
    }

    /// <summary>Reads one unprocessed source pixel for the chroma eyedropper.</summary>
    public int SourceWidth => _sourceWidth;
    public int SourceHeight => _sourceHeight;

    public SKColor ReadSourcePixel(int x, int y, int sampleSize = 1)
    {
        lock (_gpuGate)
        {
            if (_disposed || _frameGeneration == 0 || _decodeTexture == null)
                throw new InvalidOperationException("El fotograma aún no está listo.");
            x = Math.Clamp(x, 0, _sourceWidth - 1);
            y = Math.Clamp(y, 0, _sourceHeight - 1);
            var width = Math.Min(Math.Max(1, sampleSize), _sourceWidth);
            var height = Math.Min(Math.Max(1, sampleSize), _sourceHeight);
            var left = Math.Clamp(x - width / 2, 0, _sourceWidth - width);
            var top = Math.Clamp(y - height / 2, 0, _sourceHeight - height);
            using var staging = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
                (uint)width, (uint)height, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
            _context.CopySubresourceRegion(staging, 0, 0, 0, 0, _decodeTexture, 0,
                new Vortice.Mathematics.Box(left, top, 0, left + width, top + height, 1));
            _context.Flush();
            var mapped = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                var red = 0; var green = 0; var blue = 0;
                for (var row = 0; row < height; row++)
                    for (var column = 0; column < width; column++)
                    {
                        var address = IntPtr.Add(mapped.DataPointer, row * (int)mapped.RowPitch + column * 4);
                        blue += System.Runtime.InteropServices.Marshal.ReadByte(address);
                        green += System.Runtime.InteropServices.Marshal.ReadByte(address, 1);
                        red += System.Runtime.InteropServices.Marshal.ReadByte(address, 2);
                    }
                var count = width * height;
                return new SKColor((byte)(red / count), (byte)(green / count), (byte)(blue / count));
            }
            finally { _context.Unmap(staging, 0); }

        }
    }

    public void Dispose()
    {
        lock (_gpuGate)
        {
            if (_disposed) return;
            _disposed = true;
            DetachDrawingSurface();
            _resource?.Dispose(); _dxgiSurface?.Dispose(); _mutex?.Dispose(); _texture?.Dispose();
            _decodeSurface?.Dispose(); _decodeView?.Dispose(); _decodeTexture?.Dispose(); _targetView?.Dispose();
            _vertexShader?.Dispose(); _pixelShader?.Dispose(); _sampler?.Dispose(); _colorBuffer?.Dispose();
            _blurPass?.Dispose();
            _glowPass?.Dispose();
            _lutTexture?.Dispose();
            _samMaskTexture?.Dispose();
            _maskBlendPass?.Dispose();
            _maskStackPass?.Dispose();
            foreach (var pass in _maskGroupPasses.Values) pass.Dispose();
            _samRefinePass?.Dispose();
            _softwareReader?.Dispose();
            _exactPendingSample?.Dispose();
            _exactOutputView?.Dispose();
            _exactProcessor?.Dispose(); _exactEnumerator?.Dispose();
            _exactVideoContext1?.Dispose(); _exactVideoContext?.Dispose(); _exactVideoDevice?.Dispose();
            ReleaseMediaFoundation();
            _context.Dispose();
            _device.Dispose();
            _ready.Dispose();

        }
    }

    private static void OrientImage(SKCanvas canvas, SKEncodedOrigin origin, int width, int height)
    {
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Translate(width, 0); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.BottomRight:
                canvas.Translate(width, height); canvas.RotateDegrees(180); break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Translate(0, height); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.LeftTop:
                canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(height, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(height, width); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, width); canvas.RotateDegrees(-90); break;
        }
    }
}
