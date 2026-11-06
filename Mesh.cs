namespace Makarov.Tree3D;

// A VAO + VBO pair for one vertex type: float attributes laid out back to
// back in declaration order, uploaded whole and drawn with glDrawArrays.
internal sealed unsafe class Mesh<T> where T : unmanaged
{
    private readonly uint _vao;
    private readonly uint _vbo;

    public int Count { get; private set; }

    // `components`: float count of each attribute, in location order.
    public Mesh(ReadOnlySpan<int> components)
    {
        _vao = GL.GenVertexArray();
        GL.glBindVertexArray(_vao);
        _vbo = GL.GenBuffer();
        GL.glBindBuffer(GL.ARRAY_BUFFER, _vbo);

        int stride = sizeof(T);
        int offset = 0;
        for (int i = 0; i < components.Length; i++)
        {
            GL.VertexAttrib((uint)i, components[i], stride, offset);
            offset += components[i] * sizeof(float);
        }
        if (offset != stride)
            throw new ArgumentException($"{typeof(T).Name} is {stride} bytes but the attributes add up to {offset}");
    }

    public void Upload(ReadOnlySpan<T> vertices)
    {
        GL.glBindBuffer(GL.ARRAY_BUFFER, _vbo);
        GL.BufferData(vertices);
        Count = vertices.Length;
    }

    public void Draw(uint mode)
    {
        GL.glBindVertexArray(_vao);
        GL.glDrawArrays(mode, 0, Count);
    }
}
