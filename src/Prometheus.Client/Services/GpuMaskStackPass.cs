using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prometheus.Core.Model;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Prometheus.Client.Services;

public sealed record GpuMaskLayerFrame(string LayerId, SamMaskKeyframe? Mask,
    SamMaskRefinement Refinement, string Blend, ShapeMask? Shape = null);

/// <summary>Combines independent mask textures on GPU without readback per video frame.</summary>
internal sealed class GpuMaskStackPass : IDisposable
{
    private const string Shader = """
        Texture2D previousMask : register(t0);
        Texture2D layerMask : register(t1);
        SamplerState linearSampler : register(s0);
        cbuffer Params : register(b0) { float mode; float first; float2 padding; };
        struct Output { float4 position : SV_Position; float2 uv : TEXCOORD0; };
        Output VSMain(uint id : SV_VertexID) {
            Output o; o.uv = float2((id << 1) & 2, id & 2);
            o.position = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }
        float4 PSMain(Output input) : SV_Target {
            float a = first > 0.5 ? 0 : previousMask.Sample(linearSampler, input.uv).r;
            float b = saturate(layerMask.Sample(linearSampler, input.uv).r);
            float result = mode < 0.5 ? max(a, b) :
                mode < 1.5 ? a * (1 - b) : a * b;
            return float4(result, result, result, 1);
        }
        """;

    private static readonly Lazy<(byte[] Vertex, byte[] Pixel)> Bytecode = new(() => (
        Compiler.Compile(Shader, "VSMain", "MaskStack", "vs_5_0",
            ShaderFlags.None, EffectFlags.None).ToArray(),
        Compiler.Compile(Shader, "PSMain", "MaskStack", "ps_5_0",
            ShaderFlags.None, EffectFlags.None).ToArray()));

    private sealed class LayerResource : IDisposable
    {
        public string MaskId = "";
        public GpuSamMaskTexture? Texture;
        public GpuShapeMaskPass? ShapePass;
        public GpuMaskRefinePass? Refine;
        public int Width;
        public int Height;
        public SamMaskRefinement Refinement;
        public ID3D11ShaderResourceView? RefinedView;

        public void Dispose() { Refine?.Dispose(); Texture?.Dispose(); ShapePass?.Dispose(); }
    }

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Texture2D[] _textures = new ID3D11Texture2D[2];
    private readonly ID3D11RenderTargetView[] _targets = new ID3D11RenderTargetView[2];
    private readonly ID3D11ShaderResourceView[] _views = new ID3D11ShaderResourceView[2];
    private readonly Dictionary<string, LayerResource> _layers = new();
    private readonly ID3D11VertexShader _vertex;
    private readonly ID3D11PixelShader _pixel;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11Buffer _constants;
    private readonly int _width;
    private readonly int _height;
    private string? _signature;
    private ID3D11ShaderResourceView? _result;

    public GpuMaskStackPass(ID3D11Device device, ID3D11DeviceContext context,
        int width, int height)
    {
        _device = device; _context = context; _width = width; _height = height;
        for (var i = 0; i < 2; i++)
        {
            _textures[i] = device.CreateTexture2D(new Texture2DDescription(
                Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
                BindFlags.RenderTarget | BindFlags.ShaderResource));
            _targets[i] = device.CreateRenderTargetView(_textures[i]);
            _views[i] = device.CreateShaderResourceView(_textures[i]);
        }
        _vertex = device.CreateVertexShader(Bytecode.Value.Vertex);
        _pixel = device.CreatePixelShader(Bytecode.Value.Pixel);
        _sampler = device.CreateSamplerState(SamplerDescription.LinearClamp);
        _constants = device.CreateBuffer(16, BindFlags.ConstantBuffer);
    }

    public static string SignatureOf(IReadOnlyList<GpuMaskLayerFrame>? frames) => frames == null
        ? "legacy" : string.Join("|", frames.Select(frame =>
            frame.LayerId + ":" + (frame.Mask?.Id ?? frame.Shape?.Id) + ":" + frame.Blend + ":" +
            frame.Refinement.FeatherPixels.ToString("R", CultureInfo.InvariantCulture) + ":" +
            frame.Refinement.ExpandPixels.ToString("R", CultureInfo.InvariantCulture) + ":" +
            frame.Refinement.Inverted));

    public ID3D11ShaderResourceView? Apply(IReadOnlyList<GpuMaskLayerFrame> frames)
    {
        if (frames.Count == 0) return null;
        var signature = SignatureOf(frames);
        if (signature == _signature) return _result;
        var active = frames.Select(frame => frame.LayerId).ToHashSet();
        foreach (var id in _layers.Keys.Where(id => !active.Contains(id)).ToArray())
        {
            _layers[id].Dispose(); _layers.Remove(id);
        }
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            if (!_layers.TryGetValue(frame.LayerId, out var resource))
                _layers[frame.LayerId] = resource = new LayerResource();
            if (frame.Mask == null && frame.Shape == null)
                throw new InvalidOperationException("La capa no contiene una máscara.");
            if (frame.Mask != null && resource.MaskId != frame.Mask.Id)
            {
                resource.Texture?.Dispose();
                resource.Texture = new GpuSamMaskTexture(_device, _context, frame.Mask);
                resource.MaskId = frame.Mask.Id;
                resource.RefinedView = null;
            }
            var maskWidth = frame.Mask?.Width ?? _width;
            var maskHeight = frame.Mask?.Height ?? _height;
            ID3D11ShaderResourceView view;
            if (frame.Shape != null)
            {
                resource.ShapePass ??= new GpuShapeMaskPass(_device, _context, _width, _height);
                view = resource.ShapePass.Apply(frame.Shape);
                if (resource.MaskId != frame.Shape.Id)
                {
                    resource.MaskId = frame.Shape.Id;
                    resource.RefinedView = null;
                }
            }
            else view = resource.Texture!.View;
            var refinement = frame.Refinement.Clamped();
            if (refinement != SamMaskRefinement.Neutral)
            {
                if (resource.Refine == null || resource.Width != maskWidth ||
                    resource.Height != maskHeight)
                {
                    resource.Refine?.Dispose();
                    resource.Refine = new GpuMaskRefinePass(_device, _context,
                        maskWidth, maskHeight);
                    resource.Width = maskWidth; resource.Height = maskHeight;
                    resource.RefinedView = null;
                }
                if (resource.RefinedView == null || resource.Refinement != refinement)
                {
                    resource.RefinedView = resource.Refine.Apply(view, refinement);
                    resource.Refinement = refinement;
                }
                view = resource.RefinedView;
            }
            var output = i % 2;
            _context.UpdateSubresource([new System.Numerics.Vector4(
                i == 0 ? 0 : frame.Blend == "subtract" ? 1 :
                    frame.Blend == "intersect" ? 2 : 0,
                i == 0 ? 1 : 0, 0, 0)], _constants);
            _context.OMSetRenderTargets(_targets[output]);
            _context.RSSetViewport(0, 0, _width, _height);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _context.VSSetShader(_vertex);
            _context.PSSetShader(_pixel);
            _context.PSSetShaderResources(0,
                [i == 0 ? null! : _views[1 - output], view]);
            _context.PSSetSamplers(0, [_sampler]);
            _context.PSSetConstantBuffers(0, [_constants]);
            _context.Draw(3, 0);
            _context.PSSetShaderResources(0, [null!, null!]);
            _context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
        }
        _signature = signature;
        _result = _views[(frames.Count - 1) % 2];
        return _result;
    }

    public void Dispose()
    {
        foreach (var layer in _layers.Values) layer.Dispose();
        _constants.Dispose(); _sampler.Dispose(); _pixel.Dispose(); _vertex.Dispose();
        for (var i = 0; i < 2; i++)
        { _views[i].Dispose(); _targets[i].Dispose(); _textures[i].Dispose(); }
    }
}
