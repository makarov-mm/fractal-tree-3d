// Tree3D — a volumetric 3D fractal tree on raw Win32 + WGL + OpenGL 3.3 core.
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
// Author: portfolio piece, no-library style (P/Invoke only).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

internal static class Program
{
    // ---- tree parameters -------------------------------------------------
    const int   MaxDepth  = 8;
    const int   Children  = 3;
    const float Ratio     = 0.74f;
    const float TiltDeg   = 36.0f;
    const float TrunkR    = 0.030f;   // tube radius at the trunk (normalized units)
    const float TipR      = 0.0035f;  // tube radius at the twigs

    static readonly List<float> Segs = new();   // x,y,z,depth,sway per vertex (2 per segment)
    static readonly List<float> Tips = new();   // x,y,z per leaf
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
            throw new Exception("RegisterClassEx failed: " + Marshal.GetLastWin32Error());

        IntPtr hwnd = Win.CreateWindowExW(
            0, cls, "3D Fractal Tree — C#/OpenGL  (drag = rotate, wheel = zoom)",
            Win.WS_OVERLAPPEDWINDOW | Win.WS_VISIBLE,
            Win.CW_USEDEFAULT, Win.CW_USEDEFAULT, _w, _h,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
            throw new Exception("CreateWindowEx failed: " + Marshal.GetLastWin32Error());

        IntPtr hdc = Win.GetDC(hwnd);
        IntPtr ctx = CreateGLContext(hdc);
        GL.Load();
        if (GL.wglSwapIntervalEXT != null) GL.wglSwapIntervalEXT(1);

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
            if (!_dragging) _yaw += 0.0035f;   // gentle idle spin

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
                int lp = (int)(long)lParam;
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

    static int Short(IntPtr lParam, int shift) => (short)((((long)lParam) >> shift) & 0xFFFF);

    // ---- tree generation (3D) -------------------------------------------
    static void BuildTree()
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

    static void AddSeg(Vector3 a, int da, Vector3 b, int db)
    {
        AddV(a, da); AddV(b, db);
    }

    static void AddV(Vector3 p, int depth)
    {
        float d = (float)depth / MaxDepth;
        float sway = MathF.Pow(d, 1.5f);
        Segs.Add(p.X); Segs.Add(p.Y); Segs.Add(p.Z); Segs.Add(d); Segs.Add(sway);
    }

    static void AddTip(Vector3 p) { Tips.Add(p.X); Tips.Add(p.Y); Tips.Add(p.Z); }

    static void Normalize()
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

    // ---- matrices (column-major, GL convention) --------------------------
    static float[] Perspective(float fovy, float aspect, float n, float f)
    {
        float t = 1f / MathF.Tan(fovy * 0.5f);
        var m = new float[16];
        m[0] = t / aspect; m[5] = t;
        m[10] = (f + n) / (n - f); m[11] = -1f;
        m[14] = (2f * f * n) / (n - f);
        return m;
    }

    static float[] LookAt(Vector3 eye, Vector3 center, Vector3 up)
    {
        var f = Vector3.Normalize(center - eye);
        var s = Vector3.Normalize(Vector3.Cross(f, up));
        var u = Vector3.Cross(s, f);
        return new float[]
        {
            s.X, u.X, -f.X, 0f,
            s.Y, u.Y, -f.Y, 0f,
            s.Z, u.Z, -f.Z, 0f,
            -Vector3.Dot(s, eye), -Vector3.Dot(u, eye), Vector3.Dot(f, eye), 1f,
        };
    }

    static float[] Mul(float[] a, float[] b)
    {
        var r = new float[16];
        for (int col = 0; col < 4; col++)
            for (int row = 0; row < 4; row++)
            {
                float s = 0f;
                for (int k = 0; k < 4; k++) s += a[k * 4 + row] * b[col * 4 + k];
                r[col * 4 + row] = s;
            }
        return r;
    }

    // ---- GPU helpers -----------------------------------------------------
    static uint MakeTubeVao()
    {
        uint vao = 0, vbo = 0;
        GL.glGenVertexArrays(1, ref vao);
        GL.glBindVertexArray(vao);
        GL.glGenBuffers(1, ref vbo);
        GL.glBindBuffer(GL.GL_ARRAY_BUFFER, vbo);
        Upload(Segs.ToArray());
        int stride = 5 * sizeof(float);
        GL.glVertexAttribPointer(0, 3, GL.GL_FLOAT, 0, stride, (IntPtr)0);
        GL.glEnableVertexAttribArray(0);
        GL.glVertexAttribPointer(1, 1, GL.GL_FLOAT, 0, stride, (IntPtr)(3 * sizeof(float)));
        GL.glEnableVertexAttribArray(1);
        GL.glVertexAttribPointer(2, 1, GL.GL_FLOAT, 0, stride, (IntPtr)(4 * sizeof(float)));
        GL.glEnableVertexAttribArray(2);
        return vao;
    }

    static uint MakeTipVao()
    {
        uint vao = 0, vbo = 0;
        GL.glGenVertexArrays(1, ref vao);
        GL.glBindVertexArray(vao);
        GL.glGenBuffers(1, ref vbo);
        GL.glBindBuffer(GL.GL_ARRAY_BUFFER, vbo);
        Upload(Tips.ToArray());
        GL.glVertexAttribPointer(0, 3, GL.GL_FLOAT, 0, 3 * sizeof(float), (IntPtr)0);
        GL.glEnableVertexAttribArray(0);
        return vao;
    }

    static uint MakeQuadVao()
    {
        float[] q = { -1f,-1f,0f,0f,  1f,-1f,1f,0f,  -1f,1f,0f,1f,  1f,1f,1f,1f };
        uint vao = 0, vbo = 0;
        GL.glGenVertexArrays(1, ref vao);
        GL.glBindVertexArray(vao);
        GL.glGenBuffers(1, ref vbo);
        GL.glBindBuffer(GL.GL_ARRAY_BUFFER, vbo);
        Upload(q);
        int stride = 4 * sizeof(float);
        GL.glVertexAttribPointer(0, 2, GL.GL_FLOAT, 0, stride, (IntPtr)0);
        GL.glEnableVertexAttribArray(0);
        GL.glVertexAttribPointer(1, 2, GL.GL_FLOAT, 0, stride, (IntPtr)(2 * sizeof(float)));
        GL.glEnableVertexAttribArray(1);
        return vao;
    }

    static void Upload(float[] data)
    {
        GCHandle h = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            GL.glBufferData(GL.GL_ARRAY_BUFFER, (IntPtr)(data.Length * sizeof(float)),
                            h.AddrOfPinnedObject(), GL.GL_STATIC_DRAW);
        }
        finally { h.Free(); }
    }

    // ---- shaders ---------------------------------------------------------
    const string TubeVS = @"#version 330 core
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

    const string TubeGS = @"#version 330 core
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

    const string TubeFS = @"#version 330 core
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

    const string TipVS = @"#version 330 core
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

    const string TipFS = @"#version 330 core
in float vTw;
out vec4 FragColor;
void main(){
    float r = length(gl_PointCoord - 0.5) * 2.0;
    float a = smoothstep(1.0, 0.0, r);
    vec3 col = mix(vec3(1.0, 0.55, 0.95), vec3(1.0), 0.5);
    FragColor = vec4(col, a * (0.35 + 0.65 * vTw) * 0.5);
}";

    const string QuadVS = @"#version 330 core
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aUV;
uniform vec2 uScale;
out vec2 vUV;
void main(){ vUV = aUV; gl_Position = vec4(aPos * uScale, 0.0, 1.0); }";

    const string GradFS = @"#version 330 core
in vec2 vUV;
out vec4 FragColor;
void main(){
    float d = length(vUV - vec2(0.5, 0.55));
    vec3 c = mix(vec3(0.035, 0.04, 0.075), vec3(0.005, 0.005, 0.01), smoothstep(0.0, 0.85, d));
    FragColor = vec4(c, 1.0);
}";

    static uint BuildProgram(string vsSrc, string fsSrc, string gsSrc)
    {
        uint vs = Compile(GL.GL_VERTEX_SHADER, vsSrc);
        uint fs = Compile(GL.GL_FRAGMENT_SHADER, fsSrc);
        uint p  = GL.glCreateProgram();
        GL.glAttachShader(p, vs);
        GL.glAttachShader(p, fs);
        uint gs = 0;
        if (gsSrc != null)
        {
            gs = Compile(GL.GL_GEOMETRY_SHADER, gsSrc);
            GL.glAttachShader(p, gs);
        }
        GL.glLinkProgram(p);
        int ok = 0; GL.glGetProgramiv(p, GL.GL_LINK_STATUS, ref ok);
        if (ok == 0)
        {
            var log = new byte[2048]; int len = 0;
            GL.glGetProgramInfoLog(p, log.Length, ref len, log);
            throw new Exception("Link error: " + System.Text.Encoding.ASCII.GetString(log, 0, len));
        }
        GL.glDeleteShader(vs); GL.glDeleteShader(fs);
        if (gs != 0) GL.glDeleteShader(gs);
        return p;
    }

    static uint Compile(uint type, string src)
    {
        uint sh = GL.glCreateShader(type);
        IntPtr str = Marshal.StringToHGlobalAnsi(src);
        try { GL.glShaderSource(sh, 1, new[] { str }, IntPtr.Zero); }
        finally { Marshal.FreeHGlobal(str); }
        GL.glCompileShader(sh);
        int ok = 0; GL.glGetShaderiv(sh, GL.GL_COMPILE_STATUS, ref ok);
        if (ok == 0)
        {
            var log = new byte[2048]; int len = 0;
            GL.glGetShaderInfoLog(sh, log.Length, ref len, log);
            throw new Exception("Compile error: " + System.Text.Encoding.ASCII.GetString(log, 0, len));
        }
        return sh;
    }

    static byte[] Ascii(string s)
    {
        var b = new byte[s.Length + 1];
        System.Text.Encoding.ASCII.GetBytes(s, 0, s.Length, b, 0);
        return b;
    }

    static IntPtr CreateGLContext(IntPtr hdc)
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
            int[] attribs = { 0x2091, 3, 0x2092, 3, 0x9126, 0x0001, 0 };
            IntPtr core = create(hdc, IntPtr.Zero, attribs);
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

// =========================================================================
//  OpenGL entry points
// =========================================================================
internal static class GL
{
    public const uint GL_COLOR_BUFFER_BIT          = 0x4000;
    public const uint GL_DEPTH_BUFFER_BIT          = 0x0100;
    public const uint GL_DEPTH_TEST                = 0x0B71;
    public const uint GL_FLOAT                     = 0x1406;
    public const uint GL_ARRAY_BUFFER              = 0x8892;
    public const uint GL_STATIC_DRAW               = 0x88E4;
    public const uint GL_VERTEX_SHADER             = 0x8B31;
    public const uint GL_FRAGMENT_SHADER           = 0x8B30;
    public const uint GL_GEOMETRY_SHADER           = 0x8DD9;
    public const uint GL_COMPILE_STATUS            = 0x8B81;
    public const uint GL_LINK_STATUS               = 0x8B82;
    public const uint GL_LINES                     = 0x0001;
    public const uint GL_POINTS                    = 0x0000;
    public const uint GL_TRIANGLE_STRIP            = 0x0005;
    public const uint GL_BLEND                     = 0x0BE2;
    public const uint GL_SRC_ALPHA                 = 0x0302;
    public const uint GL_ONE                       = 1;
    public const uint GL_VERTEX_PROGRAM_POINT_SIZE = 0x8642;

    [DllImport("opengl32.dll")] public static extern void glClear(uint mask);
    [DllImport("opengl32.dll")] public static extern void glClearColor(float r, float g, float b, float a);
    [DllImport("opengl32.dll")] public static extern void glViewport(int x, int y, int w, int h);
    [DllImport("opengl32.dll")] public static extern void glEnable(uint cap);
    [DllImport("opengl32.dll")] public static extern void glDisable(uint cap);
    [DllImport("opengl32.dll")] public static extern void glBlendFunc(uint s, uint d);
    [DllImport("opengl32.dll")] public static extern void glDrawArrays(uint mode, int first, int count);
    [DllImport("opengl32.dll")] public static extern void glDepthMask([MarshalAs(UnmanagedType.I1)] bool flag);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate uint   GlCreateShaderD(uint type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlShaderSourceD(uint s, int c, IntPtr[] str, IntPtr len);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlCompileShaderD(uint s);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlGetShaderivD(uint s, uint p, ref int v);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlGetShaderInfoLogD(uint s, int max, ref int len, byte[] log);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate uint   GlCreateProgramD();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlAttachShaderD(uint p, uint s);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlLinkProgramD(uint p);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlGetProgramivD(uint p, uint pn, ref int v);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlGetProgramInfoLogD(uint p, int max, ref int len, byte[] log);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlUseProgramD(uint p);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlDeleteShaderD(uint s);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int    GlGetUniformLocationD(uint p, byte[] name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlUniform1fD(int loc, float v);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlUniform2fD(int loc, float a, float b);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlUniform3fD(int loc, float a, float b, float c);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlUniformMatrix4fvD(int loc, int count, byte transpose, float[] value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlGenD(int n, ref uint id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlBindVertexArrayD(uint a);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlBindBufferD(uint t, uint b);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlBufferDataD(uint t, IntPtr size, IntPtr data, uint usage);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlVertexAttribPointerD(uint i, int size, uint type, byte norm, int stride, IntPtr ptr);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void   GlEnableVertexAttribArrayD(uint i);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int    WglSwapIntervalEXTD(int interval);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate IntPtr WglCreateContextAttribsARB(IntPtr hdc, IntPtr share, int[] attribs);

    public static GlCreateShaderD            glCreateShader;
    public static GlShaderSourceD            glShaderSource;
    public static GlCompileShaderD           glCompileShader;
    public static GlGetShaderivD             glGetShaderiv;
    public static GlGetShaderInfoLogD        glGetShaderInfoLog;
    public static GlCreateProgramD           glCreateProgram;
    public static GlAttachShaderD            glAttachShader;
    public static GlLinkProgramD             glLinkProgram;
    public static GlGetProgramivD            glGetProgramiv;
    public static GlGetProgramInfoLogD       glGetProgramInfoLog;
    public static GlUseProgramD              glUseProgram;
    public static GlDeleteShaderD            glDeleteShader;
    public static GlGetUniformLocationD      glGetUniformLocation;
    public static GlUniform1fD               glUniform1f;
    public static GlUniform2fD               glUniform2f;
    public static GlUniform3fD               glUniform3f;
    public static GlUniformMatrix4fvD        glUniformMatrix4fv;
    public static GlGenD                     glGenVertexArrays;
    public static GlGenD                     glGenBuffers;
    public static GlBindVertexArrayD         glBindVertexArray;
    public static GlBindBufferD              glBindBuffer;
    public static GlBufferDataD              glBufferData;
    public static GlVertexAttribPointerD     glVertexAttribPointer;
    public static GlEnableVertexAttribArrayD glEnableVertexAttribArray;
    public static WglSwapIntervalEXTD        wglSwapIntervalEXT;

    static T Get<T>(string name) where T : Delegate
    {
        IntPtr p = Win.wglGetProcAddress(name);
        long v = (long)p;
        if (p == IntPtr.Zero || v == 1 || v == 2 || v == 3 || v == -1)
            throw new Exception("Failed to load GL function: " + name);
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    public static void Load()
    {
        glCreateShader            = Get<GlCreateShaderD>("glCreateShader");
        glShaderSource            = Get<GlShaderSourceD>("glShaderSource");
        glCompileShader           = Get<GlCompileShaderD>("glCompileShader");
        glGetShaderiv             = Get<GlGetShaderivD>("glGetShaderiv");
        glGetShaderInfoLog        = Get<GlGetShaderInfoLogD>("glGetShaderInfoLog");
        glCreateProgram           = Get<GlCreateProgramD>("glCreateProgram");
        glAttachShader            = Get<GlAttachShaderD>("glAttachShader");
        glLinkProgram             = Get<GlLinkProgramD>("glLinkProgram");
        glGetProgramiv            = Get<GlGetProgramivD>("glGetProgramiv");
        glGetProgramInfoLog       = Get<GlGetProgramInfoLogD>("glGetProgramInfoLog");
        glUseProgram              = Get<GlUseProgramD>("glUseProgram");
        glDeleteShader            = Get<GlDeleteShaderD>("glDeleteShader");
        glGetUniformLocation      = Get<GlGetUniformLocationD>("glGetUniformLocation");
        glUniform1f               = Get<GlUniform1fD>("glUniform1f");
        glUniform2f               = Get<GlUniform2fD>("glUniform2f");
        glUniform3f               = Get<GlUniform3fD>("glUniform3f");
        glUniformMatrix4fv        = Get<GlUniformMatrix4fvD>("glUniformMatrix4fv");
        glGenVertexArrays         = Get<GlGenD>("glGenVertexArrays");
        glGenBuffers              = Get<GlGenD>("glGenBuffers");
        glBindVertexArray         = Get<GlBindVertexArrayD>("glBindVertexArray");
        glBindBuffer              = Get<GlBindBufferD>("glBindBuffer");
        glBufferData              = Get<GlBufferDataD>("glBufferData");
        glVertexAttribPointer     = Get<GlVertexAttribPointerD>("glVertexAttribPointer");
        glEnableVertexAttribArray = Get<GlEnableVertexAttribArrayD>("glEnableVertexAttribArray");

        IntPtr swap = Win.wglGetProcAddress("wglSwapIntervalEXT");
        if (swap != IntPtr.Zero && (long)swap > 3)
            wglSwapIntervalEXT = Marshal.GetDelegateForFunctionPointer<WglSwapIntervalEXTD>(swap);
    }
}

// =========================================================================
//  Win32 / GDI / WGL P/Invoke
// =========================================================================
internal static class Win
{
    public const uint CS_VREDRAW = 0x0001, CS_HREDRAW = 0x0002, CS_OWNDC = 0x0020;
    public const uint WS_VISIBLE = 0x10000000, WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const int  CW_USEDEFAULT = unchecked((int)0x80000000);
    public const int  IDC_ARROW = 32512;
    public const uint PM_REMOVE = 0x0001;
    public const uint WM_DESTROY = 0x0002, WM_SIZE = 0x0005, WM_QUIT = 0x0012;
    public const uint WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_MOUSEWHEEL = 0x020A;

    public const uint PFD_DRAW_TO_WINDOW = 0x00000004;
    public const uint PFD_SUPPORT_OPENGL = 0x00000020;
    public const uint PFD_DOUBLEBUFFER   = 0x00000001;
    public const byte PFD_TYPE_RGBA      = 0;
    public const byte PFD_MAIN_PLANE     = 0;

    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint   cbSize;
        public uint   style;
        public IntPtr lpfnWndProc;
        public int    cbClsExtra;
        public int    cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint   message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint   time;
        public int    pt_x;
        public int    pt_y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize;
        public ushort nVersion;
        public uint   dwFlags;
        public byte   iPixelType;
        public byte   cColorBits;
        public byte   cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift;
        public byte   cAlphaBits, cAlphaShift;
        public byte   cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits;
        public byte   cDepthBits, cStencilBits, cAuxBuffers;
        public byte   iLayerType, bReserved;
        public uint   dwLayerMask, dwVisibleMask, dwDamageMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetModuleHandleW(IntPtr lpModuleName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEX wc);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr hInstance, IntPtr param);

    [DllImport("user32.dll")] public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern void   PostQuitMessage(int code);
    [DllImport("user32.dll")] public static extern IntPtr LoadCursorW(IntPtr hInstance, IntPtr lpCursorName);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int    ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("user32.dll")] public static extern bool   GetClientRect(IntPtr hWnd, out RECT rc);

    [DllImport("user32.dll")] public static extern bool   PeekMessageW(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] public static extern bool   TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessageW(ref MSG msg);

    [DllImport("gdi32.dll")] public static extern int  ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR pfd);
    [DllImport("gdi32.dll")] public static extern bool SetPixelFormat(IntPtr hdc, int fmt, ref PIXELFORMATDESCRIPTOR pfd);
    [DllImport("gdi32.dll")] public static extern bool SwapBuffers(IntPtr hdc);

    [DllImport("opengl32.dll")] public static extern IntPtr wglCreateContext(IntPtr hdc);
    [DllImport("opengl32.dll")] public static extern bool   wglMakeCurrent(IntPtr hdc, IntPtr ctx);
    [DllImport("opengl32.dll")] public static extern bool   wglDeleteContext(IntPtr ctx);
    [DllImport("opengl32.dll", CharSet = CharSet.Ansi)]
    public static extern IntPtr wglGetProcAddress(string name);
}
