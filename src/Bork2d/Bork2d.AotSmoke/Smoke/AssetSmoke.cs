using Bork2d.Assets.Pack;
using Bork2d.Common.Serialization;

namespace Bork2d.AotSmoke.Smoke;

/// <summary>
/// Builds the temporary pack used by the default NativeAOT smoke run and holds the expected values.
/// </summary>
/// <remarks>
/// The pack is written to an absolute temporary path with the public <see cref="PackBuilder"/>
/// (D-M3-19), loaded by the app, and deleted by the caller after the app has disposed (which closes
/// the pack handles). No binary fixture is committed.
/// </remarks>
internal static class AssetSmoke
{
    /// <summary>The canonical path of the raw asset in the smoke pack.</summary>
    internal const string RawPath = "data/smoke";

    /// <summary>The canonical path of the MessagePack asset in the smoke pack.</summary>
    internal const string MessagePackPath = "data/smoke-data";

    /// <summary>The expected value of the raw asset.</summary>
    internal const string Name = "bork2d-aot-smoke";

    /// <summary>The expected integer value of the MessagePack asset.</summary>
    internal const int Value = 42;

    /// <summary>The expected scale of the MessagePack asset.</summary>
    internal const float Scale = 1.5f;

    /// <summary>The expected entry count of the smoke pack.</summary>
    internal const int EntryCount = 2;

    /// <summary>The known raw payload bytes stored in the pack.</summary>
    internal static ReadOnlySpan<byte> RawPayload => [0x42, 0x4F, 0x52, 0x4B, 0x32, 0x44, 0x01, 0x7F];

    /// <summary>Builds the smoke pack at a new absolute temporary path.</summary>
    /// <returns>The absolute pack path; the caller deletes it.</returns>
    internal static string CreatePack()
    {
        string path = Path.Combine(Path.GetTempPath(), $"bork2d-aot-{Guid.NewGuid():N}.pck");

        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddRaw(RawPath, RawPayload);
        builder.AddMessagePack(MessagePackPath, CreateData());
        builder.Write(path);

        return path;
    }

    /// <summary>Creates the expected MessagePack asset value.</summary>
    internal static MessagePackSmokeData CreateData() => new()
    {
        Name = Name,
        Value = Value,
        Scale = Scale,
    };
}
