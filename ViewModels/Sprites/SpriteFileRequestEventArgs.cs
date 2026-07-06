using System;
using System.Collections.Generic;
using System.Linq;

namespace NyxAssetsEditor.ViewModels.Sprites;

public sealed class SpriteFileRequestEventArgs : EventArgs
{
	public SpriteFileRequestEventArgs(IEnumerable<SpriteViewModel> sprites, string format)
	{
		Sprites = sprites.ToList();
		Format = format;
	}

	public IReadOnlyList<SpriteViewModel> Sprites { get; }
	public SpriteViewModel Sprite => Sprites[0];
	/// <summary>png, jpg, bmp, or empty for import/replace.</summary>
	public string Format { get; }
}
