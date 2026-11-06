namespace Makarov.Tree3D;

// GLSL 3.30 sources. Attribute locations match the vertex structs in Tree.cs.
internal static class Shaders
{
    // Branch segments: the vertex shader applies the wind, the geometry shader
    // inflates each segment into a camera-facing tapered quad and clips the
    // part that has not grown yet, the fragment shader shades it as a tube.

    public const string TubeVertex = """
        #version 330 core
        layout(location=0) in vec3  aPos;
        layout(location=1) in float aDepth;
        layout(location=2) in float aSway;
        layout(location=3) in float aBirth;
        uniform float uTime;
        out vData { vec3 wpos; float depth; float birth; } vs;
        void main(){
            // wind with slow gusts: the amplitude swells and fades over ~17 s
            float gust = 0.6 + 0.4 * sin(uTime * 0.37 + aPos.x * 0.5);
            float amp = 0.05 * gust;
            float sx = sin(uTime*1.3 + aPos.y*1.6) * aSway * amp;
            float sz = cos(uTime*1.1 + aPos.y*1.4) * aSway * amp;
            vs.wpos = aPos + vec3(sx, 0.0, sz);
            vs.depth = aDepth;
            vs.birth = aBirth;
            gl_Position = vec4(vs.wpos, 1.0);
        }
        """;

    public const string TubeGeometry = """
        #version 330 core
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
        }
        """;

    public const string TubeFragment = """
        #version 330 core
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
        }
        """;

    // Glowing canopy sprites at the branch tips: twinkling, blooming in as
    // growth reaches them.

    public const string TipVertex = """
        #version 330 core
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
        }
        """;

    public const string TipFragment = """
        #version 330 core
        in float vTw;
        in float vFade;
        uniform vec3 uGlowCol;
        out vec4 FragColor;
        void main(){
            float r = length(gl_PointCoord - 0.5) * 2.0;
            float a = smoothstep(1.0, 0.0, r);
            vec3 col = mix(uGlowCol, vec3(1.0), 0.35);
            FragColor = vec4(col, a * (0.35 + 0.65 * vTw) * 0.5 * vFade);
        }
        """;

    // Falling petals: pure GPU particles, the whole trajectory is a function
    // of time, so there is no per-frame CPU work.

    public const string PetalVertex = """
        #version 330 core
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
        }
        """;

    public const string PetalFragment = """
        #version 330 core
        in float vAlpha;
        uniform vec3 uGlowCol;
        out vec4 FragColor;
        void main(){
            float r = length(gl_PointCoord - 0.5) * 2.0;
            float a = smoothstep(1.0, 0.2, r);
            FragColor = vec4(uGlowCol, a * vAlpha * 0.65);
        }
        """;

    // Background: a radial gradient over a fullscreen quad.

    public const string QuadVertex = """
        #version 330 core
        layout(location=0) in vec2 aPos;
        layout(location=1) in vec2 aUV;
        out vec2 vUV;
        void main(){ vUV = aUV; gl_Position = vec4(aPos, 0.0, 1.0); }
        """;

    public const string GradientFragment = """
        #version 330 core
        in vec2 vUV;
        uniform vec3 uInner;
        uniform vec3 uOuter;
        out vec4 FragColor;
        void main(){
            float d = length(vUV - vec2(0.5, 0.55));
            vec3 c = mix(uInner, uOuter, smoothstep(0.0, 0.85, d));
            FragColor = vec4(c, 1.0);
        }
        """;
}
