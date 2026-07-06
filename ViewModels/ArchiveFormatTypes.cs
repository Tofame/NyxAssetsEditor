using System;
using System.IO;

namespace NyxAssetsEditor.ViewModels
{
	public enum ArchiveFormat
	{
		Unknown,
		Spr,
		Assets,
		Dat,
		Things
	}

	public static class ArchiveFormatHelper
	{
		public static ArchiveFormat FromPath(string? path)
		{
			if (string.IsNullOrEmpty(path))
				return ArchiveFormat.Unknown;

			return Path.GetExtension(path).ToLowerInvariant() switch
			{
				".spr" => ArchiveFormat.Spr,
				".assets" => ArchiveFormat.Assets,
				".dat" => ArchiveFormat.Dat,
				".things" => ArchiveFormat.Things,
				_ => ArchiveFormat.Unknown
			};
		}

		public static bool AreCompatible(ArchiveFormat spriteFormat, ArchiveFormat thingsFormat) =>
			(spriteFormat == ArchiveFormat.Spr && thingsFormat == ArchiveFormat.Dat) ||
			(spriteFormat == ArchiveFormat.Assets && thingsFormat == ArchiveFormat.Things);
	}
}
