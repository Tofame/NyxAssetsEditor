using System;
using NyxAssetsEditor.Services.Archive;
using NyxAssets.Sprites;

namespace NyxAssetsEditor.Services.Rendering;

/// <summary>Adapts <see cref="SpriteLoader"/> to <see cref="ISpriteSource"/> for NyxAssets exporters.</summary>
public sealed class SpriteLoaderSpriteSource : ISpriteSource
{
    private readonly SpriteLoader _loader;

    public SpriteLoaderSpriteSource(SpriteLoader loader) => _loader = loader;

    public uint SpriteCount => _loader.SpriteCount;

    public bool TryDecodeSpriteById(uint spriteId, Span<byte> rgbaDestination)
    {
        try
        {
            var pixels = _loader.LoadSpritePixels(spriteId);
            if (pixels.Length != rgbaDestination.Length)
                return false;

            pixels.CopyTo(rgbaDestination);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public byte[] DecodeSpriteById(uint spriteId) => _loader.LoadSpritePixels(spriteId);

    public bool IsEmptySprite(uint spriteId) => _loader.IsEmptySprite(spriteId);

    public void PutSprite(uint spriteId, byte[] rgba) => _loader.SetSpritePixels(spriteId, rgba);

    public bool RemoveSprite(uint spriteId) => _loader.RemoveSprite(spriteId);

    public void WriteToStream(System.IO.Stream output) => _loader.WriteToStream(output);

    public void Dispose()
    {
    }
}
