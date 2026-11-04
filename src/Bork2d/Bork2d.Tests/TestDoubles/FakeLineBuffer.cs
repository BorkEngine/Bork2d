using Bork2d.Render.TwoDimensional;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// An in-memory <see cref="ILineBufferSink"/> for deterministic planner tests.
/// </summary>
/// <remarks>
/// The buffers grow like the GPU implementation (only when a required count exceeds the capacity)
/// and record every write, so tests can assert vertex/index data, world transforms, absolute indices,
/// and run ranges without Vulkan.
/// </remarks>
internal sealed class FakeLineBuffer : ILineBufferSink
{
    private LineVertex[] _vertices;
    private uint[] _indices;

    /// <summary>Initializes a new instance of the <see cref="FakeLineBuffer"/> class.</summary>
    /// <param name="initialVertexCapacity">The initial vertex-record capacity.</param>
    /// <param name="initialIndexCapacity">The initial 32-bit index capacity.</param>
    internal FakeLineBuffer(int initialVertexCapacity = 16, int initialIndexCapacity = 24)
    {
        _vertices = new LineVertex[initialVertexCapacity];
        _indices = new uint[initialIndexCapacity];
    }

    /// <summary>Gets the number of times <see cref="EnsureCapacity"/> had to grow the buffers.</summary>
    internal int GrowthCount { get; private set; }

    /// <summary>Gets the last required vertex count passed to <see cref="EnsureCapacity"/>.</summary>
    internal int LastRequiredVertexCount { get; private set; }

    /// <summary>Gets the last required index count passed to <see cref="EnsureCapacity"/>.</summary>
    internal int LastRequiredIndexCount { get; private set; }

    /// <summary>Gets the number of vertex writes performed.</summary>
    internal int VertexWriteCount { get; private set; }

    /// <summary>Gets the number of index writes performed.</summary>
    internal int IndexWriteCount { get; private set; }

    /// <inheritdoc/>
    public int VertexCapacity => _vertices.Length;

    /// <inheritdoc/>
    public int IndexCapacity => _indices.Length;

    /// <inheritdoc/>
    public void EnsureCapacity(int vertexCount, int indexCount)
    {
        LastRequiredVertexCount = vertexCount;
        LastRequiredIndexCount = indexCount;
        if (vertexCount > _vertices.Length)
        {
            GrowthCount++;
            int capacity = Math.Max(vertexCount, _vertices.Length * 2);
            Array.Resize(ref _vertices, capacity);
        }

        if (indexCount > _indices.Length)
        {
            GrowthCount++;
            int capacity = Math.Max(indexCount, _indices.Length * 2);
            Array.Resize(ref _indices, capacity);
        }
    }

    /// <inheritdoc/>
    public void WriteVertex(int index, in LineVertex vertex)
    {
        if ((uint)index >= (uint)_vertices.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"The fake line buffer holds {_vertices.Length} vertex records.");
        }

        _vertices[index] = vertex;
        VertexWriteCount++;
    }

    /// <inheritdoc/>
    public void WriteIndex(int index, uint value)
    {
        if ((uint)index >= (uint)_indices.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"The fake line buffer holds {_indices.Length} indices.");
        }

        _indices[index] = value;
        IndexWriteCount++;
    }

    /// <summary>Gets one written vertex record.</summary>
    /// <param name="index">The record index.</param>
    /// <returns>The record.</returns>
    internal LineVertex GetVertex(int index) => _vertices[index];

    /// <summary>Gets one written index.</summary>
    /// <param name="index">The index-buffer position.</param>
    /// <returns>The absolute vertex index.</returns>
    internal uint GetIndex(int index) => _indices[index];
}
