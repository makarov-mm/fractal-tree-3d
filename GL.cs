using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Makarov.Tree3D;

internal static class GL
{
    public const uint GL_COLOR_BUFFER_BIT = 0x4000;
    public const uint GL_DEPTH_BUFFER_BIT = 0x0100;
    public const uint GL_DEPTH_TEST = 0x0B71;
    public const uint GL_FLOAT = 0x1406;
    public const uint GL_ARRAY_BUFFER = 0x8892;
    public const uint GL_STATIC_DRAW = 0x88E4;
    public const uint GL_VERTEX_SHADER = 0x8B31;
    public const uint GL_FRAGMENT_SHADER = 0x8B30;
    public const uint GL_GEOMETRY_SHADER = 0x8DD9;
    public const uint GL_COMPILE_STATUS = 0x8B81;
    public const uint GL_LINK_STATUS = 0x8B82;
    public const uint GL_LINES = 0x0001;
    public const uint GL_POINTS = 0x0000;
    public const uint GL_TRIANGLE_STRIP = 0x0005;
    public const uint GL_BLEND = 0x0BE2;
    public const uint GL_SRC_ALPHA = 0x0302;
    public const uint GL_ONE = 1;
    public const uint GL_VERTEX_PROGRAM_POINT_SIZE = 0x8642;

    [DllImport("opengl32.dll")] public static extern void glClear(uint mask);
    [DllImport("opengl32.dll")] public static extern void glClearColor(float r, float g, float b, float a);
    [DllImport("opengl32.dll")] public static extern void glViewport(int x, int y, int w, int h);
    [DllImport("opengl32.dll")] public static extern void glEnable(uint cap);
    [DllImport("opengl32.dll")] public static extern void glDisable(uint cap);
    [DllImport("opengl32.dll")] public static extern void glBlendFunc(uint s, uint d);
    [DllImport("opengl32.dll")] public static extern void glDrawArrays(uint mode, int first, int count);
    [DllImport("opengl32.dll")] public static extern void glDepthMask([MarshalAs(UnmanagedType.I1)] bool flag);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate uint GlCreateShaderD(uint type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlShaderSourceD(uint s, int c, IntPtr[] str, IntPtr len);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlCompileShaderD(uint s);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlGetShaderivD(uint s, uint p, ref int v);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlGetShaderInfoLogD(uint s, int max, ref int len, byte[] log);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate uint GlCreateProgramD();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlAttachShaderD(uint p, uint s);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlLinkProgramD(uint p);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlGetProgramivD(uint p, uint pn, ref int v);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlGetProgramInfoLogD(uint p, int max, ref int len, byte[] log);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlUseProgramD(uint p);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlDeleteShaderD(uint s);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GlGetUniformLocationD(uint p, byte[] name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlUniform1fD(int loc, float v);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlUniform2fD(int loc, float a, float b);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlUniform3fD(int loc, float a, float b, float c);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlUniformMatrix4fvD(int loc, int count, byte transpose, float[] value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlGenD(int n, ref uint id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlBindVertexArrayD(uint a);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlBindBufferD(uint t, uint b);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlBufferDataD(uint t, IntPtr size, IntPtr data, uint usage);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlVertexAttribPointerD(uint i, int size, uint type, byte norm, int stride, IntPtr ptr);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GlEnableVertexAttribArrayD(uint i);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int WglSwapIntervalEXTD(int interval);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate IntPtr WglCreateContextAttribsARB(IntPtr hdc, IntPtr share, int[] attribs);

    public static GlCreateShaderD glCreateShader;
    public static GlShaderSourceD glShaderSource;
    public static GlCompileShaderD glCompileShader;
    public static GlGetShaderivD glGetShaderiv;
    public static GlGetShaderInfoLogD glGetShaderInfoLog;
    public static GlCreateProgramD glCreateProgram;
    public static GlAttachShaderD glAttachShader;
    public static GlLinkProgramD glLinkProgram;
    public static GlGetProgramivD glGetProgramiv;
    public static GlGetProgramInfoLogD glGetProgramInfoLog;
    public static GlUseProgramD glUseProgram;
    public static GlDeleteShaderD glDeleteShader;
    public static GlGetUniformLocationD glGetUniformLocation;
    public static GlUniform1fD glUniform1f;
    public static GlUniform2fD glUniform2f;
    public static GlUniform3fD glUniform3f;
    public static GlUniformMatrix4fvD glUniformMatrix4fv;
    public static GlGenD glGenVertexArrays;
    public static GlGenD glGenBuffers;
    public static GlBindVertexArrayD glBindVertexArray;
    public static GlBindBufferD glBindBuffer;
    public static GlBufferDataD glBufferData;
    public static GlVertexAttribPointerD glVertexAttribPointer;
    public static GlEnableVertexAttribArrayD glEnableVertexAttribArray;
    public static WglSwapIntervalEXTD wglSwapIntervalEXT;

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
        glCreateShader = Get<GlCreateShaderD>("glCreateShader");
        glShaderSource = Get<GlShaderSourceD>("glShaderSource");
        glCompileShader = Get<GlCompileShaderD>("glCompileShader");
        glGetShaderiv = Get<GlGetShaderivD>("glGetShaderiv");
        glGetShaderInfoLog = Get<GlGetShaderInfoLogD>("glGetShaderInfoLog");
        glCreateProgram = Get<GlCreateProgramD>("glCreateProgram");
        glAttachShader = Get<GlAttachShaderD>("glAttachShader");
        glLinkProgram = Get<GlLinkProgramD>("glLinkProgram");
        glGetProgramiv = Get<GlGetProgramivD>("glGetProgramiv");
        glGetProgramInfoLog = Get<GlGetProgramInfoLogD>("glGetProgramInfoLog");
        glUseProgram = Get<GlUseProgramD>("glUseProgram");
        glDeleteShader = Get<GlDeleteShaderD>("glDeleteShader");
        glGetUniformLocation = Get<GlGetUniformLocationD>("glGetUniformLocation");
        glUniform1f = Get<GlUniform1fD>("glUniform1f");
        glUniform2f = Get<GlUniform2fD>("glUniform2f");
        glUniform3f = Get<GlUniform3fD>("glUniform3f");
        glUniformMatrix4fv = Get<GlUniformMatrix4fvD>("glUniformMatrix4fv");
        glGenVertexArrays = Get<GlGenD>("glGenVertexArrays");
        glGenBuffers = Get<GlGenD>("glGenBuffers");
        glBindVertexArray = Get<GlBindVertexArrayD>("glBindVertexArray");
        glBindBuffer = Get<GlBindBufferD>("glBindBuffer");
        glBufferData = Get<GlBufferDataD>("glBufferData");
        glVertexAttribPointer = Get<GlVertexAttribPointerD>("glVertexAttribPointer");
        glEnableVertexAttribArray = Get<GlEnableVertexAttribArrayD>("glEnableVertexAttribArray");

        IntPtr swap = Win.wglGetProcAddress("wglSwapIntervalEXT");
        if (swap != IntPtr.Zero && (long)swap > 3)
            wglSwapIntervalEXT = Marshal.GetDelegateForFunctionPointer<WglSwapIntervalEXTD>(swap);
    }
}