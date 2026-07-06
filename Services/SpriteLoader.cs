using System;
using System.IO;
using NyxAssets.Sprites;

namespace NyxAssetsEditor.Services;

public class SpriteLoader : IDisposable
{
    private SpriteArchive? _archive_spr;
    private AssetArchive? _archive_assets;

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