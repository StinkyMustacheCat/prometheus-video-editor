namespace Prometheus.Client.Services;

internal static class GpuBlendShader
{
    // W3C Compositing and Blending Level 1, plus clipped addition/subtraction.
    // Inputs and output of BlendOver are premultiplied; blend functions use straight RGB.
    internal const string Functions = """
        float Lum(float3 c) { return dot(c, float3(.3, .59, .11)); }
        float Sat(float3 c) { return max(c.r, max(c.g,c.b)) - min(c.r,min(c.g,c.b)); }
        float3 ClipColor(float3 c) {
            float l = Lum(c), n = min(c.r,min(c.g,c.b)), x = max(c.r,max(c.g,c.b));
            if (n < 0) c = l + (c-l) * l / max(l-n, 1e-6);
            if (x > 1) c = l + (c-l) * (1-l) / max(x-l, 1e-6);
            return c;
        }
        float3 SetLum(float3 c, float l) { return ClipColor(c + l - Lum(c)); }
        float3 SetSat(float3 c, float s) {
            float low = min(c.r,min(c.g,c.b)), high = max(c.r,max(c.g,c.b));
            return high > low ? (c-low) * s / (high-low) : float3(0,0,0);
        }
        float Dodge(float b,float s) { return b <= 0 ? 0 : s >= 1 ? 1 : min(1,b/(1-s)); }
        float Burn(float b,float s) { return b >= 1 ? 1 : s <= 0 ? 0 : 1-min(1,(1-b)/s); }
        float Hard(float b,float s) { return s <= .5 ? 2*b*s : 1-2*(1-b)*(1-s); }
        float Soft(float b,float s) {
            float d = b <= .25 ? ((16*b-12)*b+4)*b : sqrt(b);
            return s <= .5 ? b-(1-2*s)*b*(1-b) : b+(2*s-1)*(d-b);
        }
        float3 BlendColor(float3 b,float3 s,int mode) {
            if (mode == 1) return min(b,s);
            if (mode == 2) return b*s;
            if (mode == 3) return float3(Burn(b.r,s.r),Burn(b.g,s.g),Burn(b.b,s.b));
            if (mode == 4) return max(b,s);
            if (mode == 5) return b+s-b*s;
            if (mode == 6) return float3(Dodge(b.r,s.r),Dodge(b.g,s.g),Dodge(b.b,s.b));
            if (mode == 7) return min(1,b+s);
            if (mode == 8) return float3(Hard(s.r,b.r),Hard(s.g,b.g),Hard(s.b,b.b));
            if (mode == 9) return float3(Soft(b.r,s.r),Soft(b.g,s.g),Soft(b.b,s.b));
            if (mode == 10) return float3(Hard(b.r,s.r),Hard(b.g,s.g),Hard(b.b,s.b));
            if (mode == 11) return abs(b-s);
            if (mode == 12) return b+s-2*b*s;
            if (mode == 13) return max(0,b-s);
            if (mode == 14) return SetLum(SetSat(s,Sat(b)),Lum(b));
            if (mode == 15) return SetLum(SetSat(b,Sat(s)),Lum(b));
            if (mode == 16) return SetLum(s,Lum(b));
            if (mode == 17) return SetLum(b,Lum(s));
            return s;
        }
        float4 BlendOver(float4 b,float4 s,int mode) {
            float3 cb = saturate(b.rgb/max(b.a,1e-6));
            float3 cs = saturate(s.rgb/max(s.a,1e-6));
            return float4((1-s.a)*b.rgb+(1-b.a)*s.rgb+s.a*b.a*BlendColor(cb,cs,mode),
                s.a+b.a*(1-s.a));
        }
        """;
}
