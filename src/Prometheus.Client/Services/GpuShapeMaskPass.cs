using System;
using System.Numerics;
using Prometheus.Core.Model;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Prometheus.Client.Services;

/// <summary>Rasterizes animated vector shapes on GPU at the display source resolution.</summary>
internal sealed class GpuShapeMaskPass : IDisposable
{
    private const string Shader = """
        cbuffer Shape : register(b0) {
            float kind; float count; float2 texel;
            float4 points[64];
        };
        struct Output { float4 position : SV_Position; float2 uv : TEXCOORD0; };
        Output VSMain(uint id : SV_VertexID) {
            Output o; o.uv = float2((id << 1) & 2, id & 2);
            o.position = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }
        float Contains(float2 p) {
            if (kind < 0.5) {
                float2 low = min(points[0].xy, points[1].xy);
                float2 high = max(points[0].xy, points[1].xy);
                return p.x >= low.x && p.x <= high.x && p.y >= low.y && p.y <= high.y ? 1 : 0;
            }
            if (kind < 1.5) {
                float2 low = min(points[0].xy, points[1].xy);
                float2 high = max(points[0].xy, points[1].xy);
                float2 center = (low + high) * 0.5;
                float2 radius = max((high - low) * 0.5, float2(0.00001, 0.00001));
                float2 q = (p - center) / radius;
                return dot(q, q) <= 1 ? 1 : 0;
            }
            bool inside = false;
            int previous = (int)count - 1;
            [loop] for (int i = 0; i < (int)count; i++) {
                float2 a = points[i].xy;
                float2 b = points[previous].xy;
                if ((a.y > p.y) != (b.y > p.y) &&
                    p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
                previous = i;
            }
            return inside ? 1 : 0;
        }
        float4 PSMain(Output input) : SV_Target {
            float2 q = texel * 0.25;
            float coverage = (Contains(input.uv + float2(-q.x, -q.y)) +
                Contains(input.uv + float2(q.x, -q.y)) +
                Contains(input.uv + float2(-q.x, q.y)) +
                Contains(input.uv + float2(q.x, q.y))) * 0.25;
            return float4(coverage, coverage, coverage, 1);
        }
        """;

    private static readonly Lazy<(byte[] Vertex, byte[] Pixel)> Bytecode = new(() => (
        Compiler.Compile(Shader, "VSMain", "Shape", "vs_5_0",
            ShaderFlags.None, EffectFlags.None).ToArray(),
        Compiler.Compile(Shader, "PSMain", "Shape", "ps_5_0",
            ShaderFlags.None, EffectFlags.None).ToArray()));

    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Texture2D _texture;
    private readonly ID3D11RenderTargetView _target;
    private readonly ID3D11ShaderResourceView _view;
    private readonly ID3D11VertexShader _vertex;
    private readonly ID3D11PixelShader _pixel;
    private readonly ID3D11Buffer _constants;
    private readonly int _width;
    private readonly int _height;
    private string? _shapeId;

    public GpuShapeMaskPass(ID3D11Device device, ID3D11DeviceContext context,
        int width, int height)
    {
        _context = context; _width = width; _height = height;
        _texture = device.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource));
        _target = device.CreateRenderTargetView(_texture);
        _view = device.CreateShaderResourceView(_texture);
        _vertex = device.CreateVertexShader(Bytecode.Value.Vertex);
        _pixel = device.CreatePixelShader(Bytecode.Value.Pixel);
        _constants = device.CreateBuffer(65 * 16, BindFlags.ConstantBuffer);
    }

    public ID3D11ShaderResourceView Apply(ShapeMask shape)
    {
        if (_shapeId == shape.Id) return _view;
        shape.Validate();
        var values = new Vector4[65];
        values[0] = new Vector4(shape.Kind == "ellipse" ? 1 :
            shape.Kind == "polygon" ? 2 : 0, shape.Points.Count,
            1f / _width, 1f / _height);
        for (var i = 0; i < shape.Points.Count; i++)
            values[i + 1] = new Vector4((float)shape.Points[i].X,
                (float)shape.Points[i].Y, 0, 0);
        _context.UpdateSubresource(values, _constants);
        _context.OMSetRenderTargets(_target);
        _context.RSSetViewport(0, 0, _width, _height);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertex);
        _context.PSSetShader(_pixel);
        _context.PSSetConstantBuffers(0, [_constants]);
        _context.Draw(3, 0);
        _context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
        _shapeId = shape.Id;
        return _view;
    }

    public void Dispose()
    {
        _constants.Dispose(); _pixel.Dispose(); _vertex.Dispose();
        _view.Dispose(); _target.Dispose(); _texture.Dispose();
    }
}
