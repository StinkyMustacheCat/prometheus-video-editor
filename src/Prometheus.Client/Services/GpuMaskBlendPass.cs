using System;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Prometheus.Client.Services;

/// <summary>Combines two premultiplied GPU results through one SAM alpha mask.</summary>
internal sealed class GpuMaskBlendPass : IDisposable
{
    private const string Shader = """
        Texture2D plainTexture : register(t0);
        Texture2D effectTexture : register(t1);
        Texture2D maskTexture : register(t2);
        SamplerState linearSampler : register(s0);
        struct Output { float4 position : SV_Position; float2 uv : TEXCOORD0; };
        Output VSMain(uint id : SV_VertexID) {
            Output o;
            o.uv = float2((id << 1) & 2, id & 2);
            o.position = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }
        float4 PSMain(Output input) : SV_Target {
            float strength = saturate(maskTexture.Sample(linearSampler, input.uv).r);
            return lerp(plainTexture.Sample(linearSampler, input.uv),
                        effectTexture.Sample(linearSampler, input.uv), strength);
        }
        """;

    private static readonly Lazy<(byte[] Vertex, byte[] Pixel)> Bytecode = new(() => (
        Compiler.Compile(Shader, "VSMain", "SamEffectBlend", "vs_5_0",
            ShaderFlags.None, EffectFlags.None).ToArray(),
        Compiler.Compile(Shader, "PSMain", "SamEffectBlend", "ps_5_0",
            ShaderFlags.None, EffectFlags.None).ToArray()));

    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Texture2D _plain;
    private readonly ID3D11Texture2D _effect;
    private readonly ID3D11ShaderResourceView _plainView;
    private readonly ID3D11ShaderResourceView _effectView;
    private readonly ID3D11RenderTargetView _plainTarget;
    private readonly ID3D11RenderTargetView _effectTarget;
    private readonly ID3D11VertexShader _vertex;
    private readonly ID3D11PixelShader _pixel;
    private readonly ID3D11SamplerState _sampler;
    private readonly int _width;
    private readonly int _height;

    public ID3D11RenderTargetView PlainTarget => _plainTarget;
    public ID3D11RenderTargetView EffectTarget => _effectTarget;

    public GpuMaskBlendPass(ID3D11Device device, ID3D11DeviceContext context,
        int width, int height)
    {
        _context = context;
        _width = width;
        _height = height;
        ID3D11Texture2D CreateTexture() => device.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource));
        _plain = CreateTexture();
        _effect = CreateTexture();
        _plainView = device.CreateShaderResourceView(_plain);
        _effectView = device.CreateShaderResourceView(_effect);
        _plainTarget = device.CreateRenderTargetView(_plain);
        _effectTarget = device.CreateRenderTargetView(_effect);
        _vertex = device.CreateVertexShader(Bytecode.Value.Vertex);
        _pixel = device.CreatePixelShader(Bytecode.Value.Pixel);
        _sampler = device.CreateSamplerState(SamplerDescription.LinearClamp);
    }

    public void Apply(ID3D11RenderTargetView target, ID3D11ShaderResourceView mask)
    {
        _context.OMSetRenderTargets(target);
        _context.RSSetViewport(0, 0, _width, _height);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertex);
        _context.PSSetShader(_pixel);
        _context.PSSetShaderResources(0, [_plainView, _effectView, mask]);
        _context.PSSetSamplers(0, [_sampler]);
        _context.Draw(3, 0);
        _context.PSSetShaderResources(0, [null!, null!, null!]);
        _context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
    }

    public void Dispose()
    {
        _sampler.Dispose(); _pixel.Dispose(); _vertex.Dispose();
        _effectTarget.Dispose(); _plainTarget.Dispose();
        _effectView.Dispose(); _plainView.Dispose();
        _effect.Dispose(); _plain.Dispose();
    }
}
