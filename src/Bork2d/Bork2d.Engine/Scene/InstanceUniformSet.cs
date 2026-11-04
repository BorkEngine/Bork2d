using System.Numerics;
using Bork2d.Engine.Assets;

namespace Bork2d.Engine.Scene;

/// <summary>
/// The per-renderable instance uniform storage shared by engine renderable kinds.
/// </summary>
/// <remarks>
/// <para>
/// The set validates a value against the currently assigned shader's frozen declarations and stores
/// it until the set is reset. A value whose name or type no longer matches after a shader
/// reassignment stays stored but is inert at capture. The owning renderable serializes every member
/// under its per-object lock.
/// </para>
/// <para>
/// <see cref="WriteBlock"/> packs the frozen shader defaults and every matching stored override into
/// a stack instance block, so a warm capture allocates nothing. It is the only copy of that packing
/// logic shared by sprites and lines.
/// </para>
/// </remarks>
internal sealed class InstanceUniformSet
{
    private readonly Dictionary<string, InstanceUniformValue> _values = new(StringComparer.Ordinal);

    /// <summary>
    /// Validates one value against the assigned shader and stores it.
    /// </summary>
    /// <param name="name">The shader-declared instance uniform name, matched ordinally.</param>
    /// <param name="type">The overload's uniform type.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="shaderData">The assigned shader's frozen declarations, or <see langword="null"/>.</param>
    /// <param name="renderableKind">The lowercase renderable kind used in diagnostics.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is <see langword="null"/>, empty, or whitespace; no shader is
    /// assigned; the shader does not declare the name; or the declared type differs from
    /// <paramref name="type"/>.
    /// </exception>
    internal void Set(
        string name,
        ShaderUniformType type,
        Vector4 value,
        ShaderRenderData? shaderData,
        string renderableKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (shaderData is null)
        {
            throw new ArgumentException(
                $"Cannot set instance uniform '{name}': the {renderableKind} has no Shader assigned.",
                nameof(name));
        }

        if (!shaderData.TryGetInstanceUniform(name, out ShaderUniform uniform))
        {
            throw new ArgumentException(
                $"The assigned shader does not declare an instance uniform named '{name}'.",
                nameof(name));
        }

        if (uniform.Type != type)
        {
            throw new ArgumentException(
                $"Instance uniform '{name}' is declared as {uniform.Type}, not {type}.",
                nameof(name));
        }

        _values[name] = new InstanceUniformValue(type, value);
    }

    /// <summary>Clears every stored value so the shader defaults apply again.</summary>
    internal void Reset() => _values.Clear();

    /// <summary>
    /// Writes the frozen default block and every matching stored override into
    /// <paramref name="block"/>.
    /// </summary>
    /// <param name="shaderData">The assigned shader's frozen declarations.</param>
    /// <param name="block">
    /// The destination block; its length must be
    /// <see cref="ShaderLayout.InstanceBlockVectorCount"/>.
    /// </param>
    internal void WriteBlock(ShaderRenderData shaderData, Span<Vector4> block)
    {
        shaderData.DefaultInstanceUniformBlock.CopyTo(block);
        foreach (KeyValuePair<string, InstanceUniformValue> pair in _values)
        {
            // A stored value survives a shader reassignment; it only applies when the new frozen
            // declaration still has the same ordinal name and value type.
            if (shaderData.TryGetInstanceUniform(pair.Key, out ShaderUniform uniform)
                && uniform.Type == pair.Value.Type)
            {
                ShaderLayout.WriteUniform(block, in uniform, pair.Value.Value);
            }
        }
    }

    private readonly record struct InstanceUniformValue(ShaderUniformType Type, Vector4 Value);
}
