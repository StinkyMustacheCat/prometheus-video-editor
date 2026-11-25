using System;
using System.Numerics;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace Prometheus.Client.Services;

/// <summary>Two separable nine-tap passes; the intermediate images never leave the GPU.</summary>
internal sealed class GpuBlurPass : IDisposable
{
    private const string Shader = """
        Texture2D image : register(t0);
        SamplerState linearSampler : register(s0);
        cbuffer BlurParams : register(b0) { float2 texelStep; float radius; float brightOnly; };
        struct Output { float4 position : SV_Position; float2 uv : TEXCOORD0; };
        Output VSMain(uint id : SV_VertexID) {
            Output o;
            o.uv = float2((id << 1) & 2, id & 2);
            o.position = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }
        float4 PSMain(Output input) : SV_Target {
            float4 sum = 0;
            float weights[9] = { 0.02853, 0.06723, 0.12401, 0.17904, 0.20236,
                                 0.17904, 0.12401, 0.06723, 0.02853 };
            [unroll] for (int i = 0; i < 9; i++) {
                float4 pixel = image.Sample(linearSampler, input.uv + texelStep * (i - 4) * (radius / 4.0));
                if (brightOnly > 0.5) {
                    float luma = dot(pixel.rgb, float3(0.2126, 0.7152, 0.0722));
                    pixel *= smoothstep(0.45, 0.95, luma);
                }
                sum += pixel * weights[i];
            }
            return sum;
        }
        """;

    private static readonly Lazy<(byte[] Vertex, byte[] Pixel)> Bytecode = new(() => (
        Compiler.Compile(Shader, "VSMain", "GaussianBlur", "vs_5_0", ShaderFlags.None, EffectFlags.None).ToArray(),
        Compiler.Compile(Shader, "PSMain", "GaussianBlur", "ps_5_0", ShaderFlags.None, EffectFlags.None).ToArray()));

    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Texture2D _horizontal;
    private readonly ID3D11Texture2D _vertical;
    private readonly ID3D11RenderTargetView _horizontalTarget;
    private readonly ID3D11RenderTargetView _verticalTarget;
    private readonly ID3D11ShaderResourceView _horizontalView;
    private readonly ID3D11ShaderResourceView _verticalView;
    private readonly ID3D11VertexShader _vertex;
    private readonly ID3D11PixelShader _pixel;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11Buffer _constants;
    private readonly int _width;
    private readonly int _height;

    public GpuBlurPass(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
    {
        _context = context;
        _width = width;
        _height = height;
        ID3D11Texture2D CreateTexture() => device.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource));
        _horizontal = CreateTexture();
        _vertical = CreateTexture();
        _horizontalTarget = device.CreateRenderTargetView(_horizontal);
        _verticalTarget = device.CreateRenderTargetView(_vertical);
        _horizontalView = device.CreateShaderResourceView(_horizontal);
        _verticalView = device.CreateShaderResourceView(_vertical);
        _vertex = device.CreateVertexShader(Bytecode.Value.Vertex);
        _pixel = device.CreatePixelShader(Bytecode.Value.Pixel);
        _sampler = device.CreateSamplerState(SamplerDescription.LinearClamp);
        _constants = device.CreateBuffer(16, BindFlags.ConstantBuffer);
    }

    public ID3D11ShaderResourceView Apply(ID3D11ShaderResourceView source, double radius,
        bool brightOnly = false)
    {
        Pass(source, _horizontalTarget, new Vector2(1f / _width, 0), radius, brightOnly);
        Pass(_horizontalView, _verticalTarget, new Vector2(0, 1f / _height), radius, false);
        return _verticalView;
    }

    private void Pass(ID3D11ShaderResourceView source, ID3D11RenderTargetView target,
        Vector2 step, double radius, bool brightOnly)
    {
        _context.UpdateSubresource([new Vector4(step, (float)Math.Clamp(radius, 0, 30), brightOnly ? 1 : 0)], _constants);
        _context.OMSetRenderTargets(target);
        _context.RSSetViewport(0, 0, _width, _height);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertex);
        _context.PSSetShader(_pixel);
        _context.PSSetShaderResources(0, [source]);
        _context.PSSetSamplers(0, [_sampler]);
        _context.PSSetConstantBuffers(0, [_constants]);
        _context.Draw(3, 0);
        _context.PSSetShaderResources(0, [null!]);
        _context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
    }

    public void Dispose()
    {
        _constants.Dispose(); _sampler.Dispose(); _pixel.Dispose(); _vertex.Dispose();
        _verticalView.Dispose(); _horizontalView.Dispose();
        _verticalTarget.Dispose(); _horizontalTarget.Dispose();
        _vertical.Dispose(); _horizontal.Dispose();
    }
}
