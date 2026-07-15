namespace NyxAssetsEditor.Models;

public class SpriteModel
{
    public const int SpriteSize = 32;

    public uint Id { get; set; }
    public byte[] Pixels { get; set; } = [];
}
