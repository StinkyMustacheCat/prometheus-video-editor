using System;
using System.Runtime.InteropServices;
using Prometheus.Core.Model;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Prometheus.Client.Services;

internal sealed class GpuSamMaskTexture : IDisposable
{
    private readonly ID3D11Texture2D _texture;
    public ID3D11ShaderResourceView View { get; }
    public int Width { get; }
    public int Height { get; }

    public GpuSamMaskTexture(ID3D11Device device, ID3D11DeviceContext context,
        SamMaskKeyframe mask)
    {
        Width = mask.Width;
        Height = mask.Height;
        var alpha = mask.DecodeAlpha();
        _texture = device.CreateTexture2D(new Texture2DDescription(Format.R8_UNorm,
            (uint)mask.Width, (uint)mask.Height, 1, 1, BindFlags.ShaderResource));
        var pinned = GCHandle.Alloc(alpha, GCHandleType.Pinned);
        try { context.UpdateSubresource(_texture, 0, null, pinned.AddrOfPinnedObject(),
            (uint)mask.Width, 0); }
        finally { pinned.Free(); }
        View = device.CreateShaderResourceView(_texture);
    }

    public void Dispose() { View.Dispose(); _texture.Dispose(); }
}
