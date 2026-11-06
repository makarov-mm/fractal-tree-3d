// Procedural tree: a species (the shape parameters) and the geometry grown
// from it, laid out as the vertex buffers the shaders read.
using System.Numerics;
using System.Runtime.InteropServices;

namespace Makarov.Tree3D;

// The shape parameters of one tree, re-rolled for every new tree.
internal readonly record struct Species(int Children, int MaxDepth, float Ratio, float TiltDegrees, int Seed)
{
    public static Species Roll(int seed)
    {
        var rng = new Random(seed);
        int treeSeed = rng.Next();

        // 3-way forks are the classic look, 2 and 4 are rarer species.
        int children = rng.Next(5) switch { 0 => 2, 4 => 4, _ => 3 };
        // Keep the segment count in the same ballpark for any fork count.
        int maxDepth = children switch { 2 => 10, 3 => 8, _ => 7 };
        float ratio = 0.70f + (float)rng.NextDouble() * 0.09f;
        float tilt = 24f + (float)rng.NextDouble() * 24f;
        return new Species(children, maxDepth, ratio, tilt, treeSeed);
    }
}

// One end of a branch segment (two per segment, drawn as GL_LINES).
[StructLayout(LayoutKind.Sequential)]
internal struct BranchVertex
{
    public Vector3 Position;
    public float Depth; // 0 at the trunk base .. 1 at the twig ends
    public float Sway;  // how much the wind moves this vertex
    public float Birth; // normalized path length from the root: when growth reaches it
}

[StructLayout(LayoutKind.Sequential)]
internal struct TipVertex
{
    public Vector3 Position;
    public float Birth;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PetalVertex
{
    public Vector3 Spawn; // the canopy tip the petal detaches from
    public float Phase;   // where in its fall cycle it starts
    public float Seed;    // per-petal variation
}

internal sealed class Tree
{
    public const int MaxPetals = 1400;
    private const float GoldenAngle = 2.39996f;
    private const float FitExtent = 1.9f; // the tree is scaled to fit this bounding cube

    public Species Species { get; }
    public List<BranchVertex> Branches { get; } = new();
    public List<TipVertex> Tips { get; } = new();
    public List<PetalVertex> Petals { get; } = new();

    public static Tree Generate(Species species) => new(species);

    private Tree(Species species)
    {
        Species = species;
        var rng = new Random(species.Seed);
        float tilt = species.TiltDegrees * MathF.PI / 180f;
        float maxDistance = 0f;

        void Branch(Vector3 position, Vector3 direction, float length, int depth, float distance)
        {
            Vector3 end = position + direction * length;
            float endDistance = distance + length;
            maxDistance = MathF.Max(maxDistance, endDistance);
            Branches.Add(Vertex(position, depth, distance));
            Branches.Add(Vertex(end, depth + 1, endDistance));
            if (depth >= species.MaxDepth)
            {
                Tips.Add(new TipVertex { Position = end, Birth = endDistance });
                return;
            }

            // Children sit on a cone around the parent: tilted off the axis,
            // then rolled around it by a golden-angle offset per generation,
            // with a little jitter so the shape is not perfectly mechanical.
            Vector3 reference = MathF.Abs(direction.Y) > 0.99f ? Vector3.UnitX : Vector3.UnitY;
            Vector3 perpendicular = Vector3.Normalize(Vector3.Cross(direction, reference));
            for (int k = 0; k < species.Children; k++)
            {
                float tiltJitter = (float)(rng.NextDouble() - 0.5) * 0.20f;
                float rollJitter = (float)(rng.NextDouble() - 0.5) * 0.40f;
                Vector3 tilted = Vector3.TransformNormal(direction, Matrix4x4.CreateFromAxisAngle(perpendicular, tilt + tiltJitter));
                float roll = k * (MathF.PI * 2f / species.Children) + depth * GoldenAngle + rollJitter;
                Vector3 child = Vector3.Normalize(Vector3.TransformNormal(tilted, Matrix4x4.CreateFromAxisAngle(direction, roll)));
                Branch(end, child, length * species.Ratio, depth + 1, endDistance);
            }
        }

        Branch(Vector3.Zero, Vector3.UnitY, 1.0f, 0, 0f);
        Normalize(maxDistance);
        SpawnPetals(rng);
    }

    private BranchVertex Vertex(Vector3 position, int depth, float distance)
    {
        // The deepest vertices are the far ends of the last generation, so
        // depth runs over MaxDepth + 1 steps to land exactly on 1 there.
        float d = (float)depth / (Species.MaxDepth + 1);
        return new BranchVertex { Position = position, Depth = d, Sway = MathF.Pow(d, 1.5f), Birth = distance };
    }

    // Centers the tree, scales it into the fit cube and turns raw path lengths
    // into birth times in [0, 1].
    private void Normalize(float maxDistance)
    {
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        foreach (BranchVertex v in Branches)
        {
            lo = Vector3.Min(lo, v.Position);
            hi = Vector3.Max(hi, v.Position);
        }
        Vector3 center = (lo + hi) * 0.5f;
        Vector3 extent = hi - lo;
        float scale = FitExtent / MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));
        float invDistance = maxDistance > 0f ? 1f / maxDistance : 1f;

        Span<BranchVertex> branches = CollectionsMarshal.AsSpan(Branches);
        for (int i = 0; i < branches.Length; i++)
        {
            branches[i].Position = (branches[i].Position - center) * scale;
            branches[i].Birth *= invDistance;
        }
        Span<TipVertex> tips = CollectionsMarshal.AsSpan(Tips);
        for (int i = 0; i < tips.Length; i++)
        {
            tips[i].Position = (tips[i].Position - center) * scale;
            tips[i].Birth *= invDistance;
        }
    }

    // Random canopy tips become petal spawn points. Each petal loops forever
    // on its own schedule; the vertex shader derives the fall from time.
    private void SpawnPetals(Random rng)
    {
        int count = Math.Min(MaxPetals, Tips.Count);
        for (int i = 0; i < count; i++)
        {
            Vector3 spawn = Tips[rng.Next(Tips.Count)].Position;
            Petals.Add(new PetalVertex { Spawn = spawn, Phase = (float)rng.NextDouble(), Seed = (float)rng.NextDouble() * 100f });
        }
    }
}
