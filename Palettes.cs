using System.Numerics;

namespace Makarov.Tree3D;

// Colors of one look: wood from trunk to twig, the canopy glow and the
// background gradient. `Drifting` palettes recolor themselves over time.
internal readonly record struct Palette(
    string Name, Vector3 Trunk, Vector3 Mid, Vector3 Tip, Vector3 Glow, Vector3 BgInner, Vector3 BgOuter,
    bool Drifting = false)
{
    // The palette as it looks at time t (seconds).
    public Palette At(float t)
    {
        if (!Drifting) return this;
        // Hue drifts slowly around the wheel; the trunk stays dark and neutral.
        float hue = t * 0.02f;
        hue -= MathF.Floor(hue);
        return this with
        {
            Mid = Hsv(hue, 0.75f, 0.60f),
            Tip = Hsv((hue + 0.10f) % 1f, 0.55f, 1.00f),
            Glow = Hsv((hue + 0.08f) % 1f, 0.35f, 1.00f),
        };
    }

    private static Vector3 Hsv(float h, float s, float v)
    {
        float r = MathF.Abs(h * 6f - 3f) - 1f;
        float g = 2f - MathF.Abs(h * 6f - 2f);
        float b = 2f - MathF.Abs(h * 6f - 4f);
        var rgb = Vector3.Clamp(new Vector3(r, g, b), Vector3.Zero, Vector3.One);
        return Vector3.Lerp(Vector3.One, rgb, s) * v;
    }
}

internal static class Palettes
{
    public static readonly Palette[] All =
    [
        new("Sakura",
            new(0.32f, 0.17f, 0.10f), new(0.55f, 0.20f, 0.62f), new(1.00f, 0.45f, 0.92f),
            new(1.00f, 0.70f, 0.95f), new(0.035f, 0.040f, 0.075f), new(0.005f, 0.005f, 0.010f)),
        new("Autumn",
            new(0.26f, 0.14f, 0.07f), new(0.72f, 0.28f, 0.06f), new(1.00f, 0.62f, 0.15f),
            new(1.00f, 0.76f, 0.38f), new(0.060f, 0.038f, 0.022f), new(0.008f, 0.005f, 0.003f)),
        new("Bioluminescent",
            new(0.05f, 0.11f, 0.13f), new(0.00f, 0.46f, 0.52f), new(0.25f, 1.00f, 0.90f),
            new(0.55f, 1.00f, 0.95f), new(0.012f, 0.045f, 0.055f), new(0.002f, 0.006f, 0.010f)),
        new("Ember",
            new(0.16f, 0.05f, 0.03f), new(0.78f, 0.16f, 0.04f), new(1.00f, 0.55f, 0.10f),
            new(1.00f, 0.42f, 0.16f), new(0.055f, 0.020f, 0.012f), new(0.006f, 0.002f, 0.002f)),
        new("Frost",
            new(0.12f, 0.15f, 0.22f), new(0.42f, 0.58f, 0.85f), new(0.85f, 0.95f, 1.00f),
            new(0.80f, 0.92f, 1.00f), new(0.030f, 0.045f, 0.075f), new(0.004f, 0.006f, 0.012f)),
        new("Emerald",
            new(0.22f, 0.15f, 0.08f), new(0.10f, 0.48f, 0.20f), new(0.52f, 1.00f, 0.42f),
            new(0.75f, 1.00f, 0.60f), new(0.020f, 0.050f, 0.030f), new(0.003f, 0.008f, 0.004f)),
        new("Aurora", // Mid / Tip / Glow are recomputed every frame from a drifting hue.
            new(0.10f, 0.09f, 0.13f), new(0.40f, 0.20f, 0.60f), new(0.90f, 0.60f, 1.00f),
            new(0.90f, 0.70f, 1.00f), new(0.025f, 0.030f, 0.055f), new(0.004f, 0.004f, 0.009f),
            Drifting: true),
    ];
}
