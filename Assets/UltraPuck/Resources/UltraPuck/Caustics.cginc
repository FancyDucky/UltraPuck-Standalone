// Shared by Sand.shader and Caustics.shader: noise, and the moving light through the water (caustics).
#ifndef ULTRAPUCK_CAUSTICS
#define ULTRAPUCK_CAUSTICS
float2 Hash2(float2 p)
{
    p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return frac(sin(p) * 43758.5453);
}
float Hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

float Noise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
}

// how far a point is from the nearest edge between moving cells (0 on an edge): bright lines where it's small
float Edge(float2 p, float t)
{
    float2 i = floor(p), f = frac(p);
    float d1 = 8.0, d2 = 8.0;
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        float2 g = float2(x, y);
        float2 o = Hash2(i + g);
        o = 0.5 + 0.42 * sin(t + 6.2831 * o);
        float d = length(g + o - f);
        if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
    }
    return d2 - d1;
}


// the light at world point p (0..1): two drifting layers of cells, wobbled a little so the lines bend
float CausticLight(float2 p, float t, float scale)
{
    float2 w = float2(Noise(p * 0.3 + t * 0.2), Noise(p * 0.3 - t * 0.17 + 3.1)) - 0.5;
    float2 q = p * scale + w * 0.6;
    float a = exp(-Edge(q + float2(t * 0.05, t * 0.03), t) * 10.0);
    float b = exp(-Edge(q * 1.7 + float2(-t * 0.04, t * 0.06) + 11.0, t * 1.25) * 13.0);
    return saturate(a + 0.35 * b);
}
#endif
