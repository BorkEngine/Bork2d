using Bork2d.Api.Assets;
using Bork2d.Bake.Textures;

namespace Bork2d.Bake.Builder.Processors;

/// <summary>
/// The built-in texture processor: decodes PNG/JPG/JPEG/BMP/TGA sources to straight-alpha RGBA8 and
/// emits a <see cref="TextureDescriptor"/> plus the raw payload.
/// </summary>
/// <remarks>
/// <para>
/// This is the only texture decode/probe implementation. Decoding rules live in
/// <see cref="TextureSourceReader"/>: StbImageSharp to straight-alpha RGBA8 with no premultiplication,
/// gamma, ICC, flipping, or resampling; explicit 16-bit PNG rejection, truncation probing, and
/// dimension/payload checks reported as <c>BK4xxx</c>. A failed decode emits no outputs.
/// </para>
/// <para>
/// The descriptor is added as a typed instance through <see cref="IAssetOutputSink.AddAsset{T}"/>, so
/// the engine serializes it; the processor never touches MessagePack or the pack writer. A present
/// sidecar key overrides the builder's texture default, an absent key keeps that default, and a
/// missing sidecar is created with both values from <c>WithTextureDefaults</c>.
/// </para>
/// </remarks>
internal sealed class TextureProcessor : AssetProcessor<TextureSidecar>
{
    private const string RawSuffix = "-raw";
    private const string FormatRgba8 = "rgba8";

    private static readonly string[] _extensions = ["png", "jpg", "jpeg", "bmp", "tga"];

    private readonly BakeTextureDefaults _defaults;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextureProcessor"/> class.
    /// </summary>
    /// <param name="defaults">
    /// The mutable builder texture defaults; the processor reads them when it creates a default
    /// sidecar and when a present sidecar omits a setting, so a late <c>WithTextureDefaults</c> call
    /// still applies.
    /// </param>
    internal TextureProcessor(BakeTextureDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        _defaults = defaults;
    }

    /// <inheritdoc/>
    public override string Name => "texture";

    /// <inheritdoc/>
    public override IReadOnlyList<string> Extensions => _extensions;

    /// <inheritdoc/>
    protected override TextureSidecar CreateDefaultSidecar(AssetProcessorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TextureSidecar
        {
            Filter = DefaultFilter,
            Wrap = DefaultWrap,
        };
    }

    /// <inheritdoc/>
    protected override void Process(AssetProcessorContext context, TextureSidecar sidecar)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sidecar);

        if (!TextureSourceReader.TryDecode(
                context.SourcePath,
                context.RelativePath,
                (code, message) => context.Diagnostics.Error(code, message),
                out byte[] payload,
                out int width,
                out int height))
        {
            return;
        }

        TextureFilter filter = sidecar.Filter ?? DefaultFilter;
        TextureWrap wrap = sidecar.Wrap ?? DefaultWrap;
        string payloadPath = context.BaseAssetPath + RawSuffix;
        var descriptor = new TextureDescriptor
        {
            Payload = payloadPath,
            Width = (uint)width,
            Height = (uint)height,
            Format = FormatRgba8,
            Filter = filter == TextureFilter.Nearest ? "nearest" : "linear",
            Wrap = wrap == TextureWrap.Repeat ? "repeat" : "clamp",
        };

        context.Outputs.AddAsset(context.BaseAssetPath, descriptor);
        context.Outputs.AddData(payloadPath, payload);
    }

    private TextureFilter DefaultFilter => _defaults.Filter == BakeTextureFilter.Nearest
        ? TextureFilter.Nearest
        : TextureFilter.Linear;

    private TextureWrap DefaultWrap => _defaults.Wrap == BakeTextureWrap.Repeat
        ? TextureWrap.Repeat
        : TextureWrap.Clamp;
}
