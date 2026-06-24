// Tree3D - a volumetric 3D fractal tree on raw Win32 + WGL + OpenGL 3.3 core.
// Zero external dependencies (System.Numerics is part of the BCL).
//
//   Recursion branches in 3D (each node spawns several children on a cone with a
//   golden-angle roll). A geometry shader inflates every segment into a
//   camera-facing tube whose radius tapers from a thick trunk to thin twigs, with
//   round cylinder shading. Depth testing makes near branches occlude far ones,
//   and glowing sprites fill the canopy. Orbit with the left mouse button, zoom
//   with the wheel.
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
    const int   MaxDepth  = 8;
    const int   Children  = 3;
    const float Ratio     = 0.74f;
    const float TiltDeg   = 36.0f;
    const float TrunkR    = 0.030f;  // tube radius at the trunk (normalized units)
    const float TipR      = 0.0035f; // tube radius at the twigs

    static readonly List<float> Segs = new(); // x,y,z,depth,sway per vertex (2 per segment)
    static readonly List<float> Tips = new(); // x,y,z per leaf
    static int SegCount, TipCount;

    static int  _w = 1080, _h = 1080;
    static bool _running = true;
    static Win.WndProc _wndProcRef;

    static float _yaw = 0.7f, _pitch = 0.30f, _dist = 3.2f;
    static bool  _dragging;
    static int   _lastX, _lastY;

    [STAThread]
    static void Main()
    {
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

        IntPtr hwnd = Win.CreateWindowExW(
            0, cls, "3D Fractal Tree - C#/OpenGL (drag = rotate, wheel = zoom)",
            Win.WS_OVERLAPPEDWINDOW | Win.WS_VISIBLE,
            Win.CW_USEDEFAULT, Win.CW_USEDEFAULT, _w, _h,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
        {
            throw new Exception("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
        }

        IntPtr hdc = Win.GetDC(hwnd);
        IntPtr ctx = CreateGLContext(hdc);
        GL.Load();

        if (GL.wglSwapIntervalEXT is not null)
        {
            GL.wglSwapIntervalEXT(1);
        }

        if (Win.GetClientRect(hwnd, out var rc)) { _w = rc.right - rc.left; _h = rc.bottom - rc.top; }

        uint gradProg = BuildProgram(QuadVS, GradFS, null);
        uint tubeProg = BuildProgram(TubeVS, TubeFS, TubeGS);
        uint tipProg  = BuildProgram(TipVS, TipFS, null);

        int tuMVP   = GL.glGetUniformLocation(tubeProg, Ascii("uMVP"));
        int tuCam   = GL.glGetUniformLocation(tubeProg, Ascii("uCam"));
        int tuTime  = GL.glGetUniformLocation(tubeProg, Ascii("uTime"));
        int tuTrunk = GL.glGetUniformLocation(tubeProg, Ascii("uTrunkR"));
        int tuTip   = GL.glGetUniformLocation(tubeProg, Ascii("uTipR"));
        int tuLight = GL.glGetUniformLocation(tubeProg, Ascii("uLight"));

        int piMVP  = GL.glGetUniformLocation(tipProg, Ascii("uMVP"));
        int piTime = GL.glGetUniformLocation(tipProg, Ascii("uTime"));
        int piSize = GL.glGetUniformLocation(tipProg, Ascii("uSize"));

        int gScale = GL.glGetUniformLocation(gradProg, Ascii("uScale"));

        uint tubeVao = MakeTubeVao();
        uint tipVao  = MakeTipVao();
        uint quadVao = MakeQuadVao();

        GL.glEnable(GL.GL_VERTEX_PROGRAM_POINT_SIZE);

        var clock = Stopwatch.StartNew();

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
            GL.glUniform3f(tuLight, 0.4f, 0.8f, 0.45f);
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
            GL.glBindVertexArray(tipVao);
            GL.glDrawArrays(GL.GL_POINTS, 0, TipCount);
            GL.glDepthMask(true);

            Win.SwapBuffers(hdc);
        }

        Win.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        Win.wglDeleteContext(ctx);
        Win.ReleaseDC(hwnd, hdc);
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

            case Win.WM_DESTROY:
                Win.PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return Win.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static int Short(IntPtr lParam, int shift) => (short)(((long)lParam >> shift) & 0xFFFF);

    private static void BuildTree()
    {
        var rng = new Random(11);
        float tilt = TiltDeg * MathF.PI / 180f;

        void Branch(Vector3 pos, Vector3 dir, float len, int depth)
        {
            Vector3 end = pos + dir * len;
            AddSeg(pos, depth, end, depth + 1);
            if (depth >= MaxDepth) { AddTip(end); return; }

            // axis perpendicular to dir, to tilt children off-axis
            Vector3 refv = MathF.Abs(dir.Y) > 0.99f ? Vector3.UnitX : Vector3.UnitY;
            Vector3 perp = Vector3.Normalize(Vector3.Cross(dir, refv));

            for (int k = 0; k < Children; k++)
            {
                float jt = (float)(rng.NextDouble() - 0.5) * 0.20f;
                float jr = (float)(rng.NextDouble() - 0.5) * 0.40f;
                var tiltM = Matrix4x4.CreateFromAxisAngle(perp, tilt + jt);
                Vector3 tilted = Vector3.TransformNormal(dir, tiltM);
                float roll = k * (MathF.PI * 2f / Children) + depth * 2.39996f + jr;
                var rollM = Matrix4x4.CreateFromAxisAngle(dir, roll);
                Vector3 child = Vector3.Normalize(Vector3.TransformNormal(tilted, rollM));
                Branch(end, child, len * Ratio, depth + 1);
            }
        }

        Branch(Vector3.Zero, Vector3.UnitY, 1.0f, 0);
        Normalize();
    }

    private static void AddSeg(Vector3 a, int da, Vector3 b, int db)
    {
        AddV(a, da);
        AddV(b, db);
    }

    private static void AddV(Vector3 p, int depth)
    {
        float d = (float)depth / MaxDepth;
        float sway = MathF.Pow(d, 1.5f);

        Segs.Add(p.X);
        Segs.Add(p.Y);
        Segs.Add(p.Z);
        Segs.Add(d);
        Segs.Add(sway);
    }

    private static void AddTip(Vector3 p)
    {
        Tips.Add(p.X);
        Tips.Add(p.Y); 
        Tips.Add(p.Z);
    }

    private static void Normalize()
    {
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);

        for (int i = 0; i < Segs.Count; i += 5)
        {
            var p = new Vector3(Segs[i], Segs[i + 1], Segs[i + 2]);
            lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
        }

        Vector3 c = (lo + hi) * 0.5f;
        Vector3 ext = hi - lo;
        float scale = 1.9f / MathF.Max(ext.X, MathF.Max(ext.Y, ext.Z));

        for (int i = 0; i < Segs.Count; i += 5)
        {
            Segs[i]     = (Segs[i]     - c.X) * scale;
            Segs[i + 1] = (Segs[i + 1] - c.Y) * scale;
            Segs[i + 2] = (Segs[i + 2] - c.Z) * scale;
        }

        for (int i = 0; i < Tips.Count; i += 3)
        {
            Tips[i]     = (Tips[i]     - c.X) * scale;
            Tips[i + 1] = (Tips[i + 1] - c.Y) * scale;
            Tips[i + 2] = (Tips[i + 2] - c.Z) * scale;
        }

        SegCount = Segs.Count / 5;
        TipCount = Tips.Count / 3;
    }

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

    static uint MakeTubeVao()
    {
        uint vao = 0, vbo = 0;
        GL.glGenVertexArrays(1, ref vao);
        GL.glBindVertexArray(vao);
        GL.glGenBuffers(1, ref vbo);
        GL.glBindBuffer(GL.GL_ARRAY_BUFFER, vbo);
        Upload(Segs.ToArray());
        int stride = 5 * sizeof(float);
        GL.glVertexAttribPointer(0, 3, GL.GL_FLOAT, 0, stride, 0);
        GL.glEnableVertexAttribArray(0);
        GL.glVertexAttribPointer(1, 1, GL.GL_FLOAT, 0, stride, 3 * sizeof(float));
        GL.glEnableVertexAttribArray(1);
        GL.glVertexAttribPointer(2, 1, GL.GL_FLOAT, 0, stride, 4 * sizeof(float));
        GL.glEnableVertexAttribArray(2);
        return vao;
    }

    private static uint MakeTipVao()
    {
        uint vao = 0, vbo = 0;
        GL.glGenVertexArrays(1, ref vao);
        GL.glBindVertexArray(vao);
        GL.glGenBuffers(1, ref vbo);
        GL.glBindBuffer(GL.GL_ARRAY_BUFFER, vbo);
        Upload(Tips.ToArray());
        GL.glVertexAttribPointer(0, 3, GL.GL_FLOAT, 0, 3 * sizeof(float), 0);
        GL.glEnableVertexAttribArray(0);
        return vao;
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

    private const string TubeVS = @"#version 330 core
layout(location=0) in vec3  aPos;
layout(location=1) in float aDepth;
layout(location=2) in float aSway;
uniform float uTime;
out vData { vec3 wpos; float depth; } vs;
void main(){
    float amp = 0.05;
    float sx = sin(uTime*1.3 + aPos.y*1.6) * aSway * amp;
    float sz = cos(uTime*1.1 + aPos.y*1.4) * aSway * amp;
    vs.wpos = aPos + vec3(sx, 0.0, sz);
    vs.depth = aDepth;
    gl_Position = vec4(vs.wpos, 1.0);
}";

    private const string TubeGS = @"#version 330 core
layout(lines) in;
layout(triangle_strip, max_vertices = 4) out;
in vData { vec3 wpos; float depth; } gs[];
uniform mat4  uMVP;
uniform vec3  uCam;
uniform float uTrunkR;
uniform float uTipR;
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
    vec3 p0 = gs[0].wpos, p1 = gs[1].wpos;
    float r0 = mix(uTrunkR, uTipR, gs[0].depth);
    float r1 = mix(uTrunkR, uTipR, gs[1].depth);
    vec3 axis = normalize(p1 - p0 + vec3(1e-6));
    vec3 s0 = normalize(cross(axis, uCam - p0));
    vec3 s1 = normalize(cross(axis, uCam - p1));
    emit(p0, s0, r0, gs[0].depth,  1.0);
    emit(p0, s0, r0, gs[0].depth, -1.0);
    emit(p1, s1, r1, gs[1].depth,  1.0);
    emit(p1, s1, r1, gs[1].depth, -1.0);
    EndPrimitive();
}";

    private const string TubeFS = @"#version 330 core
in float vSide;
in float vDepth;
in vec3  vNormal;
uniform vec3 uLight;
out vec4 FragColor;
void main(){
    float cyl = sqrt(max(0.0, 1.0 - vSide * vSide));   // round cross-section
    vec3 trunk = vec3(0.32, 0.17, 0.10);
    vec3 mid   = vec3(0.55, 0.20, 0.62);
    vec3 tip   = vec3(1.00, 0.45, 0.92);
    vec3 col = mix(trunk, mid, smoothstep(0.0, 0.5, vDepth));
    col = mix(col, tip, smoothstep(0.5, 1.0, vDepth));
    float lit = 0.45 + 0.55 * clamp(dot(normalize(vNormal), normalize(uLight)) * 0.5 + 0.5, 0.0, 1.0);
    FragColor = vec4(col * (0.30 + 0.70 * cyl) * lit, 1.0);
}";

    private const string TipVS = @"#version 330 core
layout(location=0) in vec3 aPos;
uniform mat4  uMVP;
uniform float uTime;
uniform float uSize;
out float vTw;
void main(){
    vec4 cp = uMVP * vec4(aPos, 1.0);
    gl_Position = cp;
    float tw = 0.5 + 0.5 * sin(uTime * 2.5 + aPos.x * 9.0 + aPos.y * 7.0 + aPos.z * 5.0);
    gl_PointSize = clamp(uSize / cp.w, 1.5, 26.0) * (0.6 + 0.8 * tw);
    vTw = tw;
}";

    private const string TipFS = @"#version 330 core
in float vTw;
out vec4 FragColor;
void main(){
    float r = length(gl_PointCoord - 0.5) * 2.0;
    float a = smoothstep(1.0, 0.0, r);
    vec3 col = mix(vec3(1.0, 0.55, 0.95), vec3(1.0), 0.5);
    FragColor = vec4(col, a * (0.35 + 0.65 * vTw) * 0.5);
}";

    private const string QuadVS = @"#version 330 core
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aUV;
uniform vec2 uScale;
out vec2 vUV;
void main(){ vUV = aUV; gl_Position = vec4(aPos * uScale, 0.0, 1.0); }";

    private const string GradFS = @"#version 330 core
in vec2 vUV;
out vec4 FragColor;
void main(){
    float d = length(vUV - vec2(0.5, 0.55));
    vec3 c = mix(vec3(0.035, 0.04, 0.075), vec3(0.005, 0.005, 0.01), smoothstep(0.0, 0.85, d));
    FragColor = vec4(c, 1.0);
}";

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
