using System;
using System.Runtime.InteropServices;
using Prometheus.Core.Model;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Prometheus.Client.Services;

internal sealed class GpuLutTexture : IDisposable
{
    private readonly ID3D11Texture3D _texture;
    public ID3D11ShaderResourceView View { get; }
    public CubeLut Data { get; }

    public GpuLutTexture(ID3D11Device device, ID3D11DeviceContext context, CubeLut data)
    {
        Data = data;
        _texture = device.CreateTexture3D(new Texture3DDescription(Format.R32G32B32A32_Float,
            (uint)data.Size, (uint)data.Size, (uint)data.Size, 1, BindFlags.ShaderResource));
        var handle = GCHandle.Alloc(data.Rgba, GCHandleType.Pinned);
        try
        {
            context.UpdateSubresource(_texture, 0, null, handle.AddrOfPinnedObject(),
                (uint)(data.Size * 16), (uint)(data.Size * data.Size * 16));
        }
        finally { handle.Free(); }
        View = device.CreateShaderResourceView(_texture);
    }

    public void Dispose() { View.Dispose(); _texture.Dispose(); }
}
