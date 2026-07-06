using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NyxAssetsEditor.Models;

namespace NyxAssetsEditor.Services.Rendering;

public class SpriteRenderer
{
    public WriteableBitmap Convert(byte[] spritePixels)
    {
        var SpriteSize = SpriteModel.SpriteSize;

        var bitmap = new WriteableBitmap(
            new PixelSize(SpriteSize, SpriteSize),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Premul);

        using (ILockedFramebuffer fb = bitmap.Lock())
        {
            int sourceStride = SpriteSize * 4;

            if (fb.RowBytes == sourceStride)
            {
                Marshal.Copy(spritePixels, 0, fb.Address, spritePixels.Length);
            }
            else
            {
                for (int y = 0; y < SpriteSize; y++)
                {
                    IntPtr destRowAddress = fb.Address + (y * fb.RowBytes);
                    Marshal.Copy(spritePixels, y * sourceStride, destRowAddress, sourceStride);
                }
            }
        }

        return bitmap;
    }

    public WriteableBitmap Convert(SpriteModel model)
    {
        return Convert(model.Pixels);
    }
}