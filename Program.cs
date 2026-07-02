// Tree3D - a volumetric 3D fractal tree on raw Win32 + WGL + OpenGL 3.3 core.
// Zero external dependencies (System.Numerics is part of the BCL).
//
//   Recursion branches in 3D (each node spawns several children on a cone with a
//   golden-angle roll). A geometry shader inflates every segment into a
//   camera-facing tube whose radius tapers from a thick trunk to thin twigs, with
//   round cylinder shading. Depth testing makes near branches occlude far ones,
//   and glowing sprites fill the canopy.
//
//   "Living tree" edition:
//     * The tree GROWS from a seed. Every vertex stores its normalized path
//       length from the root ("birth time"); the geometry shader clips segments
//       that have not been reached yet and stretches the one currently growing,
//       so branches visibly extend and thicken as they mature.
//     * Seven color palettes (1-7 or C to cycle), including "Aurora" whose hue
//       drifts around the color wheel over time.
//     * Space grows a brand-new random tree: branching factor, tilt, shrink
//       ratio and recursion depth are re-rolled, so every tree is a new species.
//       R replays the growth of the current tree (handy for screen recording).
//     * Once the canopy is complete, petals begin to detach and drift down.
//       They are pure GPU particles: the vertex shader derives a looping fall
//       trajectory from time alone, no per-frame CPU work.
//
//   Controls: drag = rotate, wheel = zoom, Space = new tree, R = replay growth,
//             C / 1..7 = palette, Esc = quit.
//
//   dotnet run -c Release
//
// Author: Mykhailo Makarov (m.m.makarov@gmail.com), no-library style (P/Invoke only).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Makarov.Tree3D;

public static class Program
{
    // ------------------------------------------------------------------ tree

    const float TrunkR = 0.030f;  // tube radius at the trunk (normalized units)
    const float TipR   = 0.0035f; // tube radius at the twigs
    const float GrowSeconds = 8.0f;
    const int   PetalMax    = 1400;

    // Current "species" - re-rolled on Space.
    static int   _children = 3;
    static int   _maxDepth = 8;
    static float _ratio    = 0.74f;
    static float _tiltDeg  = 36.0f;
    static int   _seed     = 11;

    static readonly List<float> Segs   = new(); // x,y,z,depth,sway,birth per vertex (2 per segment)
    static readonly List<float> Tips   = new(); // x,y,z,birth per leaf
    static readonly List<float> Petals = new(); // x,y,z,phase,seed per petal
    static int SegCount, TipCount, PetalCount;

    // ---------------------------------------------------------------- window

    static int  _w = 1080, _h = 1080;
    static bool _running = true;
    static Win.WndProc _wndProcRef;
    static IntPtr _hwnd;

    static float _yaw = 0.7f, _pitch = 0.30f, _dist = 3.2f;
    static bool  _dragging;
    static int   _lastX, _lastY;

    // ------------------------------------------------------------- animation

    static bool  _regrow;        // Space: rebuild a new tree
    static bool  _replant;       // R: restart growth of the same tree
    static float _growStart;     // seconds on the app clock when growth began
    static int   _paletteIndex;
    static int   _paletteRequest = -1; // set from WndProc, applied on the main loop

    // ---------------------------------------------------------------- colors

    private readonly record struct Palette(
        string Name, Vector3 Trunk, Vector3 Mid, Vector3 Tip,
        Vector3 Glow, Vector3 BgIn, Vector3 BgOut);

    static readonly Palette[] Palettes =
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
        new("Aurora", // Mid/Tip/Glow are recomputed every frame from a drifting hue.
            new(0.10f, 0.09f, 0.13f), new(0.40f, 0.20f, 0.60f), new(0.90f, 0.60f, 1.00f),
            new(0.90f, 0.70f, 1.00f), new(0.025f, 0.030f, 0.055f), new(0.004f, 0.004f, 0.009f)),
    ];

    [STAThread]
    static void Main()
    {
        RollSpecies(new Random().Next()); // first tree is already a surprise
        BuildTree();

        IntPtr hInstance = Win.GetModuleHandleW(IntPtr.Zero);
        const string cls = "Tree3DGLWindow";

        _wndProcRef = WindowProc;
        var wc = new Win.WNDCLASSEX
        {
            cbSize        = (uint)Marshal.SizeOf<Win.WNDCLASSEX>(),
            style         = Win.CS_OWNDC | Win.CS_HREDRAW | Win.CS_VREDRAW,
            lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_wndProcRef),
            hInstance     = hInstance,
            hCursor       = Win.LoadCursorW(IntPtr.Zero, (IntPtr)Win.IDC_ARROW),
            lpszClassName = cls,
        };

        if (Win.RegisterClassExW(ref wc) == 0)
        {
            throw new Exception($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
        }

        _hwnd = Win.CreateWindowExW(
            0, cls, "3D Fractal Tree",
            Win.WS_OVERLAPPEDWINDOW | Win.WS_VISIBLE,
            Win.CW_USEDEFAULT, Win.CW_USEDEFAULT, _w, _h,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            throw new Exception("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
        }

        IntPtr hdc = Win.GetDC(_hwnd);
        IntPtr ctx = CreateGLContext(hdc);
        GL.Load();

        if (GL.wglSwapIntervalEXT is not null)
        {
            GL.wglSwapIntervalEXT(1);
        }

        if (Win.GetClientRect(_hwnd, out var rc)) { _w = rc.right - rc.left; _h = rc.bottom - rc.top; }

        uint gradProg  = BuildProgram(QuadVS, GradFS, null);
        uint tubeProg  = BuildProgram(TubeVS, TubeFS, TubeGS);
        uint tipProg   = BuildProgram(TipVS, TipFS, null);
        uint petalProg = BuildProgram(PetalVS, PetalFS, null);

        int tuMVP   = GL.glGetUniformLocation(tubeProg, Ascii("uMVP"));
        int tuCam   = GL.glGetUniformLocation(tubeProg, Ascii("uCam"));
        int tuTime  = GL.glGetUniformLocation(tubeProg, Ascii("uTime"));
        int tuTrunk = GL.glGetUniformLocation(tubeProg, Ascii("uTrunkR"));
        int tuTip   = GL.glGetUniformLocation(tubeProg, Ascii("uTipR"));
        int tuLight = GL.glGetUniformLocation(tubeProg, Ascii("uLight"));
        int tuGrow  = GL.glGetUniformLocation(tubeProg, Ascii("uGrow"));
        int tuColT  = GL.glGetUniformLocation(tubeProg, Ascii("uTrunkCol"));
        int tuColM  = GL.glGetUniformLocation(tubeProg, Ascii("uMidCol"));
        int tuColP  = GL.glGetUniformLocation(tubeProg, Ascii("uTipCol"));

        int piMVP  = GL.glGetUniformLocation(tipProg, Ascii("uMVP"));
        int piTime = GL.glGetUniformLocation(tipProg, Ascii("uTime"));
        int piSize = GL.glGetUniformLocation(tipProg, Ascii("uSize"));
        int piGrow = GL.glGetUniformLocation(tipProg, Ascii("uGrow"));
        int piGlow = GL.glGetUniformLocation(tipProg, Ascii("uGlowCol"));

        int peMVP  = GL.glGetUniformLocation(petalProg, Ascii("uMVP"));
        int peTime = GL.glGetUniformLocation(petalProg, Ascii("uPetalTime"));
        int peSize = GL.glGetUniformLocation(petalProg, Ascii("uSize"));
        int peGlow = GL.glGetUniformLocation(petalProg, Ascii("uGlowCol"));

        int gScale = GL.glGetUniformLocation(gradProg, Ascii("uScale"));
        int gIn    = GL.glGetUniformLocation(gradProg, Ascii("uInner"));
        int gOut   = GL.glGetUniformLocation(gradProg, Ascii("uOuter"));

        var (tubeVao, tubeVbo)   = MakeTubeVao();
        var (tipVao, tipVbo)     = MakeTipVao();
        var (petalVao, petalVbo) = MakePetalVao();
        uint quadVao = MakeQuadVao();

        GL.glEnable(GL.GL_VERTEX_PROGRAM_POINT_SIZE);

        var clock = Stopwatch.StartNew();
        _growStart = 0f;
        UpdateTitle();

        while (_running)
        {
            while (Win.PeekMessageW(out var msg, IntPtr.Zero, 0, 0, Win.PM_REMOVE))
            {
                if (msg.message == Win.WM_QUIT) { _running = false; break; }
                Win.TranslateMessage(ref msg);
                Win.DispatchMessageW(ref msg);
            }

            if (!_running) break;

            float t = (float)clock.Elapsed.TotalSeconds;

            if (_paletteRequest >= 0)
            {
                _paletteIndex = _paletteRequest;
                _paletteRequest = -1;
                UpdateTitle();
            }

            if (_regrow)
            {
                _regrow = false;
                RollSpecies(Environment.TickCount);
                BuildTree();
                ReuploadTree(tubeVbo, tipVbo, petalVbo);
                _growStart = t;
                UpdateTitle();
            }

            if (_replant)
            {
                _replant = false;
                _growStart = t; // same geometry, growth starts over
            }

            // growth: 0..1 clips branches (ease-out), then keeps rising past 1
            // so young wood finishes thickening and the last tips reach full glow
            float gx = MathF.Max(0f, (t - _growStart) / GrowSeconds);
            float grow = gx >= 1f ? gx : 1f - (1f - gx) * (1f - gx);
            float petalTime = MathF.Max(0f, t - _growStart - GrowSeconds);

            var pal = CurrentPalette(t);

            if (!_dragging) _yaw += 0.0035f; // gentle idle spin

            // camera
            var eye = new Vector3(
                _dist * MathF.Cos(_pitch) * MathF.Sin(_yaw),
                _dist * MathF.Sin(_pitch),
                _dist * MathF.Cos(_pitch) * MathF.Cos(_yaw));
            float aspect = (float)_w / _h;
            float[] proj = Perspective(0.80f, aspect, 0.05f, 50f);
            float[] view = LookAt(eye, Vector3.Zero, Vector3.UnitY);
            float[] mvp  = Mul(proj, view);

            GL.glViewport(0, 0, _w, _h);
            GL.glClearColor(0f, 0f, 0f, 1f);
            GL.glClear(GL.GL_COLOR_BUFFER_BIT | GL.GL_DEPTH_BUFFER_BIT);

            // background gradient
            GL.glDisable(GL.GL_DEPTH_TEST);
            GL.glDisable(GL.GL_BLEND);
            GL.glUseProgram(gradProg);
            GL.glUniform2f(gScale, 1f, 1f);
            Uniform3(gIn, pal.BgIn);
            Uniform3(gOut, pal.BgOut);
            GL.glBindVertexArray(quadVao);
            GL.glDrawArrays(GL.GL_TRIANGLE_STRIP, 0, 4);

            // tubes (solid, depth-tested)
            GL.glEnable(GL.GL_DEPTH_TEST);
            GL.glDepthMask(true);
            GL.glUseProgram(tubeProg);
            GL.glUniformMatrix4fv(tuMVP, 1, 0, mvp);
            GL.glUniform3f(tuCam, eye.X, eye.Y, eye.Z);
            GL.glUniform1f(tuTime, t);
            GL.glUniform1f(tuTrunk, TrunkR);
            GL.glUniform1f(tuTip, TipR);
            GL.glUniform1f(tuGrow, grow);
            GL.glUniform3f(tuLight, 0.4f, 0.8f, 0.45f);
            Uniform3(tuColT, pal.Trunk);
            Uniform3(tuColM, pal.Mid);
            Uniform3(tuColP, pal.Tip);
            GL.glBindVertexArray(tubeVao);
            GL.glDrawArrays(GL.GL_LINES, 0, SegCount);

            // glowing canopy sprites (additive, depth-tested but no depth write)
            GL.glEnable(GL.GL_BLEND);
            GL.glBlendFunc(GL.GL_SRC_ALPHA, GL.GL_ONE);
            GL.glDepthMask(false);
            GL.glUseProgram(tipProg);
            GL.glUniformMatrix4fv(piMVP, 1, 0, mvp);
            GL.glUniform1f(piTime, t);
            GL.glUniform1f(piSize, _h * 0.10f);
            GL.glUniform1f(piGrow, grow);
            Uniform3(piGlow, pal.Glow);
            GL.glBindVertexArray(tipVao);
            GL.glDrawArrays(GL.GL_POINTS, 0, TipCount);

            // falling petals - only once the tree has fully grown
            if (petalTime > 0f && PetalCount > 0)
            {
                GL.glUseProgram(petalProg);
                GL.glUniformMatrix4fv(peMVP, 1, 0, mvp);
                GL.glUniform1f(peTime, petalTime);
                GL.glUniform1f(peSize, _h * 0.045f);
                Uniform3(peGlow, pal.Glow);
                GL.glBindVertexArray(petalVao);
                GL.glDrawArrays(GL.GL_POINTS, 0, PetalCount);
            }

            GL.glDepthMask(true);

            Win.SwapBuffers(hdc);
        }

        Win.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        Win.wglDeleteContext(ctx);
        Win.ReleaseDC(_hwnd, hdc);
    }

    static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win.WM_SIZE:
                int lp = (int)lParam;
                _w = Math.Max(1, lp & 0xFFFF);
                _h = Math.Max(1, (lp >> 16) & 0xFFFF);
                return IntPtr.Zero;

            case Win.WM_LBUTTONDOWN:
                _dragging = true; _lastX = Short(lParam, 0); _lastY = Short(lParam, 16);
                return IntPtr.Zero;

            case Win.WM_LBUTTONUP:
                _dragging = false;
                return IntPtr.Zero;

            case Win.WM_MOUSEMOVE:
                if (_dragging)
                {
                    int x = Short(lParam, 0), y = Short(lParam, 16);
                    _yaw   -= (x - _lastX) * 0.006f;
                    _pitch += (y - _lastY) * 0.006f;
                    float lim = 1.45f;
                    if (_pitch >  lim) _pitch =  lim;
                    if (_pitch < -lim) _pitch = -lim;
                    _lastX = x; _lastY = y;
                }
                return IntPtr.Zero;

            case Win.WM_MOUSEWHEEL:
                int delta = (short)((((long)wParam) >> 16) & 0xFFFF);
                _dist *= 1f - delta / 1200f;
                if (_dist < 1.2f) _dist = 1.2f;
                if (_dist > 12f)  _dist = 12f;
                return IntPtr.Zero;

            case Win.WM_KEYDOWN:
                int vk = (int)wParam;
                if (vk == 0x1B) { Win.PostQuitMessage(0); }                       // Esc
                else if (vk == 0x20) { _regrow = true; }                          // Space
                else if (vk == 0x52) { _replant = true; }                         // R
                else if (vk == 0x43)                                              // C
                {
                    _paletteRequest = (_paletteIndex + 1) % Palettes.Length;
                }
                else if (vk >= 0x31 && vk <= 0x30 + Palettes.Length)              // 1..7
                {
                    _paletteRequest = vk - 0x31;
                }
                return IntPtr.Zero;

            case Win.WM_DESTROY:
                Win.PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return Win.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static int Short(IntPtr lParam, int shift) => (short)(((long)lParam >> shift) & 0xFFFF);

    // ------------------------------------------------------------ tree build

    private static void RollSpecies(int seed)
    {
        var rng = new Random(seed);
        _seed = rng.Next();

        // 3-way forks are the classic look, 2 and 4 are rarer species.
        int pick = rng.Next(5);
        _children = pick switch { 0 => 2, 4 => 4, _ => 3 };

        // Keep the total segment count in the same ballpark for any fork count.
        _maxDepth = _children switch { 2 => 10, 3 => 8, _ => 7 };

        _ratio   = 0.70f + (float)rng.NextDouble() * 0.09f;
        _tiltDeg = 24f   + (float)rng.NextDouble() * 24f;
    }

    private static void BuildTree()
    {
        Segs.Clear();
        Tips.Clear();
        Petals.Clear();

        var rng = new Random(_seed);
        float tilt = _tiltDeg * MathF.PI / 180f;
        float maxDist = 0f;

        void Branch(Vector3 pos, Vector3 dir, float len, int depth, float dist)
        {
            Vector3 end = pos + dir * len;
            float endDist = dist + len;
            if (endDist > maxDist) maxDist = endDist;
            AddSeg(pos, depth, dist, end, depth + 1, endDist);
            if (depth >= _maxDepth) { AddTip(end, endDist); return; }

            // axis perpendicular to dir, to tilt children off-axis
            Vector3 refv = MathF.Abs(dir.Y) > 0.99f ? Vector3.UnitX : Vector3.UnitY;
            Vector3 perp = Vector3.Normalize(Vector3.Cross(dir, refv));

            for (int k = 0; k < _children; k++)
            {
                float jt = (float)(rng.NextDouble() - 0.5) * 0.20f;
                float jr = (float)(rng.NextDouble() - 0.5) * 0.40f;
                var tiltM = Matrix4x4.CreateFromAxisAngle(perp, tilt + jt);
                Vector3 tilted = Vector3.TransformNormal(dir, tiltM);
                float roll = k * (MathF.PI * 2f / _children) + depth * 2.39996f + jr;
                var rollM = Matrix4x4.CreateFromAxisAngle(dir, roll);
                Vector3 child = Vector3.Normalize(Vector3.TransformNormal(tilted, rollM));
                Branch(end, child, len * _ratio, depth + 1, endDist);
            }
        }

        Branch(Vector3.Zero, Vector3.UnitY, 1.0f, 0, 0f);
        Normalize(maxDist);
        BuildPetals(rng);
    }

    private static void AddSeg(Vector3 a, int da, float distA, Vector3 b, int db, float distB)
    {
        AddV(a, da, distA);
        AddV(b, db, distB);
    }

    private static void AddV(Vector3 p, int depth, float dist)
    {
        float d = (float)depth / _maxDepth;
        float sway = MathF.Pow(d, 1.5f);

        Segs.Add(p.X);
        Segs.Add(p.Y);
        Segs.Add(p.Z);
        Segs.Add(d);
        Segs.Add(sway);
        Segs.Add(dist); // raw path length; normalized to "birth" in Normalize()
    }

    private static void AddTip(Vector3 p, float dist)
    {
        Tips.Add(p.X);
        Tips.Add(p.Y);
        Tips.Add(p.Z);
        Tips.Add(dist);
    }

    private static void Normalize(float maxDist)
    {
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);

        for (int i = 0; i < Segs.Count; i += 6)
        {
            var p = new Vector3(Segs[i], Segs[i + 1], Segs[i + 2]);
            lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
        }

        Vector3 c = (lo + hi) * 0.5f;
        Vector3 ext = hi - lo;
        float scale = 1.9f / MathF.Max(ext.X, MathF.Max(ext.Y, ext.Z));
        float inv = maxDist > 0f ? 1f / maxDist : 1f;

        for (int i = 0; i < Segs.Count; i += 6)
        {
            Segs[i]     = (Segs[i]     - c.X) * scale;
            Segs[i + 1] = (Segs[i + 1] - c.Y) * scale;
            Segs[i + 2] = (Segs[i + 2] - c.Z) * scale;
            Segs[i + 5] *= inv;
        }

        for (int i = 0; i < Tips.Count; i += 4)
        {
            Tips[i]     = (Tips[i]     - c.X) * scale;
            Tips[i + 1] = (Tips[i + 1] - c.Y) * scale;
            Tips[i + 2] = (Tips[i + 2] - c.Z) * scale;
            Tips[i + 3] *= inv;
        }

        SegCount = Segs.Count / 6;
        TipCount = Tips.Count / 4;
    }

    private static void BuildPetals(Random rng)
    {
        // Sample random canopy tips as petal spawn points (positions are already
        // normalized here). Each petal loops forever on its own schedule.
        int tips = Tips.Count / 4;
        int count = Math.Min(PetalMax, tips);

        for (int i = 0; i < count; i++)
        {
            int j = rng.Next(tips) * 4;
            Petals.Add(Tips[j]);
            Petals.Add(Tips[j + 1]);
            Petals.Add(Tips[j + 2]);
            Petals.Add((float)rng.NextDouble());        // phase
            Petals.Add((float)rng.NextDouble() * 100f); // per-petal seed
        }

        PetalCount = count;
    }

    // -------------------------------------------------------------- palettes

    private static Palette CurrentPalette(float t)
    {
        var p = Palettes[_paletteIndex];
        if (p.Name != "Aurora") return p;

        // Aurora: hue drifts slowly around the wheel; trunk stays dark neutral.
        float hue = t * 0.02f - MathF.Floor(t * 0.02f);
        return p with
        {
            Mid  = Hsv(hue, 0.75f, 0.60f),
            Tip  = Hsv((hue + 0.10f) % 1f, 0.55f, 1.00f),
            Glow = Hsv((hue + 0.08f) % 1f, 0.35f, 1.00f),
        };
    }

    private static Vector3 Hsv(float h, float s, float v)
    {
        float r = MathF.Abs(h * 6f - 3f) - 1f;
        float g = 2f - MathF.Abs(h * 6f - 2f);
        float b = 2f - MathF.Abs(h * 6f - 4f);
        var rgb = new Vector3(Math.Clamp(r, 0f, 1f), Math.Clamp(g, 0f, 1f), Math.Clamp(b, 0f, 1f));
        return Vector3.Lerp(Vector3.One, rgb, s) * v;
    }

    private static void Uniform3(int loc, Vector3 v) => GL.glUniform3f(loc, v.X, v.Y, v.Z);

    private static void UpdateTitle()
    {
        Win.SetWindowTextW(_hwnd,
            $"3D Fractal Tree - {Palettes[_paletteIndex].Name} | {_children}-fork, depth {_maxDepth}" +
            "  (Space = new tree, R = replay, C/1-7 = colors, drag = rotate, wheel = zoom)");
    }

    // ------------------------------------------------------------------ math

    private static float[] Perspective(float fovy, float aspect, float n, float f)
    {
        float t = 1f / MathF.Tan(fovy * 0.5f);
        var m = new float[16];
        m[0] = t / aspect; m[5] = t;
        m[10] = (f + n) / (n - f); m[11] = -1f;
        m[14] = (2f * f * n) / (n - f);
        return m;
    }

    private static float[] LookAt(Vector3 eye, Vector3 center, Vector3 up)
    {
        var f = Vector3.Normalize(center - eye);
        var s = Vector3.Normalize(Vector3.Cross(f, up));
        var u = Vector3.Cross(s, f);

        return
        [
            s.X, u.X, -f.X, 0f,
            s.Y, u.Y, -f.Y, 0f,
            s.Z, u.Z, -f.Z, 0f,
            -Vector3.Dot(s, eye), -Vector3.Dot(u, eye), Vector3.Dot(f, eye), 1f
        ];
    }

    private static float[] Mul(float[] a, float[] b)
    {
        var r = new float[16];

        for (int col = 0; col < 4; col++)
        {
            for (int row = 0; row < 4; row++)
            {
                float s = 0f;
                for (int k = 0; k < 4; k++) s += a[k * 4 + row] * b[col * 4 + k];
                r[col * 4 + row] = s;
            }
        }

        return r;
    }

    // ------------------------------------------------------------------ VAOs

    static (uint vao, uint vbo) MakeTubeVao()
    {
        uint vao = 0, vbo = 0;
        GL.glGenVertexArrays(1, ref vao);
        GL.glBindVertexArray(vao);
        GL.glGenBuffers(1, ref vbo);
        GL.glBindBuffer(GL.GL_ARRAY_BUFFER, vbo);
        Upload(Segs.ToArray());
        int stride = 6 * sizeof(float);
        GL.glVertexAttribPointer(0, 3, GL.GL_FLOAT, 0, stride, 0);
        GL.glEnableVertexAttribArray(0);
        GL.glVertexAttribPointer(1, 1, GL.GL_FLOAT, 0, stride, 3 * sizeof(float));
        GL.glEnableVertexAttribArray(1);
        GL.glVertexAttribPointer(2, 1, GL.GL_FLOAT, 0, stride, 4 * sizeof(float));
        GL.glEnableVertexAttribArray(2);
        GL.glVertexAttribPointer(3, 1, GL.GL_FLOAT, 0, stride, 5 * sizeof(float));
        GL.glEnableVertexAttribArray(3);
        return (vao, vbo);
    }

    private static (uint vao, uint vbo) MakeTipVao()
    {
        uint vao = 0, vbo = 0;
        GL.glGenVertexArrays(1, ref vao);
        GL.glBindVertexArray(vao);
        GL.glGenBuffers(1, ref vbo);
        GL.glBindBuffer(GL.GL_ARRAY_BUFFER, vbo);
        Upload(Tips.ToArray());
        int stride = 4 * sizeof(float);
        GL.glVertexAttribPointer(0, 3, GL.GL_FLOAT, 0, stride, 0);
        GL.glEnableVertexAttribArray(0);
        GL.glVertexAttribPointer(1, 1, GL.GL_FLOAT, 0, stride, 3 * sizeof(float));
        GL.glEnableVertexAttribArray(1);
        return (vao, vbo);
    }

    private static (uint vao, uint vbo) MakePetalVao()
    {
        uint vao = 0, vbo = 0;
        GL.glGenVertexArrays(1, ref vao);
        GL.glBindVertexArray(vao);
        GL.glGenBuffers(1, ref vbo);
        GL.glBindBuffer(GL.GL_ARRAY_BUFFER, vbo);
        Upload(Petals.ToArray());
        int stride = 5 * sizeof(float);
        GL.glVertexAttribPointer(0, 3, GL.GL_FLOAT, 0, stride, 0);
        GL.glEnableVertexAttribArray(0);
        GL.glVertexAttribPointer(1, 1, GL.GL_FLOAT, 0, stride, 3 * sizeof(float));
        GL.glEnableVertexAttribArray(1);
        GL.glVertexAttribPointer(2, 1, GL.GL_FLOAT, 0, stride, 4 * sizeof(float));
        GL.glEnableVertexAttribArray(2);
        return (vao, vbo);
    }

    private static void ReuploadTree(uint tubeVbo, uint tipVbo, uint petalVbo)
    {
        GL.glBindBuffer(GL.GL_ARRAY_BUFFER, tubeVbo);
        Upload(Segs.ToArray());
        GL.glBindBuffer(GL.GL_ARRAY_BUFFER, tipVbo);
        Upload(Tips.ToArray());
        GL.glBindBuffer(GL.GL_ARRAY_BUFFER, petalVbo);
        Upload(Petals.ToArray());
    }

    private static uint MakeQuadVao()
    {
        float[] q = [-1f,-1f,0f,0f,  1f,-1f,1f,0f,  -1f,1f,0f,1f,  1f,1f,1f,1f];
        uint vao = 0, vbo = 0;
        GL.glGenVertexArrays(1, ref vao);
        GL.glBindVertexArray(vao);
        GL.glGenBuffers(1, ref vbo);
        GL.glBindBuffer(GL.GL_ARRAY_BUFFER, vbo);
        Upload(q);
        int stride = 4 * sizeof(float);
        GL.glVertexAttribPointer(0, 2, GL.GL_FLOAT, 0, stride, 0);
        GL.glEnableVertexAttribArray(0);
        GL.glVertexAttribPointer(1, 2, GL.GL_FLOAT, 0, stride, 2 * sizeof(float));
        GL.glEnableVertexAttribArray(1);
        return vao;
    }

    private static void Upload(float[] data)
    {
        GCHandle h = GCHandle.Alloc(data, GCHandleType.Pinned);

        try
        {
            GL.glBufferData(GL.GL_ARRAY_BUFFER, data.Length * sizeof(float), h.AddrOfPinnedObject(), GL.GL_STATIC_DRAW);
        }
        finally
        {
            h.Free();
        }
    }

    // --------------------------------------------------------------- shaders

    private const string TubeVS = @"#version 330 core
layout(location=0) in vec3  aPos;
layout(location=1) in float aDepth;
layout(location=2) in float aSway;
layout(location=3) in float aBirth;
uniform float uTime;
out vData { vec3 wpos; float depth; float birth; } vs;
void main(){
    // wind with slow gusts: amplitude swells and fades over ~17 s
    float gust = 0.6 + 0.4 * sin(uTime * 0.37 + aPos.x * 0.5);
    float amp = 0.05 * gust;
    float sx = sin(uTime*1.3 + aPos.y*1.6) * aSway * amp;
    float sz = cos(uTime*1.1 + aPos.y*1.4) * aSway * amp;
    vs.wpos = aPos + vec3(sx, 0.0, sz);
    vs.depth = aDepth;
    vs.birth = aBirth;
    gl_Position = vec4(vs.wpos, 1.0);
}";

    private const string TubeGS = @"#version 330 core
layout(lines) in;
layout(triangle_strip, max_vertices = 4) out;
in vData { vec3 wpos; float depth; float birth; } gs[];
uniform mat4  uMVP;
uniform vec3  uCam;
uniform float uTrunkR;
uniform float uTipR;
uniform float uGrow;
out float vSide;
out float vDepth;
out vec3  vNormal;
void emit(vec3 p, vec3 side, float r, float depth, float s){
    vec3 wp = p + side * (r * s);
    vSide = s; vDepth = depth; vNormal = side * s;
    gl_Position = uMVP * vec4(wp, 1.0);
    EmitVertex();
}
void main(){
    float b0 = gs[0].birth, b1 = gs[1].birth;
    float g = min(uGrow, 1.0); // clipping saturates; maturity keeps using raw uGrow
    if (g <= b0) return;       // this segment has not been reached yet

    // clip the far end of the segment that is currently growing
    float ct = clamp((g - b0) / max(b1 - b0, 1e-5), 0.0, 1.0);
    vec3  p0 = gs[0].wpos;
    vec3  p1 = mix(gs[0].wpos, gs[1].wpos, ct);
    float d1 = mix(gs[0].depth, gs[1].depth, ct);
    float bc = mix(b0, b1, ct);

    // young wood is thin, it thickens as the branch matures
    float m0 = mix(0.30, 1.0, smoothstep(b0, b0 + 0.30, uGrow));
    float m1 = mix(0.30, 1.0, smoothstep(bc, bc + 0.30, uGrow));
    float r0 = mix(uTrunkR, uTipR, gs[0].depth) * m0;
    float r1 = mix(uTrunkR, uTipR, d1) * m1;

    vec3 axis = normalize(p1 - p0 + vec3(1e-6));
    vec3 s0 = normalize(cross(axis, uCam - p0));
    vec3 s1 = normalize(cross(axis, uCam - p1));
    emit(p0, s0, r0, gs[0].depth,  1.0);
    emit(p0, s0, r0, gs[0].depth, -1.0);
    emit(p1, s1, r1, d1,  1.0);
    emit(p1, s1, r1, d1, -1.0);
    EndPrimitive();
}";

    private const string TubeFS = @"#version 330 core
in float vSide;
in float vDepth;
in vec3  vNormal;
uniform vec3 uLight;
uniform vec3 uTrunkCol;
uniform vec3 uMidCol;
uniform vec3 uTipCol;
out vec4 FragColor;
void main(){
    float cyl = sqrt(max(0.0, 1.0 - vSide * vSide));   // round cross-section
    vec3 col = mix(uTrunkCol, uMidCol, smoothstep(0.0, 0.5, vDepth));
    col = mix(col, uTipCol, smoothstep(0.5, 1.0, vDepth));
    float lit = 0.45 + 0.55 * clamp(dot(normalize(vNormal), normalize(uLight)) * 0.5 + 0.5, 0.0, 1.0);
    FragColor = vec4(col * (0.30 + 0.70 * cyl) * lit, 1.0);
}";

    private const string TipVS = @"#version 330 core
layout(location=0) in vec3  aPos;
layout(location=1) in float aBirth;
uniform mat4  uMVP;
uniform float uTime;
uniform float uSize;
uniform float uGrow;
out float vTw;
out float vFade;
void main(){
    vec4 cp = uMVP * vec4(aPos, 1.0);
    gl_Position = cp;
    float tw = 0.5 + 0.5 * sin(uTime * 2.5 + aPos.x * 9.0 + aPos.y * 7.0 + aPos.z * 5.0);
    vFade = smoothstep(aBirth - 0.02, aBirth + 0.05, uGrow); // bloom in
    gl_PointSize = clamp(uSize / cp.w, 1.5, 26.0) * (0.6 + 0.8 * tw) * max(vFade, 0.001);
    vTw = tw;
}";

    private const string TipFS = @"#version 330 core
in float vTw;
in float vFade;
uniform vec3 uGlowCol;
out vec4 FragColor;
void main(){
    float r = length(gl_PointCoord - 0.5) * 2.0;
    float a = smoothstep(1.0, 0.0, r);
    vec3 col = mix(uGlowCol, vec3(1.0), 0.35);
    FragColor = vec4(col, a * (0.35 + 0.65 * vTw) * 0.5 * vFade);
}";

    private const string PetalVS = @"#version 330 core
layout(location=0) in vec3  aSpawn;
layout(location=1) in float aPhase;
layout(location=2) in float aSeed;
uniform mat4  uMVP;
uniform float uPetalTime;
uniform float uSize;
out float vAlpha;
void main(){
    // Each petal loops forever: detach from its tip, flutter down, respawn.
    float cycle = 6.0 + fract(aSeed * 0.173) * 4.0;
    float tt = uPetalTime + aPhase * cycle;
    float k = fract(tt / cycle);                 // 0..1 fall progress

    float fall = k * k * 0.9 + k * 0.5;          // accelerating descent
    float sway = sin(tt * 2.1 + aSeed) * 0.10 * k
               + sin(tt * 0.7 + aSeed * 3.1) * 0.05 * k;
    float drift = cos(tt * 1.7 + aSeed * 2.3) * 0.10 * k;

    vec3 p = aSpawn + vec3(sway, -fall, drift);
    vec4 cp = uMVP * vec4(p, 1.0);
    gl_Position = cp;

    // fade in on detach, fade out near the ground; ramp up over the first cycle
    vAlpha = smoothstep(0.0, 0.06, k) * smoothstep(1.0, 0.80, k)
           * smoothstep(0.0, 3.0, uPetalTime);
    gl_PointSize = clamp(uSize / cp.w, 1.0, 10.0);
}";

    private const string PetalFS = @"#version 330 core
in float vAlpha;
uniform vec3 uGlowCol;
out vec4 FragColor;
void main(){
    float r = length(gl_PointCoord - 0.5) * 2.0;
    float a = smoothstep(1.0, 0.2, r);
    FragColor = vec4(uGlowCol, a * vAlpha * 0.65);
}";

    private const string QuadVS = @"#version 330 core
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aUV;
uniform vec2 uScale;
out vec2 vUV;
void main(){ vUV = aUV; gl_Position = vec4(aPos * uScale, 0.0, 1.0); }";

    private const string GradFS = @"#version 330 core
in vec2 vUV;
uniform vec3 uInner;
uniform vec3 uOuter;
out vec4 FragColor;
void main(){
    float d = length(vUV - vec2(0.5, 0.55));
    vec3 c = mix(uInner, uOuter, smoothstep(0.0, 0.85, d));
    FragColor = vec4(c, 1.0);
}";

    // ----------------------------------------------------------- GL plumbing

    private static uint BuildProgram(string vsSrc, string fsSrc, string gsSrc)
    {
        uint vs = Compile(GL.GL_VERTEX_SHADER, vsSrc);
        uint fs = Compile(GL.GL_FRAGMENT_SHADER, fsSrc);
        uint p  = GL.glCreateProgram();
        GL.glAttachShader(p, vs);
        GL.glAttachShader(p, fs);
        uint gs = 0;

        if (gsSrc is not null)
        {
            gs = Compile(GL.GL_GEOMETRY_SHADER, gsSrc);
            GL.glAttachShader(p, gs);
        }

        GL.glLinkProgram(p);
        int ok = 0;
        GL.glGetProgramiv(p, GL.GL_LINK_STATUS, ref ok);

        if (ok == 0)
        {
            var log = new byte[2048];
            int len = 0;
            GL.glGetProgramInfoLog(p, log.Length, ref len, log);
            throw new Exception($"Link error: {System.Text.Encoding.ASCII.GetString(log, 0, len)}");
        }

        GL.glDeleteShader(vs);
        GL.glDeleteShader(fs);
        if (gs != 0) GL.glDeleteShader(gs);
        return p;
    }

    private static uint Compile(uint type, string src)
    {
        uint sh = GL.glCreateShader(type);
        IntPtr str = Marshal.StringToHGlobalAnsi(src);

        try
        {
            GL.glShaderSource(sh, 1, [str], IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(str);
        }

        GL.glCompileShader(sh);
        int ok = 0;
        GL.glGetShaderiv(sh, GL.GL_COMPILE_STATUS, ref ok);

        if (ok == 0)
        {
            var log = new byte[2048]; int len = 0;
            GL.glGetShaderInfoLog(sh, log.Length, ref len, log);
            throw new Exception("Compile error: " + System.Text.Encoding.ASCII.GetString(log, 0, len));
        }

        return sh;
    }

    private static byte[] Ascii(string s)
    {
        var b = new byte[s.Length + 1];
        Encoding.ASCII.GetBytes(s, 0, s.Length, b, 0);
        return b;
    }

    private static IntPtr CreateGLContext(IntPtr hdc)
    {
        var pfd = new Win.PIXELFORMATDESCRIPTOR
        {
            nSize        = (ushort)Marshal.SizeOf<Win.PIXELFORMATDESCRIPTOR>(),
            nVersion     = 1,
            dwFlags      = Win.PFD_DRAW_TO_WINDOW | Win.PFD_SUPPORT_OPENGL | Win.PFD_DOUBLEBUFFER,
            iPixelType   = Win.PFD_TYPE_RGBA,
            cColorBits   = 32,
            cDepthBits   = 24,
            cStencilBits = 8,
            iLayerType   = Win.PFD_MAIN_PLANE,
        };

        int fmt = Win.ChoosePixelFormat(hdc, ref pfd);
        if (fmt == 0) throw new Exception("ChoosePixelFormat failed");
        if (!Win.SetPixelFormat(hdc, fmt, ref pfd)) throw new Exception("SetPixelFormat failed");

        IntPtr tmp = Win.wglCreateContext(hdc);
        Win.wglMakeCurrent(hdc, tmp);
        IntPtr proc = Win.wglGetProcAddress("wglCreateContextAttribsARB");

        if (proc != IntPtr.Zero)
        {
            var create = Marshal.GetDelegateForFunctionPointer<GL.WglCreateContextAttribsARB>(proc);
            int[] attributes = [0x2091, 3, 0x2092, 3, 0x9126, 0x0001, 0];
            IntPtr core = create(hdc, IntPtr.Zero, attributes);

            if (core != IntPtr.Zero)
            {
                Win.wglMakeCurrent(hdc, core);
                Win.wglDeleteContext(tmp);
                return core;
            }
        }

        return tmp;
    }
}
