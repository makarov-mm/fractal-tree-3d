// Tree3D - a volumetric 3D fractal tree on raw Win32 + WGL + OpenGL 3.3 core.
// Zero external dependencies (System.Numerics is part of the BCL).
//
//   Recursion branches in 3D (each node spawns several children on a cone with a
//   golden-angle roll). A geometry shader inflates every segment into a
//   camera-facing tube whose radius tapers from a thick trunk to thin twigs, with
//   round cylinder shading. Depth testing makes near branches occlude far ones,
//   and glowing sprites fill the canopy.
//
//   The tree grows from a seed: every vertex stores its normalized path length
//   from the root ("birth time"); the geometry shader clips segments that have
//   not been reached yet and stretches the one currently growing. Once the
//   canopy is complete, petals detach and drift down - pure GPU particles.
//
//   Controls: drag = rotate, wheel = zoom, Space = new tree, R = replay growth,
//             C / 1..7 = palette, Esc = quit.
//
//   dotnet run -c Release
//
// Author: Mykhailo Makarov (m.m.makarov@gmail.com), no-library style (P/Invoke only).

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Makarov.Tree3D;

internal static unsafe class Program
{
    private const float TrunkRadius = 0.030f; // tube radius at the trunk (normalized units)
    private const float TipRadius = 0.0035f;  // tube radius at the twigs
    private const float GrowSeconds = 8.0f;
    private const int Multisamples = 4;
    private const float IdleSpinRadiansPerSecond = 0.21f;

    // Uniform locations of one shader program, looked up once.
    private sealed class TubeProgram
    {
        public uint Id;
        public int Mvp, Camera, Time, Grow, TrunkColor, MidColor, TipColor;
    }

    private sealed class SpriteProgram
    {
        public uint Id;
        public int Mvp, Time, Size, Grow, GlowColor;
    }

    private struct Camera
    {
        public float Yaw, Pitch, Distance;

        public readonly Vector3 Eye => new(
            Distance * MathF.Cos(Pitch) * MathF.Sin(Yaw),
            Distance * MathF.Sin(Pitch),
            Distance * MathF.Cos(Pitch) * MathF.Cos(Yaw));
    }

    private static Camera s_camera = new() { Yaw = 0.7f, Pitch = 0.30f, Distance = 3.2f };
    private static bool s_regrow;   // Space: build a new tree
    private static bool s_replant;  // R: restart growth of the same tree
    private static int s_paletteIndex;
    private static int s_paletteRequest = -1;
    private static bool s_quit;

    [STAThread]
    private static int Main()
    {
        try
        {
            Run();
            return 0;
        }
        catch (Exception e)
        {
            Win32.MessageBoxW(0, e.Message, "Tree3D", Win32.MB_ICONERROR);
            return 1;
        }
    }

    private static void Run()
    {
        Tree tree = Tree.Generate(Species.Roll(Random.Shared.Next())); // the first tree is already a surprise

        Window.Create("3D Fractal Tree", 1080, 1080, Multisamples);
        Window.KeyDown = OnKey;
        Window.MouseDrag = OnDrag;
        Window.MouseWheel = OnWheel;

        uint gradientProgram = GL.BuildProgram(Shaders.QuadVertex, Shaders.GradientFragment);
        int gradientInner = GL.Uniform(gradientProgram, "uInner"u8);
        int gradientOuter = GL.Uniform(gradientProgram, "uOuter"u8);

        uint tubeId = GL.BuildProgram(Shaders.TubeVertex, Shaders.TubeFragment, Shaders.TubeGeometry);
        var tube = new TubeProgram
        {
            Id = tubeId,
            Mvp = GL.Uniform(tubeId, "uMVP"u8),
            Camera = GL.Uniform(tubeId, "uCam"u8),
            Time = GL.Uniform(tubeId, "uTime"u8),
            Grow = GL.Uniform(tubeId, "uGrow"u8),
            TrunkColor = GL.Uniform(tubeId, "uTrunkCol"u8),
            MidColor = GL.Uniform(tubeId, "uMidCol"u8),
            TipColor = GL.Uniform(tubeId, "uTipCol"u8),
        };
        GL.glUseProgram(tubeId); // the constants, set once
        GL.glUniform1f(GL.Uniform(tubeId, "uTrunkR"u8), TrunkRadius);
        GL.glUniform1f(GL.Uniform(tubeId, "uTipR"u8), TipRadius);
        GL.glUniform3f(GL.Uniform(tubeId, "uLight"u8), 0.4f, 0.8f, 0.45f);

        SpriteProgram tips = LoadSpriteProgram(Shaders.TipVertex, Shaders.TipFragment, "uTime"u8);
        SpriteProgram petals = LoadSpriteProgram(Shaders.PetalVertex, Shaders.PetalFragment, "uPetalTime"u8);

        var tubeMesh = new Mesh<BranchVertex>(components: [3, 1, 1, 1]);
        var tipMesh = new Mesh<TipVertex>(components: [3, 1]);
        var petalMesh = new Mesh<PetalVertex>(components: [3, 1, 1]);
        var quadMesh = new Mesh<Vector4>(components: [2, 2]);
        quadMesh.Upload([new(-1, -1, 0, 0), new(1, -1, 1, 0), new(-1, 1, 0, 1), new(1, 1, 1, 1)]);
        UploadTree(tree, tubeMesh, tipMesh, petalMesh);

        GL.glEnable(GL.PROGRAM_POINT_SIZE);
        if (Window.Samples > 1) GL.glEnable(GL.MULTISAMPLE);

        var clock = Stopwatch.StartNew();
        float growStart = 0f;
        float lastTime = 0f;
        UpdateTitle(tree);

        while (Window.PumpMessages() && !s_quit)
        {
            if (Window.Minimized) continue;

            float time = (float)clock.Elapsed.TotalSeconds;
            float dt = time - lastTime;
            lastTime = time;

            if (s_paletteRequest >= 0)
            {
                s_paletteIndex = s_paletteRequest;
                s_paletteRequest = -1;
                UpdateTitle(tree);
            }
            if (s_regrow)
            {
                s_regrow = false;
                tree = Tree.Generate(Species.Roll(Environment.TickCount));
                UploadTree(tree, tubeMesh, tipMesh, petalMesh);
                growStart = time;
                UpdateTitle(tree);
            }
            if (s_replant)
            {
                s_replant = false;
                growStart = time; // same geometry, growth starts over
            }

            // growth: 0..1 clips branches (ease-out), then keeps rising past 1
            // so young wood finishes thickening and the last tips reach full glow
            float gx = MathF.Max(0f, (time - growStart) / GrowSeconds);
            float grow = gx >= 1f ? gx : 1f - (1f - gx) * (1f - gx);
            float petalTime = MathF.Max(0f, time - growStart - GrowSeconds);

            Palette palette = Palettes.All[s_paletteIndex].At(time);

            if (!Window.Dragging) s_camera.Yaw += IdleSpinRadiansPerSecond * dt; // gentle idle spin
            Vector3 eye = s_camera.Eye;
            Matrix4x4 view = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY);
            Matrix4x4 mvp = view * PerspectiveGL(0.80f, (float)Window.Width / Window.Height, 0.05f, 50f);

            GL.glViewport(0, 0, Window.Width, Window.Height);
            GL.glClearColor(0f, 0f, 0f, 1f);
            GL.glClear(GL.COLOR_BUFFER_BIT | GL.DEPTH_BUFFER_BIT);

            // background gradient
            GL.glDisable(GL.DEPTH_TEST);
            GL.glDisable(GL.BLEND);
            GL.glUseProgram(gradientProgram);
            GL.Uniform(gradientInner, palette.BgInner);
            GL.Uniform(gradientOuter, palette.BgOuter);
            quadMesh.Draw(GL.TRIANGLE_STRIP);

            // tubes (solid, depth-tested)
            GL.glEnable(GL.DEPTH_TEST);
            GL.glDepthMask(1);
            GL.glUseProgram(tube.Id);
            GL.Uniform(tube.Mvp, mvp);
            GL.Uniform(tube.Camera, eye);
            GL.glUniform1f(tube.Time, time);
            GL.glUniform1f(tube.Grow, grow);
            GL.Uniform(tube.TrunkColor, palette.Trunk);
            GL.Uniform(tube.MidColor, palette.Mid);
            GL.Uniform(tube.TipColor, palette.Tip);
            tubeMesh.Draw(GL.LINES);

            // glowing canopy sprites (additive, depth-tested but no depth write)
            GL.glEnable(GL.BLEND);
            GL.glBlendFunc(GL.SRC_ALPHA, GL.ONE);
            GL.glDepthMask(0);
            GL.glUseProgram(tips.Id);
            GL.Uniform(tips.Mvp, mvp);
            GL.glUniform1f(tips.Time, time);
            GL.glUniform1f(tips.Size, Window.Height * 0.10f);
            GL.glUniform1f(tips.Grow, grow);
            GL.Uniform(tips.GlowColor, palette.Glow);
            tipMesh.Draw(GL.POINTS);

            // falling petals - only once the tree has fully grown
            if (petalTime > 0f && petalMesh.Count > 0)
            {
                GL.glUseProgram(petals.Id);
                GL.Uniform(petals.Mvp, mvp);
                GL.glUniform1f(petals.Time, petalTime);
                GL.glUniform1f(petals.Size, Window.Height * 0.045f);
                GL.Uniform(petals.GlowColor, palette.Glow);
                petalMesh.Draw(GL.POINTS);
            }
            GL.glDepthMask(1);

            Window.Swap();
        }

        Window.Destroy();
    }

    private static SpriteProgram LoadSpriteProgram(string vertex, string fragment, ReadOnlySpan<byte> timeUniform)
    {
        uint id = GL.BuildProgram(vertex, fragment);
        return new SpriteProgram
        {
            Id = id,
            Mvp = GL.Uniform(id, "uMVP"u8),
            Time = GL.Uniform(id, timeUniform),
            Size = GL.Uniform(id, "uSize"u8),
            Grow = GL.Uniform(id, "uGrow"u8),
            GlowColor = GL.Uniform(id, "uGlowCol"u8),
        };
    }

    private static void UploadTree(Tree tree, Mesh<BranchVertex> tubes, Mesh<TipVertex> tips, Mesh<PetalVertex> petals)
    {
        tubes.Upload(CollectionsMarshal.AsSpan(tree.Branches));
        tips.Upload(CollectionsMarshal.AsSpan(tree.Tips));
        petals.Upload(CollectionsMarshal.AsSpan(tree.Petals));
    }

    private static void UpdateTitle(Tree tree)
    {
        Species s = tree.Species;
        Window.SetTitle($"3D Fractal Tree - {Palettes.All[s_paletteIndex].Name} | {s.Children}-fork, depth {s.MaxDepth}" +
                        "  (Space = new tree, R = replay, C/1-7 = colors, drag = rotate, wheel = zoom)");
    }

    // OpenGL-style perspective (clip z in [-1, 1]) in the System.Numerics
    // row-vector convention, so `view * projection` is the GL "proj * view".
    private static Matrix4x4 PerspectiveGL(float fovY, float aspect, float near, float far)
    {
        float t = 1f / MathF.Tan(fovY * 0.5f);
        return new Matrix4x4(
            t / aspect, 0f, 0f, 0f,
            0f, t, 0f, 0f,
            0f, 0f, (far + near) / (near - far), -1f,
            0f, 0f, 2f * far * near / (near - far), 0f);
    }

    // ---- input ------------------------------------------------------------

    private static void OnKey(int vk)
    {
        switch (vk)
        {
        case Win32.VK_ESCAPE: s_quit = true; break;
        case Win32.VK_SPACE: s_regrow = true; break;
        case Win32.VK_R: s_replant = true; break;
        case Win32.VK_C: s_paletteRequest = (s_paletteIndex + 1) % Palettes.All.Length; break;
        default:
            if (vk > Win32.VK_0 && vk <= Win32.VK_0 + Palettes.All.Length) s_paletteRequest = vk - Win32.VK_0 - 1;
            break;
        }
    }

    private static void OnDrag(int dx, int dy)
    {
        s_camera.Yaw -= dx * 0.006f;
        s_camera.Pitch = Math.Clamp(s_camera.Pitch + dy * 0.006f, -1.45f, 1.45f);
    }

    private static void OnWheel(int delta)
    {
        s_camera.Distance = Math.Clamp(s_camera.Distance * (1f - delta / 1200f), 1.2f, 12f);
    }
}
