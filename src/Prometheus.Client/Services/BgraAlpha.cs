using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Prometheus.Client.Services;

internal static class BgraAlpha
{
    internal static void Premultiply(Span<byte> pixels)
    {
        var offset = 0;
        if (Ssse3.IsSupported)
        {
            var alphaPositions = Vector128.Create((byte)3, 3, 3, 3, 7, 7, 7, 7,
                11, 11, 11, 11, 15, 15, 15, 15);
            var alphaMask = Vector128.Create(0xff000000u).AsByte();
            var zero = Vector128<byte>.Zero;
            ref var first = ref MemoryMarshal.GetReference(pixels);
            for (; offset <= pixels.Length - Vector128<byte>.Count; offset += Vector128<byte>.Count)
            {
                var bgra = Vector128.LoadUnsafe(ref first, (nuint)offset);
                var alpha = Ssse3.Shuffle(bgra, alphaPositions);
                var low = Multiply(Sse2.UnpackLow(bgra, zero), Sse2.UnpackLow(alpha, zero));
                var high = Multiply(Sse2.UnpackHigh(bgra, zero), Sse2.UnpackHigh(alpha, zero));
                var rgb = Sse2.PackUnsignedSaturate(low.AsInt16(), high.AsInt16());
                Sse2.Or(Sse2.And(bgra, alphaMask), Sse2.AndNot(alphaMask, rgb))
                    .StoreUnsafe(ref first, (nuint)offset);
            }
        }

        for (; offset < pixels.Length; offset += 4)
        {
            var alpha = pixels[offset + 3];
            pixels[offset] = (byte)((pixels[offset] * alpha + 128) >> 8);
            pixels[offset + 1] = (byte)((pixels[offset + 1] * alpha + 128) >> 8);
            pixels[offset + 2] = (byte)((pixels[offset + 2] * alpha + 128) >> 8);
        }

        // Match FFmpeg's 8-bit premultiply filter without converting to and from planar RGB.
        static Vector128<ushort> Multiply(Vector128<byte> color, Vector128<byte> alpha) =>
            Sse2.ShiftRightLogical(Sse2.Add(Sse2.MultiplyLow(color.AsUInt16(), alpha.AsUInt16()),
                Vector128.Create((ushort)128)), 8);
    }
}
