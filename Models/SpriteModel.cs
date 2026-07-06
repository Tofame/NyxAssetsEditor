namespace NyxAssetsEditor.Models;

public class SpriteModel
{
    public static readonly int SpriteSize = 32;

    public uint Id { get; set; }
    public byte[] Pixels { get; set; } = [];
}
