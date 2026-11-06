// The one window of the demo: class registration, a multisampled pixel
// format, an OpenGL 3.3 core context, the message pump and mouse / keyboard
// input. Single-threaded; the callbacks run inside PumpMessages().
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Makarov.Tree3D;

internal static unsafe class Window
{
    public static int Width { get; private set; }
    public static int Height { get; private set; }
    public static bool Minimized { get; private set; }
    public static int Samples { get; private set; } // MSAA samples actually obtained
    public static bool Dragging { get; private set; } // left button held

    public static Action<int>? KeyDown;        // virtual-key code
    public static Action<int, int>? MouseDrag; // pixels moved with the left button held
    public static Action<int>? MouseWheel;     // wheel delta, 120 per notch

    private const string ClassName = "Tree3DGLWindow";
    private const string DummyClassName = "Tree3DGLDummy";

    private static nint s_hwnd, s_hdc, s_context;
    private static int s_lastX, s_lastY;
    private static Exception? s_callbackError;

    public static void Create(string title, int width, int height, int samples)
    {
        // Per-monitor DPI awareness: the client area gets real pixels instead
        // of a bitmap-scaled window on high-DPI screens. The API appeared in
        // Windows 10 1703; without it the window is simply DPI-unaware.
        try { Win32.SetProcessDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); }
        catch (EntryPointNotFoundException) { }

        nint instance = Win32.GetModuleHandleW(0);
        RegisterClass(instance, ClassName, &WndProc);

        int format = samples > 1 ? ChooseMultisampleFormat(instance, samples) : 0;

        s_hwnd = Win32.CreateWindowExW(0, ClassName, title, Win32.WS_OVERLAPPEDWINDOW | Win32.WS_VISIBLE,
                                       Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT, width, height, 0, 0, instance, 0);
        if (s_hwnd == 0) throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        s_hdc = Win32.GetDC(s_hwnd);

        var pfd = BasicPixelFormat();
        if (format != 0 && Win32.DescribePixelFormat(s_hdc, format, (uint)sizeof(Win32.PIXELFORMATDESCRIPTOR), &pfd) != 0)
        {
            Samples = samples;
        }
        else
        {
            format = Win32.ChoosePixelFormat(s_hdc, &pfd);
            if (format == 0) throw new InvalidOperationException("ChoosePixelFormat failed");
            Samples = 1;
        }
        if (!Win32.SetPixelFormat(s_hdc, format, &pfd)) throw new InvalidOperationException("SetPixelFormat failed");

        s_context = CreateCoreContext(s_hdc);
        GL.Load();
        if (GL.wglSwapIntervalEXT is not null) GL.wglSwapIntervalEXT(1);

        Win32.RECT rc;
        if (Win32.GetClientRect(s_hwnd, &rc))
        {
            Width = Math.Max(1, rc.right - rc.left);
            Height = Math.Max(1, rc.bottom - rc.top);
        }
    }

    public static void SetTitle(string title) => Win32.SetWindowTextW(s_hwnd, title);

    public static void Swap() => Win32.SwapBuffers(s_hdc);

    // Dispatches pending messages; returns false once the window is closing.
    // While minimized it blocks until the next message instead of spinning.
    public static bool PumpMessages()
    {
        if (Minimized) Win32.WaitMessage();
        Win32.MSG msg;
        while (Win32.PeekMessageW(&msg, 0, 0, 0, Win32.PM_REMOVE))
        {
            if (msg.message == Win32.WM_QUIT) return false;
            Win32.TranslateMessage(&msg);
            Win32.DispatchMessageW(&msg);
            if (s_callbackError is { } error)
            {
                s_callbackError = null;
                throw error;
            }
        }
        return true;
    }

    public static void Destroy()
    {
        Win32.wglMakeCurrent(0, 0);
        if (s_context != 0) Win32.wglDeleteContext(s_context);
        if (s_hdc != 0) _ = Win32.ReleaseDC(s_hwnd, s_hdc);
        if (s_hwnd != 0) Win32.DestroyWindow(s_hwnd);
        s_context = s_hdc = s_hwnd = 0;
    }

    // ---- window procedure ---------------------------------------------------

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
            case Win32.WM_SIZE:
                Minimized = (int)wParam == Win32.SIZE_MINIMIZED;
                Width = Math.Max(1, Win32.LoWord(lParam));
                Height = Math.Max(1, Win32.HiWord(lParam));
                return 0;

            case Win32.WM_LBUTTONDOWN:
                Dragging = true;
                s_lastX = Win32.LoWord(lParam);
                s_lastY = Win32.HiWord(lParam);
                return 0;

            case Win32.WM_LBUTTONUP:
                Dragging = false;
                return 0;

            case Win32.WM_MOUSEMOVE:
                if (Dragging)
                {
                    int x = Win32.LoWord(lParam), y = Win32.HiWord(lParam);
                    MouseDrag?.Invoke(x - s_lastX, y - s_lastY);
                    s_lastX = x;
                    s_lastY = y;
                }
                return 0;

            case Win32.WM_MOUSEWHEEL:
                MouseWheel?.Invoke(Win32.HiWord(wParam));
                return 0;

            case Win32.WM_KEYDOWN:
                KeyDown?.Invoke((int)wParam);
                return 0;

            case Win32.WM_DESTROY:
                Win32.PostQuitMessage(0);
                return 0;
            }
        }
        catch (Exception e)
        {
            s_callbackError = e; // rethrown by PumpMessages: exceptions must not cross into Windows
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint DummyProc(nint hwnd, uint msg, nint wParam, nint lParam)
        => Win32.DefWindowProcW(hwnd, msg, wParam, lParam);

    // ---- pixel format and context -----------------------------------------------

    private static void RegisterClass(nint instance, string name, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> proc)
    {
        fixed (char* className = name)
        {
            var wc = new Win32.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(Win32.WNDCLASSEXW),
                style = Win32.CS_OWNDC | Win32.CS_HREDRAW | Win32.CS_VREDRAW,
                lpfnWndProc = proc,
                hInstance = instance,
                hCursor = Win32.LoadCursorW(0, Win32.IDC_ARROW),
                lpszClassName = className,
            };
            if (Win32.RegisterClassExW(&wc) == 0)
                throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
        }
    }

    private static Win32.PIXELFORMATDESCRIPTOR BasicPixelFormat() => new()
    {
        nSize = (ushort)sizeof(Win32.PIXELFORMATDESCRIPTOR),
        nVersion = 1,
        dwFlags = Win32.PFD_DRAW_TO_WINDOW | Win32.PFD_SUPPORT_OPENGL | Win32.PFD_DOUBLEBUFFER,
        iPixelType = Win32.PFD_TYPE_RGBA,
        cColorBits = 32,
        cDepthBits = 24,
        cStencilBits = 8,
        iLayerType = Win32.PFD_MAIN_PLANE,
    };

    // A window's pixel format can be set only once, and wglChoosePixelFormatARB
    // needs a current context to be called at all, so a throwaway window with
    // a plain context asks the driver for a multisampled format first.
    // Returns 0 when the driver has none.
    private static int ChooseMultisampleFormat(nint instance, int samples)
    {
        RegisterClass(instance, DummyClassName, &DummyProc);
        nint hwnd = Win32.CreateWindowExW(0, DummyClassName, "", Win32.WS_OVERLAPPEDWINDOW, 0, 0, 16, 16, 0, 0, instance, 0);
        if (hwnd == 0) return 0;
        nint hdc = Win32.GetDC(hwnd);
        nint context = 0;
        int format = 0;
        try
        {
            var pfd = BasicPixelFormat();
            int basic = Win32.ChoosePixelFormat(hdc, &pfd);
            if (basic == 0 || !Win32.SetPixelFormat(hdc, basic, &pfd)) return 0;
            context = Win32.wglCreateContext(hdc);
            if (context == 0 || !Win32.wglMakeCurrent(hdc, context)) return 0;

            var choose = (delegate* unmanaged[Stdcall]<nint, int*, float*, uint, int*, uint*, int>)GL.TryProc("wglChoosePixelFormatARB");
            if (choose is null) return 0;

            int* attribs = stackalloc int[]
            {
                Win32.WGL_DRAW_TO_WINDOW_ARB, 1,
                Win32.WGL_SUPPORT_OPENGL_ARB, 1,
                Win32.WGL_DOUBLE_BUFFER_ARB, 1,
                Win32.WGL_PIXEL_TYPE_ARB, Win32.WGL_TYPE_RGBA_ARB,
                Win32.WGL_COLOR_BITS_ARB, 32,
                Win32.WGL_DEPTH_BITS_ARB, 24,
                Win32.WGL_STENCIL_BITS_ARB, 8,
                Win32.WGL_SAMPLE_BUFFERS_ARB, 1,
                Win32.WGL_SAMPLES_ARB, samples,
                0,
            };
            int chosen;
            uint count;
            if (choose(hdc, attribs, null, 1, &chosen, &count) == 0 || count == 0) return 0;
            format = chosen;
        }
        finally
        {
            Win32.wglMakeCurrent(0, 0);
            if (context != 0) Win32.wglDeleteContext(context);
            _ = Win32.ReleaseDC(hwnd, hdc);
            Win32.DestroyWindow(hwnd);
        }
        return format;
    }

    // A legacy context first, which is the only way to reach
    // wglCreateContextAttribsARB, then the 3.3 core context the shaders need.
    private static nint CreateCoreContext(nint hdc)
    {
        nint legacy = Win32.wglCreateContext(hdc);
        if (legacy == 0 || !Win32.wglMakeCurrent(hdc, legacy))
            throw new InvalidOperationException("wglCreateContext failed");

        var create = (delegate* unmanaged[Stdcall]<nint, nint, int*, nint>)GL.TryProc("wglCreateContextAttribsARB");
        if (create is null)
            throw new InvalidOperationException("The driver has no OpenGL 3.3 core profile (wglCreateContextAttribsARB).");

        int* attribs = stackalloc int[]
        {
            Win32.WGL_CONTEXT_MAJOR_VERSION_ARB, 3,
            Win32.WGL_CONTEXT_MINOR_VERSION_ARB, 3,
            Win32.WGL_CONTEXT_PROFILE_MASK_ARB, Win32.WGL_CONTEXT_CORE_PROFILE_BIT_ARB,
            0,
        };
        nint core = create(hdc, 0, attribs);
        if (core == 0 || !Win32.wglMakeCurrent(hdc, core))
        {
            Win32.wglMakeCurrent(0, 0);
            Win32.wglDeleteContext(legacy);
            throw new InvalidOperationException("OpenGL 3.3 core context could not be created.");
        }
        Win32.wglDeleteContext(legacy);
        return core;
    }
}
