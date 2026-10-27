using Bork2d.Api.Assets;
using Bork2d.Assets.Vfs;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Paths;
using Bork2d.Bake.Project;
using Bork2d.Bake.Sources;

namespace Bork2d.Bake.Textures;

/// <summary>
/// Imports every texture source under the asset root into descriptors and RGBA8 payloads.
/// </summary>
/// <remarks>
/// <para>
/// D-M4-13 removes the fixed <c>textures/</c> and <c>textures/raw/</c> folders. Discovery walks the
/// whole asset root in ordinal order, skips reparse points, accepts case-insensitive
/// <c>.png</c>/<c>.jpg</c>/<c>.jpeg</c>/<c>.bmp</c>/<c>.tga</c> sources wherever they live, ignores
/// non-asset files, and associates optional <c>&lt;source file&gt;.toml</c> sidecars next to the
/// source. Canonical paths come from <see cref="BakeSourcePath"/> (the single path grammar): the
/// descriptor path is the source path relative to the asset root minus its extension, and the raw
/// payload path is the descriptor path plus <c>-raw</c> (for example <c>art/player</c> and
/// <c>art/player-raw</c>).
/// </para>
/// <para>
/// A <c>.toml</c> with a root <c>[bork2d]</c> table is data, even when its name looks like a sidecar,
/// and is ignored here. A type-less <c>&lt;image&gt;.toml</c> is a sidecar; a type-less <c>.toml</c>
/// that is neither is a missing-type data error reported by discovery. Canonical texture collisions
/// and a sidecar without a source are errors. A missing sidecar is valid and applies the descriptor's
/// <c>[textures]</c> defaults. Decoding normalizes to straight-alpha RGBA8 without premultiplication,
/// gamma, ICC, flipping, or resampling; explicit 16-bit PNGs are rejected before decode (Q-12).
/// Sources shorter than a derivable uncompressed BMP/TGA layout and JPEGs without an end-of-image
/// marker are rejected as truncated (BK4015) before decode.
/// </para>
/// <para>
/// This component does not write packs, validate custom-data references, or check cross-kind
/// collisions; WP4 composes those on top of <see cref="TextureBakeResult"/>.
/// </para>
/// </remarks>
internal static class TexturePipeline
{
    private const string TomlSuffix = ".toml";
    private const string RawSuffix = "-raw";
    private const string FormatRgba8 = "rgba8";
    private const int HeaderByteCount = 64;

    private static readonly HashSet<string> _textureExtensions = new(
        [".png", ".jpg", ".jpeg", ".bmp", ".tga"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Imports every texture of the main pack using the StbImageSharp decoder.
    /// </summary>
    /// <param name="project">The validated project descriptor.</param>
    /// <returns>The baked textures and every diagnostic, in deterministic order.</returns>
    internal static TextureBakeResult Import(ProjectDescriptor project)
        => Import(project, project.MainPack);

    /// <summary>
    /// Imports every texture of one pack using the StbImageSharp decoder.
    /// </summary>
    /// <param name="project">The validated project descriptor.</param>
    /// <param name="pack">The pack whose root is imported.</param>
    /// <returns>The baked textures and every diagnostic, in deterministic order.</returns>
    internal static TextureBakeResult Import(ProjectDescriptor project, ProjectPack pack)
        => Import(project, pack, StbTextureDecoder.Instance);

    /// <summary>
    /// Imports every texture of the main pack with an explicit decoder seam.
    /// </summary>
    /// <param name="project">The validated project descriptor.</param>
    /// <param name="decoder">The decoder implementation.</param>
    /// <returns>The baked textures and every diagnostic, in deterministic order.</returns>
    internal static TextureBakeResult Import(ProjectDescriptor project, ITextureDecoder decoder)
        => Import(project, project.MainPack, decoder);

    /// <summary>
    /// Imports every texture of one pack with an explicit decoder seam.
    /// </summary>
    /// <param name="project">The validated project descriptor.</param>
    /// <param name="pack">The pack whose root is imported.</param>
    /// <param name="decoder">The decoder implementation.</param>
    /// <returns>The baked textures and every diagnostic, in deterministic order.</returns>
    internal static TextureBakeResult Import(
        ProjectDescriptor project,
        ProjectPack pack,
        ITextureDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(decoder);

        var diagnostics = new BakeDiagnostics();
        List<BakedTexture> textures = ImportCore(project, pack, decoder, diagnostics);
        return new TextureBakeResult(textures, diagnostics.GetSorted());
    }

    private static List<BakedTexture> ImportCore(
        ProjectDescriptor project,
        ProjectPack pack,
        ITextureDecoder decoder,
        BakeDiagnostics diagnostics)
    {
        var sources = new List<TextureSourceEntry>();
        var sourcesByAssetPath = new Dictionary<string, TextureSourceEntry>(StringComparer.Ordinal);
        var sourcePaths = new HashSet<string>(StringComparer.Ordinal);
        var sidecars = new List<string>();

        foreach (string file in BakeSourcePath.EnumerateFilesSorted(pack.Root))
        {
            string extension = Path.GetExtension(file);
            if (extension.Equals(TomlSuffix, StringComparison.OrdinalIgnoreCase))
            {
                AddSidecarIfUntyped(file, sidecars);
            }
            else if (_textureExtensions.Contains(extension))
            {
                AddSource(project, pack, file, sources, sourcesByAssetPath, sourcePaths, diagnostics);
            }
        }

        AttachSidecars(project, pack, sidecars, sourcesByAssetPath, sourcePaths, diagnostics);
        MarkCollisions(sources, diagnostics);

        var textures = new List<BakedTexture>();
        foreach (TextureSourceEntry entry in sources)
        {
            if (entry.AssetPath is null || entry.IsCollision)
            {
                continue;
            }

            BakedTexture? texture = BakeSource(project, pack, entry, decoder, diagnostics);
            if (texture is not null)
            {
                textures.Add(texture);
            }
        }

        textures.Sort(static (left, right) => string.CompareOrdinal(left.AssetPath, right.AssetPath));
        return textures;
    }

    private static void AddSidecarIfUntyped(string file, List<string> sidecars)
    {
        string? text = TryReadSilently(file);
        if (text is not null && BakeSourceMetadataReader.TryHasBork2dTable(text))
        {
            // A typed TOML is data, even when its name looks exactly like a sidecar (D-M4-13).
            return;
        }

        if (BakeSourceDiscovery.IsSidecarShaped(Path.GetFileName(file)))
        {
            sidecars.Add(file);
        }
    }

    private static void AddSource(
        ProjectDescriptor project,
        ProjectPack pack,
        string sourcePath,
        List<TextureSourceEntry> sources,
        Dictionary<string, TextureSourceEntry> sourcesByAssetPath,
        HashSet<string> sourcePaths,
        BakeDiagnostics diagnostics)
    {
        if (!BakeSourcePath.TryMakeRelative(
                pack.Root,
                sourcePath,
                out string relativePath,
                out string? reason))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                TextureDiagnosticCodes.TextureSourcePath,
                $"Texture source '{sourcePath}' is not under the assets root: {reason}.",
                sourcePath);
            return;
        }

        string displayPath = DisplayPath(project, sourcePath);
        var entry = new TextureSourceEntry(sourcePath, relativePath, displayPath);
        sources.Add(entry);
        sourcePaths.Add(sourcePath);

        bool hasCanonicalPath = BakeSourcePath.TryToAssetPath(
            relativePath,
            BakeSourceKind.TextureSource,
            out string assetPath,
            out reason);

        if (hasCanonicalPath)
        {
            // Sidecars attach by canonical asset path, so a differently-cased stem or a different
            // accepted extension still resolves to the same texture, exactly like collisions do.
            sourcesByAssetPath[assetPath] = entry;
        }

        if (!hasCanonicalPath)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                TextureDiagnosticCodes.TextureSourcePath,
                $"Texture source '{displayPath}' cannot be mapped to a canonical asset path: {reason}.",
                displayPath);
            return;
        }

        string payloadPath = assetPath + RawSuffix;
        if (!AssetPath.TryValidateCanonical(payloadPath, out string? payloadReason))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                TextureDiagnosticCodes.TextureSourcePath,
                $"Texture source '{displayPath}' maps to the raw payload path '{payloadPath}', which is "
                    + $"not canonical: {payloadReason}.",
                displayPath);
            return;
        }

        entry.AssetPath = assetPath;
        entry.PayloadPath = payloadPath;
    }

    private static void AttachSidecars(
        ProjectDescriptor project,
        ProjectPack pack,
        List<string> sidecars,
        Dictionary<string, TextureSourceEntry> sourcesByAssetPath,
        HashSet<string> sourcePaths,
        BakeDiagnostics diagnostics)
    {
        foreach (string sidecarPath in sidecars)
        {
            string displayPath = DisplayPath(project, sidecarPath);
            TextureSourceEntry? entry = null;

            if (BakeSourcePath.TryMakeRelative(
                    pack.Root,
                    sidecarPath,
                    out string relativePath,
                    out _)
                && BakeSourcePath.TryToAssetPath(
                    relativePath,
                    BakeSourceKind.TextureSidecar,
                    out string assetPath,
                    out _))
            {
                sourcesByAssetPath.TryGetValue(assetPath, out entry);
            }

            if (entry is not null)
            {
                entry.SidecarPath ??= sidecarPath;
                continue;
            }

            string candidate = sidecarPath[..^TomlSuffix.Length];
            if (sourcePaths.Contains(candidate))
            {
                // The source exists but its path is not canonical-safe; its own BK4002 is the
                // error, so the sidecar must not add an orphaned-sidecar diagnostic.
                continue;
            }

            diagnostics.Add(
                BakeSeverity.Error,
                TextureDiagnosticCodes.TextureSidecarWithoutSource,
                $"Texture sidecar '{displayPath}' has no matching source file.",
                displayPath);
        }
    }

    private static void MarkCollisions(List<TextureSourceEntry> sources, BakeDiagnostics diagnostics)
    {
        var groups = new SortedDictionary<string, List<TextureSourceEntry>>(StringComparer.Ordinal);
        foreach (TextureSourceEntry entry in sources)
        {
            if (entry.AssetPath is null)
            {
                continue;
            }

            if (!groups.TryGetValue(entry.AssetPath, out List<TextureSourceEntry>? group))
            {
                group = [];
                groups[entry.AssetPath] = group;
            }

            group.Add(entry);
        }

        foreach ((string assetPath, List<TextureSourceEntry> group) in groups)
        {
            if (group.Count < 2)
            {
                continue;
            }

            group.Sort(static (left, right)
                => string.CompareOrdinal(left.SourcePath, right.SourcePath));

            foreach (TextureSourceEntry entry in group)
            {
                entry.IsCollision = true;
            }

            for (int i = 1; i < group.Count; i++)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    TextureDiagnosticCodes.TexturePathCollision,
                    $"Texture sources '{group[i].DisplayPath}' and '{group[0].DisplayPath}' both map "
                        + $"to the canonical asset path '{assetPath}'.",
                    group[i].DisplayPath,
                    assetPath: assetPath);
            }
        }
    }

    private static BakedTexture? BakeSource(
        ProjectDescriptor project,
        ProjectPack pack,
        TextureSourceEntry entry,
        ITextureDecoder decoder,
        BakeDiagnostics diagnostics)
    {
        TextureImportSettings settings;
        if (entry.SidecarPath is null)
        {
            settings = TextureImportSettings.Defaults(project.TextureFilter, project.TextureWrap);
        }
        else
        {
            TextureImportSettings? parsed = TextureSidecarParser.Read(
                entry.SidecarPath,
                DisplayPath(project, entry.SidecarPath),
                project.TextureFilter,
                project.TextureWrap,
                diagnostics);
            if (parsed is null)
            {
                return null;
            }

            settings = parsed;
        }

        if (!TryDecode(entry, decoder, diagnostics, out byte[] payload, out int width, out int height))
        {
            return null;
        }

        var descriptor = new TextureDescriptor
        {
            Payload = entry.PayloadPath,
            Width = (uint)width,
            Height = (uint)height,
            Format = FormatRgba8,
            Filter = ToFilterName(settings.Filter),
            Wrap = ToWrapName(settings.Wrap),
        };

        return new BakedTexture(
            entry.AssetPath!,
            descriptor,
            entry.PayloadPath!,
            payload,
            entry.SourcePath,
            settings);
    }

    private static bool TryDecode(
        TextureSourceEntry entry,
        ITextureDecoder decoder,
        BakeDiagnostics diagnostics,
        out byte[] payload,
        out int width,
        out int height)
    {
        payload = [];
        width = 0;
        height = 0;

        FileStream stream;
        try
        {
            stream = new FileStream(entry.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            AddReadFailure(diagnostics, entry, exception);
            return false;
        }

        using (stream)
        {
            try
            {
                byte[] header = new byte[HeaderByteCount];
                int headerLength = stream.ReadAtLeast(
                    header,
                    header.Length,
                    throwOnEndOfStream: false);

                if (TextureSourceProbe.TryGetHeaderProblem(
                        header.AsSpan(0, headerLength),
                        out string code,
                        out long probeWidth,
                        out long probeHeight))
                {
                    diagnostics.Add(
                        BakeSeverity.Error,
                        code,
                        BuildHeaderProblemMessage(entry.DisplayPath, code, probeWidth, probeHeight),
                        entry.DisplayPath,
                        assetPath: entry.AssetPath);
                    return false;
                }

                long fileLength = stream.Length;
                Span<byte> trailer = stackalloc byte[2];
                int trailerLength = ReadTrailer(stream, fileLength, trailer);

                if (TextureSourceProbe.TryGetTruncationProblem(
                        stream,
                        header.AsSpan(0, headerLength),
                        trailer[..trailerLength],
                        fileLength,
                        out string truncationDetail))
                {
                    diagnostics.Add(
                        BakeSeverity.Error,
                        TextureDiagnosticCodes.TextureTruncated,
                        $"Texture source '{entry.DisplayPath}' is truncated: {truncationDetail}.",
                        entry.DisplayPath,
                        assetPath: entry.AssetPath);
                    return false;
                }

                stream.Position = 0;
                DecodedImage image = decoder.Decode(stream);

                if (!TextureSourceProbe.IsSupportedSize(image.Width, image.Height))
                {
                    diagnostics.Add(
                        BakeSeverity.Error,
                        TextureDiagnosticCodes.TextureDimensions,
                        $"Texture source '{entry.DisplayPath}' decoded to unsupported dimensions "
                            + $"{image.Width}x{image.Height}; width and height must be positive and "
                            + "the RGBA8 payload must fit in one array.",
                        entry.DisplayPath,
                        assetPath: entry.AssetPath);
                    return false;
                }

                long expectedLength = (long)image.Width * image.Height * 4;
                if (image.Pixels.Length != expectedLength)
                {
                    diagnostics.Add(
                        BakeSeverity.Error,
                        TextureDiagnosticCodes.TexturePayloadSize,
                        $"Texture source '{entry.DisplayPath}' decoded to {image.Pixels.Length} "
                            + $"RGBA8 bytes; expected {expectedLength} (width × height × 4).",
                        entry.DisplayPath,
                        assetPath: entry.AssetPath);
                    return false;
                }

                width = image.Width;
                height = image.Height;
                payload = image.Pixels;
                return true;
            }
            catch (OutOfMemoryException)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    TextureDiagnosticCodes.TextureOutOfMemory,
                    $"Texture source '{entry.DisplayPath}' could not be decoded: the process ran out "
                        + "of memory.",
                    entry.DisplayPath,
                    assetPath: entry.AssetPath);
                return false;
            }
            catch (Exception exception) when (IsReadFailure(exception))
            {
                AddReadFailure(diagnostics, entry, exception);
                return false;
            }
            catch (Exception exception)
            {
                // The third-party decoder may surface malformed input as any exception type; the
                // bake must turn every such failure into a source diagnostic, never a crash.
                diagnostics.Add(
                    BakeSeverity.Error,
                    TextureDiagnosticCodes.TextureDecode,
                    $"Texture source '{entry.DisplayPath}' could not be decoded: {exception.Message}",
                    entry.DisplayPath,
                    assetPath: entry.AssetPath);
                return false;
            }
        }
    }

    private static string BuildHeaderProblemMessage(
        string displayPath,
        string code,
        long width,
        long height)
        => code == TextureDiagnosticCodes.TextureBitDepth
            ? $"Texture source '{displayPath}' is a 16-bit PNG; only PNG sources with a bit depth of "
                + "8 or lower are supported."
            : $"Texture source '{displayPath}' declares unsupported dimensions {width}x{height}; "
                + "width and height must be positive and the RGBA8 payload must fit in one array.";

    private static void AddReadFailure(
        BakeDiagnostics diagnostics,
        TextureSourceEntry entry,
        Exception exception)
        => diagnostics.Add(
            BakeSeverity.Error,
            TextureDiagnosticCodes.TextureSourceRead,
            $"Failed to read texture source '{entry.DisplayPath}': {exception.Message}",
            entry.DisplayPath,
            assetPath: entry.AssetPath);

    private static string DisplayPath(ProjectDescriptor project, string fullPath)
        => BakeSourcePath.TryMakeRelative(
            project.DescriptorDirectory,
            fullPath,
            out string relativePath,
            out _)
            ? relativePath
            : fullPath;

    private static string? TryReadSilently(string fullPath)
    {
        try
        {
            return File.ReadAllText(fullPath);
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            return null;
        }
    }

    private static int ReadTrailer(Stream stream, long fileLength, Span<byte> trailer)
    {
        if (fileLength < trailer.Length)
        {
            return 0;
        }

        stream.Position = fileLength - trailer.Length;
        return stream.ReadAtLeast(trailer, trailer.Length, throwOnEndOfStream: false);
    }

    private static string ToFilterName(BakeTextureFilter filter)
        => filter == BakeTextureFilter.Nearest ? "nearest" : "linear";

    private static string ToWrapName(BakeTextureWrap wrap)
        => wrap == BakeTextureWrap.Repeat ? "repeat" : "clamp";

    private static bool IsReadFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException;
}
