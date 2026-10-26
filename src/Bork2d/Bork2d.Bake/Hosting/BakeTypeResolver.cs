using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Bork2d.Bake.Diagnostics;

namespace Bork2d.Bake.Hosting;

/// <summary>
/// Resolves descriptor <c>[[types]]</c> full names against <c>Project.Common</c>, its referenced
/// assembly closure, and <c>Bork2d.Api</c> (Q-04), without loading game assemblies into the tooling
/// process.
/// </summary>
/// <remarks>
/// <para>
/// Metadata is read with <see cref="MetadataLoadContext"/> over a probe set built from the runtime's
/// trusted platform assemblies, the tooling directory, and the directory that contains
/// <c>Project.Common</c> (which wins, because it is the exact output the host must reference). The
/// referenced-assembly closure is walked transitively and sorted, so resolution is deterministic.
/// </para>
/// <para>
/// Resolution rejects an assembly-qualified, nested, open-generic, non-class-or-struct, abstract,
/// static, interface, enum, or delegate name with a <c>BK1xxx</c> diagnostic naming the reason. It
/// reports a missing name with every searched assembly and an ambiguous name with every candidate
/// assembly. A mapped type must implement <c>PolyType.IShapeable&lt;T&gt;</c> for itself (the
/// generated shape interface, not the <c>[Conditional("NEVER")]</c> <c>[GenerateShape]</c>
/// attribute) and must be public or internal with <c>InternalsVisibleTo("Bork2d.BakeHost")</c>.
/// </para>
/// </remarks>
internal sealed class BakeTypeResolver : IDisposable
{
    private static readonly string[] _trustedPlatformAssemblyPaths =
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

    private readonly MetadataLoadContext _context;
    private readonly ImmutableArray<Assembly> _searchOrder;
    private readonly string _commonPath;
    private readonly string _apiPath;
    private bool _disposed;

    private BakeTypeResolver(
        MetadataLoadContext context,
        ImmutableArray<Assembly> searchOrder,
        string commonPath,
        string apiPath)
    {
        _context = context;
        _searchOrder = searchOrder;
        _commonPath = commonPath;
        _apiPath = apiPath;
    }

    /// <summary>Gets the absolute <c>Project.Common</c> assembly path.</summary>
    internal string CommonAssemblyPath => _commonPath;

    /// <summary>Gets the absolute <c>Bork2d.Api</c> assembly path used for resolution.</summary>
    internal string ApiAssemblyPath => _apiPath;

    /// <summary>
    /// Gets the non-framework assemblies in the <c>Project.Common</c> closure, including
    /// <c>Project.Common</c> itself, in deterministic order.
    /// </summary>
    internal IReadOnlyList<BakeHostReference> ClosureAssemblies
        => [.. _searchOrder
            .Where(assembly => !IsFrameworkAssembly(assembly.GetName().Name))
            .Select(assembly => new BakeHostReference(
                assembly.GetName().Name ?? string.Empty,
                assembly.Location))
            .Where(reference => reference.AssemblyName.Length > 0
                && !string.IsNullOrEmpty(reference.Path))];

    /// <summary>
    /// Creates a resolver over the metadata of <paramref name="commonAssemblyPath"/> and its
    /// non-framework dependency closure.
    /// </summary>
    /// <param name="commonAssemblyPath">The absolute <c>Project.Common</c> assembly path.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <param name="resolver">The resolver when it was created.</param>
    /// <returns><see langword="true"/> when the resolver was created.</returns>
    internal static bool TryCreate(
        string commonAssemblyPath,
        BakeDiagnostics diagnostics,
        [NotNullWhen(true)] out BakeTypeResolver? resolver)
    {
        ArgumentNullException.ThrowIfNull(commonAssemblyPath);
        ArgumentNullException.ThrowIfNull(diagnostics);

        resolver = null;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(commonAssemblyPath);
        }
        catch (Exception exception) when (IsPathFailure(exception))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.HostReferenceMissing,
                $"Invalid Project.Common path '{commonAssemblyPath}': {exception.Message}",
                commonAssemblyPath);
            return false;
        }

        if (!File.Exists(fullPath))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.HostReferenceMissing,
                $"The Project.Common assembly '{fullPath}' does not exist.",
                fullPath);
            return false;
        }

        Dictionary<string, string> probePaths = CreateProbePaths(fullPath);
        var context = new MetadataLoadContext(
            new ProbeAssemblyResolver(probePaths),
            typeof(object).Assembly.GetName().Name);

        try
        {
            Assembly common = context.LoadFromAssemblyPath(fullPath);
            var searchOrder = new List<Assembly> { common };
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                common.GetName().Name ?? string.Empty,
            };

            var pending = new Queue<Assembly>();
            pending.Enqueue(common);
            bool closureValid = true;

            while (pending.Count > 0)
            {
                Assembly current = pending.Dequeue();
                AssemblyName[] references = [.. current.GetReferencedAssemblies()
                    .Where(reference => !IsFrameworkAssembly(reference.Name))
                    .OrderBy(reference => reference.Name, StringComparer.Ordinal)];

                foreach (AssemblyName reference in references)
                {
                    string name = reference.Name ?? string.Empty;
                    if (name.Length == 0 || !visited.Add(name))
                    {
                        continue;
                    }

                    try
                    {
                        Assembly dependency = context.LoadFromAssemblyName(reference);
                        searchOrder.Add(dependency);
                        pending.Enqueue(dependency);
                    }
                    catch (Exception exception) when (exception is FileNotFoundException or FileLoadException
                        or BadImageFormatException)
                    {
                        diagnostics.Add(
                            BakeSeverity.Error,
                            BakeDiagnosticCodes.HostReferenceMissing,
                            $"Project.Common assembly '{current.GetName().Name}' references '{name}', "
                                + $"which was not found next to it: {exception.Message}",
                            fullPath);
                        closureValid = false;
                    }
                }
            }

            Assembly? api = searchOrder.FirstOrDefault(
                assembly => string.Equals(
                    assembly.GetName().Name,
                    "Bork2d.Api",
                    StringComparison.Ordinal));

            if (api is null)
            {
                if (!probePaths.TryGetValue("Bork2d.Api", out string? apiPath))
                {
                    diagnostics.Add(
                        BakeSeverity.Error,
                        BakeDiagnosticCodes.HostReferenceMissing,
                        $"The tooling could not find 'Bork2d.Api.dll'; it must sit next to the bake "
                            + "tooling or next to Project.Common.",
                        fullPath);
                    context.Dispose();
                    return false;
                }

                api = context.LoadFromAssemblyPath(apiPath);
                searchOrder.Add(api);
            }

            if (!closureValid)
            {
                context.Dispose();
                return false;
            }

            resolver = new BakeTypeResolver(context, [.. searchOrder], fullPath, api.Location);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.HostMetadataUnreadable,
                $"Failed to read Project.Common metadata: {exception.Message}",
                fullPath);
            context.Dispose();
            return false;
        }
    }

    /// <summary>Resolves and validates a mapped type full name.</summary>
    /// <param name="typeName">The descriptor's mapped type full name.</param>
    /// <param name="descriptorPath">The descriptor path used in diagnostics.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <param name="type">The resolved type when successful.</param>
    /// <param name="assemblyPath">The defining assembly path when successful.</param>
    /// <returns><see langword="true"/> when the type is a supported mapped root.</returns>
    internal bool TryResolve(
        string typeName,
        string? descriptorPath,
        BakeDiagnostics diagnostics,
        out Type? type,
        out string? assemblyPath)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(diagnostics);

        type = null;
        assemblyPath = null;

        if (!TryValidateNameForm(typeName, descriptorPath, diagnostics))
        {
            return false;
        }

        var candidates = new List<(Assembly Assembly, Type Type)>();
        foreach (Assembly assembly in _searchOrder)
        {
            Type? candidate = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (candidate is not null)
            {
                candidates.Add((assembly, candidate));
            }
        }

        if (candidates.Count == 0)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.TypeNotFound,
                $"The mapped type '{typeName}' was not found in Project.Common or its referenced "
                    + $"assemblies. Searched: {DescribeSearchOrder()}.",
                descriptorPath);
            return false;
        }

        if (candidates.Count > 1)
        {
            string details = string.Join(
                "; ",
                candidates.Select(
                    candidate => $"{candidate.Assembly.GetName().Name} ({candidate.Assembly.Location})"));
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.TypeAmbiguous,
                $"The mapped type '{typeName}' is ambiguous; it is defined by {candidates.Count} "
                    + $"candidate assemblies: {details}.",
                descriptorPath);
            return false;
        }

        Type resolved = candidates[0].Type;

        if (resolved.IsInterface)
        {
            return RejectForm(typeName, descriptorPath, diagnostics, "an interface");
        }

        if (resolved.IsEnum)
        {
            return RejectForm(typeName, descriptorPath, diagnostics, "an enum");
        }

        if (IsDelegate(resolved))
        {
            return RejectForm(typeName, descriptorPath, diagnostics, "a delegate");
        }

        if (resolved.IsAbstract && resolved.IsSealed)
        {
            return RejectForm(typeName, descriptorPath, diagnostics, "a static class");
        }

        if (resolved.IsAbstract)
        {
            return RejectForm(typeName, descriptorPath, diagnostics, "an abstract type");
        }

        if (resolved.IsGenericType || resolved.ContainsGenericParameters)
        {
            return RejectForm(typeName, descriptorPath, diagnostics, "a generic type");
        }

        if (!resolved.IsClass && !(resolved.IsValueType && !resolved.IsEnum))
        {
            return RejectForm(typeName, descriptorPath, diagnostics, "neither a class nor a struct");
        }

        if (!ImplementsSelfShapeable(resolved))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.TypeNotShapeable,
                $"The mapped type '{typeName}' does not implement "
                    + $"PolyType.IShapeable<{typeName}>; add [GenerateShape] to a partial declaration "
                    + "of the type so PolyType generates its shape.",
                descriptorPath);
            return false;
        }

        if (!IsVisibleToHost(resolved))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.TypeNotVisible,
                $"The mapped type '{typeName}' is internal and assembly "
                    + $"'{resolved.Assembly.GetName().Name}' does not grant "
                    + $"InternalsVisibleTo(\"{BakeHostIdentity.AssemblyName}\"); make the type public "
                    + "or add the grant.",
                descriptorPath);
            return false;
        }

        type = resolved;
        assemblyPath = resolved.Assembly.Location;
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _context.Dispose();
    }

    /// <summary>Maps a metadata type to the C# source name the generated host uses.</summary>
    /// <param name="type">The resolved type.</param>
    /// <param name="diagnostics">The diagnostics bag to append to on an unquotable name.</param>
    /// <param name="descriptorPath">The descriptor path used in diagnostics.</param>
    /// <param name="csharpName">The C# name, for example <c>global::MyGame.Data.GameConfig</c>.</param>
    /// <returns><see langword="true"/> when the type can be written in generated C#.</returns>
    internal static bool TryGetCSharpName(
        Type type,
        BakeDiagnostics diagnostics,
        string? descriptorPath,
        out string csharpName)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(diagnostics);

        csharpName = string.Empty;
        string? fullName = type.FullName;
        if (string.IsNullOrEmpty(fullName))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.TypeUnsupportedForm,
                $"The mapped type '{type}' has no full name and cannot be written in generated C#.",
                descriptorPath);
            return false;
        }

        foreach (string segment in fullName.Split('.'))
        {
            if (!IsCSharpIdentifier(segment))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.TypeUnsupportedForm,
                    $"The mapped type '{fullName}' contains the name segment '{segment}', which is "
                        + "not a C# identifier and cannot be written in generated code.",
                    descriptorPath);
                return false;
            }
        }

        string[] segments = fullName.Split('.');
        for (int i = 0; i < segments.Length; i++)
        {
            if (IsCSharpKeyword(segments[i]))
            {
                segments[i] = "@" + segments[i];
            }
        }

        csharpName = "global::" + string.Join('.', segments);
        return true;
    }

    private static bool TryValidateNameForm(
        string typeName,
        string? descriptorPath,
        BakeDiagnostics diagnostics)
    {
        string? reason = null;
        if (string.IsNullOrWhiteSpace(typeName))
        {
            reason = "the name is empty";
        }
        else if (typeName.Contains(',', StringComparison.Ordinal))
        {
            reason = "assembly-qualified names are not supported; the assembly is resolved "
                + "automatically from Project.Common, its referenced assemblies, and Bork2d.Api";
        }
        else if (typeName.Contains('+', StringComparison.Ordinal))
        {
            reason = "nested types are not supported";
        }
        else if (typeName.Contains('`', StringComparison.Ordinal)
            || typeName.Contains('[', StringComparison.Ordinal)
            || typeName.Contains(']', StringComparison.Ordinal))
        {
            reason = "generic and array type names are not supported";
        }
        else if (typeName.Contains("::", StringComparison.Ordinal))
        {
            reason = "C# alias-qualified names are not supported; use the CLR full name";
        }

        if (reason is null)
        {
            return true;
        }

        diagnostics.Add(
            BakeSeverity.Error,
            BakeDiagnosticCodes.TypeUnsupportedForm,
            $"The mapped type name '{typeName}' is not supported: {reason}.",
            descriptorPath);
        return false;
    }

    private static bool RejectForm(
        string typeName,
        string? descriptorPath,
        BakeDiagnostics diagnostics,
        string form)
    {
        diagnostics.Add(
            BakeSeverity.Error,
            BakeDiagnosticCodes.TypeUnsupportedForm,
            $"The mapped type '{typeName}' is {form}; a mapped custom-data root must be a "
                + "non-abstract, non-static, non-generic class or struct.",
            descriptorPath);
        return false;
    }

    private static bool ImplementsSelfShapeable(Type type)
    {
        foreach (Type implemented in type.GetInterfaces())
        {
            if (!implemented.IsGenericType)
            {
                continue;
            }

            if (!string.Equals(
                implemented.GetGenericTypeDefinition().FullName,
                "PolyType.IShapeable`1",
                StringComparison.Ordinal))
            {
                continue;
            }

            Type[] arguments = implemented.GetGenericArguments();
            if (arguments.Length == 1 && arguments[0] == type)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsVisibleToHost(Type type)
    {
        if (type.IsPublic)
        {
            return true;
        }

        if (!type.IsNotPublic)
        {
            return false;
        }

        foreach (CustomAttributeData attribute in type.Assembly.GetCustomAttributesData())
        {
            if (!string.Equals(
                attribute.AttributeType.FullName,
                "System.Runtime.CompilerServices.InternalsVisibleToAttribute",
                StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Count != 1
                || attribute.ConstructorArguments[0].Value is not string value)
            {
                continue;
            }

            int comma = value.IndexOf(',', StringComparison.Ordinal);
            string name = comma < 0 ? value : value[..comma].Trim();
            if (string.Equals(name, BakeHostIdentity.AssemblyName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDelegate(Type type)
    {
        for (Type? current = type.BaseType; current is not null; current = current.BaseType)
        {
            string? name = current.FullName;
            if (string.Equals(name, "System.MulticastDelegate", StringComparison.Ordinal)
                || string.Equals(name, "System.Delegate", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFrameworkAssembly(string? name)
        => name is null
            || name.StartsWith("System", StringComparison.Ordinal)
            || name.StartsWith("Microsoft", StringComparison.Ordinal)
            || name.StartsWith("mscorlib", StringComparison.Ordinal)
            || name.StartsWith("netstandard", StringComparison.Ordinal)
            || name.StartsWith("WindowsBase", StringComparison.Ordinal);

    private static Dictionary<string, string> CreateProbePaths(string commonAssemblyPath)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        AddDirectory(paths, Path.GetDirectoryName(commonAssemblyPath), overwrite: true);
        AddDirectory(paths, AppContext.BaseDirectory, overwrite: false);

        foreach (string runtimePath in _trustedPlatformAssemblyPaths)
        {
            AddPath(paths, runtimePath, overwrite: false);
        }

        return paths;
    }

    private static void AddDirectory(
        Dictionary<string, string> paths,
        string? directory,
        bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*.dll"))
        {
            AddPath(paths, file, overwrite);
        }
    }

    private static void AddPath(
        Dictionary<string, string> paths,
        string path,
        bool overwrite)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (name.Length == 0)
        {
            return;
        }

        if (overwrite || !paths.ContainsKey(name))
        {
            paths[name] = path;
        }
    }

    private string DescribeSearchOrder()
        => string.Join(
            ", ",
            _searchOrder
                .Select(assembly => assembly.GetName().Name)
                .Where(name => name is not null)
                .Distinct(StringComparer.Ordinal));

    private static bool IsCSharpIdentifier(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        if (!char.IsLetter(value[0]) && value[0] != '_')
        {
            return false;
        }

        for (int i = 1; i < value.Length; i++)
        {
            if (!char.IsLetterOrDigit(value[i]) && value[i] != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCSharpKeyword(string value) => Array.BinarySearch(_csharpKeywords, value) >= 0;

    private static readonly string[] _csharpKeywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while",
    ];

    private static bool IsPathFailure(Exception exception)
        => exception is ArgumentException or NotSupportedException or PathTooLongException;

    private sealed class ProbeAssemblyResolver : MetadataAssemblyResolver
    {
        private readonly IReadOnlyDictionary<string, string> _paths;

        internal ProbeAssemblyResolver(IReadOnlyDictionary<string, string> paths) => _paths = paths;

        public override Assembly? Resolve(MetadataLoadContext context, AssemblyName assemblyName)
        {
            string? name = assemblyName.Name;
            if (name is null || !_paths.TryGetValue(name, out string? path))
            {
                return null;
            }

            return context.LoadFromAssemblyPath(path);
        }
    }
}
