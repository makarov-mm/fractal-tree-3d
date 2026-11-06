// OpenGL 3.3 core: the handful of entry points the demo uses. The OpenGL 1.1
// functions come straight from opengl32.dll; everything newer is fetched with
// wglGetProcAddress once a context is current and called through unmanaged
// function pointers (no delegates, no marshalling per call).
using System.Runtime.InteropServices;
using System.Text;

namespace Makarov.Tree3D;

internal static unsafe partial class GL
{
    public const uint COLOR_BUFFER_BIT = 0x4000, DEPTH_BUFFER_BIT = 0x0100;
    public const uint DEPTH_TEST = 0x0B71, BLEND = 0x0BE2, MULTISAMPLE = 0x809D, PROGRAM_POINT_SIZE = 0x8642;
    public const uint FLOAT = 0x1406;
    public const uint ARRAY_BUFFER = 0x8892, STATIC_DRAW = 0x88E4;
    public const uint VERTEX_SHADER = 0x8B31, FRAGMENT_SHADER = 0x8B30, GEOMETRY_SHADER = 0x8DD9;
    public const uint COMPILE_STATUS = 0x8B81, LINK_STATUS = 0x8B82;
    public const uint POINTS = 0x0000, LINES = 0x0001, TRIANGLE_STRIP = 0x0005;
    public const uint SRC_ALPHA = 0x0302, ONE = 1;

    [LibraryImport("opengl32.dll")] public static partial void glClear(uint mask);
    [LibraryImport("opengl32.dll")] public static partial void glClearColor(float r, float g, float b, float a);
    [LibraryImport("opengl32.dll")] public static partial void glViewport(int x, int y, int w, int h);
    [LibraryImport("opengl32.dll")] public static partial void glEnable(uint cap);
    [LibraryImport("opengl32.dll")] public static partial void glDisable(uint cap);
    [LibraryImport("opengl32.dll")] public static partial void glBlendFunc(uint src, uint dst);
    [LibraryImport("opengl32.dll")] public static partial void glDrawArrays(uint mode, int first, int count);
    [LibraryImport("opengl32.dll")] public static partial void glDepthMask(byte flag);

    public static delegate* unmanaged[Stdcall]<uint, uint> glCreateShader;
    public static delegate* unmanaged[Stdcall]<uint, int, byte**, int*, void> glShaderSource;
    public static delegate* unmanaged[Stdcall]<uint, void> glCompileShader;
    public static delegate* unmanaged[Stdcall]<uint, uint, int*, void> glGetShaderiv;
    public static delegate* unmanaged[Stdcall]<uint, int, int*, byte*, void> glGetShaderInfoLog;
    public static delegate* unmanaged[Stdcall]<uint> glCreateProgram;
    public static delegate* unmanaged[Stdcall]<uint, uint, void> glAttachShader;
    public static delegate* unmanaged[Stdcall]<uint, void> glLinkProgram;
    public static delegate* unmanaged[Stdcall]<uint, uint, int*, void> glGetProgramiv;
    public static delegate* unmanaged[Stdcall]<uint, int, int*, byte*, void> glGetProgramInfoLog;
    public static delegate* unmanaged[Stdcall]<uint, void> glUseProgram;
    public static delegate* unmanaged[Stdcall]<uint, void> glDeleteShader;
    public static delegate* unmanaged[Stdcall]<uint, byte*, int> glGetUniformLocation;
    public static delegate* unmanaged[Stdcall]<int, float, void> glUniform1f;
    public static delegate* unmanaged[Stdcall]<int, float, float, void> glUniform2f;
    public static delegate* unmanaged[Stdcall]<int, float, float, float, void> glUniform3f;
    public static delegate* unmanaged[Stdcall]<int, int, byte, float*, void> glUniformMatrix4fv;
    public static delegate* unmanaged[Stdcall]<int, uint*, void> glGenVertexArrays;
    public static delegate* unmanaged[Stdcall]<int, uint*, void> glGenBuffers;
    public static delegate* unmanaged[Stdcall]<uint, void> glBindVertexArray;
    public static delegate* unmanaged[Stdcall]<uint, uint, void> glBindBuffer;
    public static delegate* unmanaged[Stdcall]<uint, nint, void*, uint, void> glBufferData;
    public static delegate* unmanaged[Stdcall]<uint, int, uint, byte, int, nint, void> glVertexAttribPointer;
    public static delegate* unmanaged[Stdcall]<uint, void> glEnableVertexAttribArray;
    public static delegate* unmanaged[Stdcall]<int, int> wglSwapIntervalEXT; // null if unsupported

    public static void Load()
    {
        glCreateShader = (delegate* unmanaged[Stdcall]<uint, uint>)Proc("glCreateShader");
        glShaderSource = (delegate* unmanaged[Stdcall]<uint, int, byte**, int*, void>)Proc("glShaderSource");
        glCompileShader = (delegate* unmanaged[Stdcall]<uint, void>)Proc("glCompileShader");
        glGetShaderiv = (delegate* unmanaged[Stdcall]<uint, uint, int*, void>)Proc("glGetShaderiv");
        glGetShaderInfoLog = (delegate* unmanaged[Stdcall]<uint, int, int*, byte*, void>)Proc("glGetShaderInfoLog");
        glCreateProgram = (delegate* unmanaged[Stdcall]<uint>)Proc("glCreateProgram");
        glAttachShader = (delegate* unmanaged[Stdcall]<uint, uint, void>)Proc("glAttachShader");
        glLinkProgram = (delegate* unmanaged[Stdcall]<uint, void>)Proc("glLinkProgram");
        glGetProgramiv = (delegate* unmanaged[Stdcall]<uint, uint, int*, void>)Proc("glGetProgramiv");
        glGetProgramInfoLog = (delegate* unmanaged[Stdcall]<uint, int, int*, byte*, void>)Proc("glGetProgramInfoLog");
        glUseProgram = (delegate* unmanaged[Stdcall]<uint, void>)Proc("glUseProgram");
        glDeleteShader = (delegate* unmanaged[Stdcall]<uint, void>)Proc("glDeleteShader");
        glGetUniformLocation = (delegate* unmanaged[Stdcall]<uint, byte*, int>)Proc("glGetUniformLocation");
        glUniform1f = (delegate* unmanaged[Stdcall]<int, float, void>)Proc("glUniform1f");
        glUniform2f = (delegate* unmanaged[Stdcall]<int, float, float, void>)Proc("glUniform2f");
        glUniform3f = (delegate* unmanaged[Stdcall]<int, float, float, float, void>)Proc("glUniform3f");
        glUniformMatrix4fv = (delegate* unmanaged[Stdcall]<int, int, byte, float*, void>)Proc("glUniformMatrix4fv");
        glGenVertexArrays = (delegate* unmanaged[Stdcall]<int, uint*, void>)Proc("glGenVertexArrays");
        glGenBuffers = (delegate* unmanaged[Stdcall]<int, uint*, void>)Proc("glGenBuffers");
        glBindVertexArray = (delegate* unmanaged[Stdcall]<uint, void>)Proc("glBindVertexArray");
        glBindBuffer = (delegate* unmanaged[Stdcall]<uint, uint, void>)Proc("glBindBuffer");
        glBufferData = (delegate* unmanaged[Stdcall]<uint, nint, void*, uint, void>)Proc("glBufferData");
        glVertexAttribPointer = (delegate* unmanaged[Stdcall]<uint, int, uint, byte, int, nint, void>)Proc("glVertexAttribPointer");
        glEnableVertexAttribArray = (delegate* unmanaged[Stdcall]<uint, void>)Proc("glEnableVertexAttribArray");
        wglSwapIntervalEXT = (delegate* unmanaged[Stdcall]<int, int>)TryProc("wglSwapIntervalEXT");
    }

    // wglGetProcAddress reports failure as 0, 1, 2, 3 or -1 depending on the driver.
    public static nint TryProc(string name)
    {
        nint p = Win32.wglGetProcAddress(name);
        return p is 0 or 1 or 2 or 3 or -1 ? 0 : p;
    }

    private static nint Proc(string name)
    {
        nint p = TryProc(name);
        return p != 0 ? p : throw new InvalidOperationException($"OpenGL function not available: {name}");
    }

    // ---- helpers -----------------------------------------------------------

    public static uint GenVertexArray()
    {
        uint id;
        glGenVertexArrays(1, &id);
        return id;
    }

    public static uint GenBuffer()
    {
        uint id;
        glGenBuffers(1, &id);
        return id;
    }

    // Uploads a span of blittable vertices to the bound ARRAY_BUFFER.
    public static void BufferData<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        fixed (T* p = data)
            glBufferData(ARRAY_BUFFER, data.Length * sizeof(T), p, STATIC_DRAW);
    }

    public static void VertexAttrib(uint index, int components, int stride, int offset)
    {
        glVertexAttribPointer(index, components, FLOAT, 0, stride, offset);
        glEnableVertexAttribArray(index);
    }

    // `name` is a null-terminated UTF-8 literal, e.g. "uMVP"u8.
    public static int Uniform(uint program, ReadOnlySpan<byte> name)
    {
        fixed (byte* p = name)
            return glGetUniformLocation(program, p);
    }

    public static void Uniform(int location, System.Numerics.Vector3 v) => glUniform3f(location, v.X, v.Y, v.Z);

    public static void Uniform(int location, in System.Numerics.Matrix4x4 m)
    {
        // Matrix4x4 is stored row-major with the translation in row 4, which is
        // byte for byte what OpenGL reads as a column-major matrix.
        fixed (System.Numerics.Matrix4x4* p = &m)
            glUniformMatrix4fv(location, 1, 0, (float*)p);
    }

    public static uint BuildProgram(string vertex, string fragment, string? geometry = null)
    {
        uint vs = Compile(VERTEX_SHADER, vertex);
        uint fs = Compile(FRAGMENT_SHADER, fragment);
        uint gs = geometry is null ? 0 : Compile(GEOMETRY_SHADER, geometry);

        uint program = glCreateProgram();
        glAttachShader(program, vs);
        glAttachShader(program, fs);
        if (gs != 0) glAttachShader(program, gs);
        glLinkProgram(program);

        int ok;
        glGetProgramiv(program, LINK_STATUS, &ok);
        if (ok == 0) throw new InvalidOperationException("Shader link error: " + InfoLog(program, false));

        glDeleteShader(vs);
        glDeleteShader(fs);
        if (gs != 0) glDeleteShader(gs);
        return program;
    }

    private static uint Compile(uint type, string source)
    {
        uint shader = glCreateShader(type);
        byte[] utf8 = Encoding.UTF8.GetBytes(source);
        int length = utf8.Length;
        fixed (byte* p = utf8)
        {
            byte** sources = stackalloc byte*[1];
            sources[0] = p;
            glShaderSource(shader, 1, sources, &length);
        }
        glCompileShader(shader);

        int ok;
        glGetShaderiv(shader, COMPILE_STATUS, &ok);
        if (ok == 0)
        {
            string kind = type switch { VERTEX_SHADER => "vertex", GEOMETRY_SHADER => "geometry", _ => "fragment" };
            throw new InvalidOperationException($"{kind} shader compile error: " + InfoLog(shader, true));
        }
        return shader;
    }

    private static string InfoLog(uint obj, bool shader)
    {
        byte* log = stackalloc byte[4096];
        int length;
        if (shader) glGetShaderInfoLog(obj, 4096, &length, log);
        else glGetProgramInfoLog(obj, 4096, &length, log);
        return Encoding.UTF8.GetString(log, Math.Max(0, length));
    }
}
