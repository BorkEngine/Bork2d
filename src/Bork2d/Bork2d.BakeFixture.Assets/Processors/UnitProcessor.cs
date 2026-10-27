using System.Text.Json;
using Bork2d.Bake.Builder;

namespace Bork2d.BakeFixture.Assets.Processors;

/// <summary>
/// The Unit acceptance processor: reads a small JSON object with required integer <c>Health</c> and
/// <c>Attack</c> members and emits a <see cref="Bork2d.BakeFixture.Unit"/> at the source's base asset
/// path.
/// </summary>
/// <remarks>
/// <para>
/// The name is the required sidecar's non-empty <c>name</c> value, otherwise the source file name
/// without its extension. An unreadable source or invalid JSON is <c>BK7001</c>; a valid JSON document
/// that is not an object with integer <c>Health</c>/<c>Attack</c> members is <c>BK7002</c>; a failed
/// source produces no outputs.
/// </para>
/// <para>
/// The unit is added as a typed instance through <see cref="IAssetOutputSink.AddAsset{T}"/>, so the
/// engine serializes it with its compiled shape. The processor never serializes MessagePack, and the
/// output type needs no <c>RegisterAsset&lt;T&gt;()</c> entry because the generic call carries the
/// shape at compile time.
/// </para>
/// </remarks>
internal sealed class UnitProcessor : AssetProcessor<UnitSidecar>
{
    private const string HealthMember = "Health";
    private const string AttackMember = "Attack";

    private static readonly string[] _extensions = ["unit"];

    /// <inheritdoc/>
    public override string Name => "unit";

    /// <inheritdoc/>
    public override IReadOnlyList<string> Extensions => _extensions;

    /// <inheritdoc/>
    protected override UnitSidecar CreateDefaultSidecar(AssetProcessorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new UnitSidecar
        {
            Name = context.SourceFileNameWithoutExtension,
        };
    }

    /// <inheritdoc/>
    protected override void Process(AssetProcessorContext context, UnitSidecar sidecar)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sidecar);

        byte[] source;
        try
        {
            source = File.ReadAllBytes(context.SourcePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            context.Diagnostics.Error(
                ProcessorDiagnosticCodes.SourceRead,
                $"Failed to read the unit source '{context.RelativePath}': {exception.Message}");
            return;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(source);
        }
        catch (JsonException exception)
        {
            context.Diagnostics.Error(
                ProcessorDiagnosticCodes.SourceRead,
                $"The unit source '{context.RelativePath}' is not valid JSON: {exception.Message}");
            return;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                ReportInvalid(context, "its root must be a JSON object.");
                return;
            }

            if (!TryReadInteger(root, HealthMember, out int health))
            {
                ReportInvalid(context, $"it must declare an integer '{HealthMember}' member.");
                return;
            }

            if (!TryReadInteger(root, AttackMember, out int attack))
            {
                ReportInvalid(context, $"it must declare an integer '{AttackMember}' member.");
                return;
            }

            string name = string.IsNullOrEmpty(sidecar.Name)
                ? context.SourceFileNameWithoutExtension
                : sidecar.Name;
            context.Outputs.AddAsset(context.BaseAssetPath, new Bork2d.BakeFixture.Unit(name, health, attack));
        }
    }

    private static bool TryReadInteger(JsonElement root, string memberName, out int value)
    {
        value = 0;
        return root.TryGetProperty(memberName, out JsonElement member)
            && member.ValueKind == JsonValueKind.Number
            && member.TryGetInt32(out value);
    }

    private static void ReportInvalid(AssetProcessorContext context, string reason)
        => context.Diagnostics.Error(
            ProcessorDiagnosticCodes.InputInvalid,
            $"The unit source '{context.RelativePath}' is not valid unit input: {reason}");
}
