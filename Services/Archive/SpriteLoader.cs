using System;
using System.Collections.Generic;
using System.IO;
using NyxAssets.Sprites;

namespace NyxAssetsEditor.Services.Archive;

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

        var preload = NyxAssetsEditor.ViewModels.Pages.SettingsViewModel.PreloadGraphicalAssets;

        if (extension == ".spr")
        {
            _archive_spr = SpriteArchive.OpenReadOnlyFile(filePath, extendedSpriteIds, transparentPixels, preloadSprites: preload);
        }
        else if (extension == ".assets")
        {
            _archive_assets = AssetArchive.OpenReadOnlyFile(filePath, preloadPages: preload);
        }
        else
        {
            throw new NotSupportedException($"The file extension '{extension}' is not supported by NyxAssets.");
        }
    }

    public void OpenEmptyArchive(string format, bool extendedSpriteIds = true, bool transparentPixels = true)
    {
        ClearArchives();

        _extendedSpriteIds = extendedSpriteIds;
        _transparentPixels = transparentPixels;

        if (format.ToLower() == "spr")
        {
            byte[] header = new byte[8];
            _archive_spr = SpriteArchive.Load(header, extendedSpriteIds, transparentPixels, preloadSprites: false);
        }
        else if (format.ToLower() == "assets")
        {
            byte[] header = new byte[32];
            uint signature = 0x54535341;
            BitConverter.GetBytes(signature).CopyTo(header, 0);
            uint version = 1;
            BitConverter.GetBytes(version).CopyTo(header, 4);
            _archive_assets = AssetArchive.Load(header, preloadPages: false);
        }
        else
        {
            throw new NotSupportedException($"The format '{format}' is not supported.");
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

    public void SetSpritePixels(uint spriteId, byte[] rgba)
    {
        if (rgba.Length != SpritePixelCodec.RgbaBufferLength)
            throw new ArgumentException($"Expected {SpritePixelCodec.RgbaBufferLength} RGBA bytes.");

        if (_archive_spr != null)
        {
            _archive_spr.PutSprite(spriteId, rgba);
        }
        else if (_archive_assets != null)
        {
            _archive_assets.PutSprite(spriteId, rgba);
        }
        else
        {
            throw new InvalidOperationException("No sprite archive is open.");
        }
    }

    public void ClearSprite(uint spriteId) =>
        SetSpritePixels(spriteId, new byte[SpritePixelCodec.RgbaBufferLength]);

    public bool RemoveLastSprite()
    {
        var count = SpriteCount;
        if (count == 0)
            return false;

        if (_archive_spr != null)
        {
            return _archive_spr.RemoveSprite(count);
        }
        else if (_archive_assets != null)
        {
            return _archive_assets.RemoveSprite(count);
        }
        return false;
    }

    public uint AddNewSprite()
    {
        var newId = SpriteCount + 1;
        var emptyRgba = new byte[SpritePixelCodec.RgbaBufferLength];
        SetSpritePixels(newId, emptyRgba);
        return newId;
    }

    public void WriteSprTo(string path)
    {
        if (_archive_spr == null)
            throw new InvalidOperationException("No .spr archive is open.");

        using var ms = new MemoryStream();
        _archive_spr.WriteToStream(ms);

        // Release the file lock before writing to the same path
        _archive_spr.Dispose();
        _archive_spr = null;

        using var output = File.Create(path);
        ms.Position = 0;
        ms.CopyTo(output);
    }

    public void WriteAssetsTo(string path, int compressionLevel = 3, uint spritesPerPage = 2048)
    {
        if (_archive_assets == null)
            throw new InvalidOperationException("No .assets archive is open.");

        using var ms = new MemoryStream();
        _archive_assets.WriteToStream(ms);

        // Release the file lock before writing to the same path
        _archive_assets.Dispose();
        _archive_assets = null;

        using var output = File.Create(path);
        ms.Position = 0;
        ms.CopyTo(output);
    }

    public bool RemoveSprite(uint spriteId)
    {
        if (_archive_spr != null)
        {
            return _archive_spr.RemoveSprite(spriteId);
        }
        else if (_archive_assets != null)
        {
            return _archive_assets.RemoveSprite(spriteId);
        }
        return false;
    }

    public void WriteToStream(Stream output)
    {
        if (_archive_spr != null)
            _archive_spr.WriteToStream(output);
        else if (_archive_assets != null)
            _archive_assets.WriteToStream(output);
        else
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

public enum SpriteArchiveKind
{
    None,
    Spr,
    Assets
}