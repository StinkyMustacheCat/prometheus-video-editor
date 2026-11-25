using System;
using System.Numerics;
using Prometheus.Core.Model;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Prometheus.Client.Services;

/// <summary>Expands, contracts, inverts and softens a SAM mask at mask resolution on the GPU.</summary>
internal sealed class GpuMaskRefinePass : IDisposable
{
    private const string Shader = """
        Texture2D maskTexture : register(t0);
        SamplerState linearSampler : register(s0);
        cbuffer Params : register(b0) {
            float2 texelSize; float expandPixels; float inverted;
        };
        struct Output { float4 position : SV_Position; float2 uv : TEXCOORD0; };
        Output VSMain(uint id : SV_VertexID) {
            Output o;
            o.uv = float2((id << 1) & 2, id & 2);
            o.position = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }
        float4 PSMain(Output input) : SV_Target {
            float center = maskTexture.Sample(linearSampler, input.uv).r;
            float value = center;
            if (abs(expandPixels) > 0.01) {
                float radius = abs(expandPixels) * 0.25;
                [unroll] for (int y = -4; y <= 4; y++) {
                    [unroll] for (int x = -4; x <= 4; x++) {
                        if (x * x + y * y > 16) continue;
                        float2 uv = input.uv + float2(x, y) * radius * texelSize;
                        float sampleValue = maskTexture.Sample(linearSampler, uv).r;
                        value = expandPixels > 0 ? max(value, sampleValue) : min(value, sampleValue);
                    }
                }
            }
            if (inverted > 0.5) value = 1.0 - value;
            return float4(value, value, value, 1.0);
        }
        """;

    private static readonly Lazy<(byte[] Vertex, byte[] Pixel)> Bytecode = new(() => (
        Compiler.Compile(Shader, "VSMain", "SamRefine", "vs_5_0",
            ShaderFlags.None, EffectFlags.None).ToArray(),
        Compiler.Compile(Shader, "PSMain", "SamRefine", "ps_5_0",
            ShaderFlags.None, EffectFlags.None).ToArray()));

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Texture2D _texture;
    private readonly ID3D11RenderTargetView _target;
    private readonly ID3D11ShaderResourceView _view;
    private readonly ID3D11VertexShader _vertex;
    private readonly ID3D11PixelShader _pixel;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11Buffer _constants;
    private readonly int _width;
    private readonly int _height;
    private GpuBlurPass? _feather;

    public GpuMaskRefinePass(ID3D11Device device, ID3D11DeviceContext context,
        int width, int height)
    {
        _device = device;
        _context = context;
        _width = width;
        _height = height;
        _texture = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
            (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource));
        _target = device.CreateRenderTargetView(_texture);
        _view = device.CreateShaderResourceView(_texture);
        _vertex = device.CreateVertexShader(Bytecode.Value.Vertex);
        _pixel = device.CreatePixelShader(Bytecode.Value.Pixel);
        _sampler = device.CreateSamplerState(SamplerDescription.LinearClamp);
        _constants = device.CreateBuffer(16, BindFlags.ConstantBuffer);
    }

    public ID3D11ShaderResourceView Apply(ID3D11ShaderResourceView input,
        SamMaskRefinement settings)
    {
        var adjusted = settings.Clamped();
        _context.UpdateSubresource([new Vector4(1f / _width, 1f / _height,
            (float)adjusted.ExpandPixels, adjusted.Inverted ? 1 : 0)], _constants);
        _context.OMSetRenderTargets(_target);
        _context.RSSetViewport(0, 0, _width, _height);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertex);
        _context.PSSetShader(_pixel);
        _context.PSSetShaderResources(0, [input]);
        _context.PSSetSamplers(0, [_sampler]);
        _context.PSSetConstantBuffers(0, [_constants]);
        _context.Draw(3, 0);
        _context.PSSetShaderResources(0, [null!]);
        _context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
        if (adjusted.FeatherPixels < 0.5) return _view;
        _feather ??= new GpuBlurPass(_device, _context, _width, _height);
        return _feather.Apply(_view, adjusted.FeatherPixels);
    }

    public void Dispose()
    {
        _feather?.Dispose();
        _constants.Dispose(); _sampler.Dispose(); _pixel.Dispose(); _vertex.Dispose();
        _view.Dispose(); _target.Dispose(); _texture.Dispose();
    }
}
