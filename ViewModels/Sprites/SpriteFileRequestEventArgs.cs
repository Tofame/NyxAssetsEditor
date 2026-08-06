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
	/// <summary>png, jpg, bmp, export_popup, replace, append (new sprites), or empty for import-into-selection.</summary>
	public string Format { get; }
}
