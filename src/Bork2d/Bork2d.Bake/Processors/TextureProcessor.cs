using Bork2d.Bake.Textures;
using Bork2d.Engine.Assets;

namespace Bork2d.Bake.Processors;

/// <summary>
/// The built-in texture processor: decodes PNG/JPG/JPEG/BMP/TGA sources to straight-alpha RGBA8 and
/// emits a <see cref="TextureDescriptor"/> plus its KTX2 payload.
/// </summary>
/// <remarks>
/// <para>
/// This is the only texture decode/probe implementation. Decoding rules live in
/// <see cref="TextureSourceReader"/>: StbImageSharp to straight-alpha RGBA8 with no premultiplication,
/// gamma, ICC, flipping, or resampling; explicit 16-bit PNG rejection, truncation probing, and
/// dimension/payload checks reported as <c>BK4xxx</c>. A failed decode emits no outputs.
/// </para>
/// <para>
/// The decoded pixels and the sidecar's <c>type</c>/<c>compression</c> settings go through
/// <see cref="TextureBaker"/>, so the processor never writes KTX2 headers or block data itself. The
/// descriptor is added as a typed instance through <see cref="IAssetOutputSink.AddAsset{T}"/>, so the
/// engine serializes it; the processor never touches MessagePack or the pack writer. A present sidecar
/// key overrides its default, an absent key keeps that default, and a missing sidecar is created with
/// the explicit defaults and both builder sampler values. A bake-side KTX2/BC failure is a
/// <c>BK91xx</c> diagnostic and emits no outputs.
/// </para>
/// </remarks>
internal sealed class TextureProcessor : AssetProcessor<TextureSidecar>
{
    private const string PayloadSuffix = "-ktx2";

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
            Type = TextureChannelType.Rgba,
            Compression = TextureCompression.None,
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

        TextureChannelType channelType = sidecar.Type ?? TextureChannelType.Rgba;
        TextureCompression compression = sidecar.Compression ?? TextureCompression.None;
        if (!TextureFormatMapping.IsDefined(channelType, compression))
        {
            context.Diagnostics.Error(
                TextureDiagnosticCodes.TextureFormatMapping,
                $"Texture source '{context.RelativePath}' selects the channel type "
                    + $"'{channelType}' with compression '{compression}', which does not map to a "
                    + "texture format.");
            return;
        }

        BakedTexture baked;
        try
        {
            baked = TextureBaker.Bake(payload, width, height, channelType, compression);
        }
        catch (Exception exception)
        {
            context.Diagnostics.Error(
                TextureDiagnosticCodes.TextureBake,
                $"Texture source '{context.RelativePath}' could not be baked: {exception.Message}");
            return;
        }

        TextureFilter filter = sidecar.Filter ?? DefaultFilter;
        TextureWrap wrap = sidecar.Wrap ?? DefaultWrap;
        string payloadPath = context.BaseAssetPath + PayloadSuffix;
        var descriptor = new TextureDescriptor
        {
            Payload = payloadPath,
            Width = (uint)width,
            Height = (uint)height,
            Format = baked.Format,
            MipLevels = baked.MipLevels,
            Filter = filter,
            Wrap = wrap,
        };

        context.Outputs.AddAsset(context.BaseAssetPath, descriptor);
        context.Outputs.AddData(payloadPath, baked.Payload.Span);
    }

    private TextureFilter DefaultFilter => _defaults.Filter == BakeTextureFilter.Nearest
        ? TextureFilter.Nearest
        : TextureFilter.Linear;

    private TextureWrap DefaultWrap => _defaults.Wrap == BakeTextureWrap.Repeat
        ? TextureWrap.Repeat
        : TextureWrap.Clamp;
}
