using System;
using System.IO;
using NyxAssets.Sprites;

namespace NyxAssetsEditor.Services;

public class SpriteLoader : IDisposable
{
    private SpriteArchive? _archive_spr;
    private AssetArchive? _archive_assets;
    private bool _extendedSpriteIds = true;
    private bool _transparentPixels = true;

    public SpriteArchiveKind ArchiveKind =>
        _archive_spr != null ? SpriteArchiveKind.Spr :
        _archive_assets != null ? SpriteArchiveKind.Assets :
        SpriteArchiveKind.None;

    public uint SprSignature => _archive_spr?.Signature ?? 0;

    public uint SpriteCount
    {
        get
        {
            if (_archive_spr != null) return _archive_spr.SpriteCount;
            if (_archive_assets != null) return _archive_assets.SpriteCount;
            return 0;
        }
    }

    /// <summary>
    /// Detects the extension and opens the file into the corresponding archive slot.
    /// </summary>
    public void OpenArchive(string filePath, bool extendedSpriteIds = true, bool transparentPixels = true)
    {
        // Reset both slots to ensure we don't leak memory handles from previous selections
        ClearArchives();

        _extendedSpriteIds = extendedSpriteIds;
        _transparentPixels = transparentPixels;

        string extension = Path.GetExtension(filePath).ToLower();

        if (extension == ".spr")
        {
            _archive_spr = SpriteArchive.OpenReadOnlyFile(filePath, extendedSpriteIds, transparentPixels);
        }
        else if (extension == ".assets")
        {
            _archive_assets = AssetArchive.OpenReadOnlyFile(filePath);
        }
        else
        {
            throw new NotSupportedException($"The file extension '{extension}' is not supported by NyxAssets.");
        }
    }

    /// <summary>
    /// Decodes a specific sprite by checking whichever archive slot is active.
    /// </summary>
    public byte[] LoadSpritePixels(uint spriteId)
    {
        byte[] rgbaBuffer = new byte[SpritePixelCodec.RgbaBufferLength];

        if (_archive_spr != null)
        {
            _archive_spr.TryDecodeSpriteById(spriteId, rgbaBuffer);
            return rgbaBuffer;
        }
            
        if (_archive_assets != null)
        {
            _archive_assets.TryDecodeSpriteById(spriteId, rgbaBuffer);
            return rgbaBuffer;
        }

        throw new InvalidOperationException("No sprite archive is currently open.");
    }

    public bool IsEmptySprite(uint spriteId)
    {
        if (_archive_spr != null)
            return _archive_spr.IsEmptySprite(spriteId);
        if (_archive_assets != null)
            return _archive_assets.IsEmptySprite(spriteId);
        return true;
    }

    public void WriteSprTo(string path)
    {
        if (_archive_spr == null)
            throw new InvalidOperationException("No .spr archive is open.");

        var rgbaList = new byte[]?[SpriteCount + 1];
        for (uint id = 1; id <= SpriteCount; id++)
            rgbaList[id] = IsEmptySprite(id) ? null : LoadSpritePixels(id);

        using var output = File.Create(path);
        SpriteSheetCompiler.WriteToStream(
            output,
            SprSignature,
            _extendedSpriteIds,
            _transparentPixels,
            rgbaList);
    }

    public void WriteAssetsTo(string path, int compressionLevel = 3, uint spritesPerPage = 2048)
    {
        if (ArchiveKind == SpriteArchiveKind.None)
            throw new InvalidOperationException("No sprite archive is open.");

        var writer = new AssetArchiveWriter();
        var spritesArray = new byte[SpriteCount][];

        for (uint id = 1; id <= SpriteCount; id++)
        {
            if (IsEmptySprite(id))
            {
                spritesArray[id - 1] = new byte[] { 0, 0, 0, 0 };
                continue;
            }

            var rgba = LoadSpritePixels(id);
            byte[] entry = new byte[4 + SpritePixelCodec.RgbaBufferLength];
            entry[0] = SpritePixelCodec.SpriteEdgeLength;
            entry[1] = 0;
            entry[2] = SpritePixelCodec.SpriteEdgeLength;
            entry[3] = 0;
            rgba.CopyTo(entry.AsSpan(4));
            spritesArray[id - 1] = entry;
        }

        writer.AddRange(spritesArray);
        writer.Save(path, compressionLevel, spritesPerPage);
    }

    private void ClearArchives()
    {
        _archive_spr?.Dispose();
        _archive_spr = null;

        _archive_assets?.Dispose();
        _archive_assets = null;
    }

    public void Dispose()
    {
        ClearArchives();
    }
}

public enum SpriteArchiveKind
{
    None,
    Spr,
    Assets
}