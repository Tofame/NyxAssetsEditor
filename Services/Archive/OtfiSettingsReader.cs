using System;
using System.IO;
using NyxAssets.Client;

namespace NyxAssetsEditor.Services.Archive;

public static class OtfiSettingsReader
{
	public static OtfiFile? ReadForArchive(string archivePath, out string? warning)
	{
		warning = null;
		var directory = Path.GetDirectoryName(Path.GetFullPath(archivePath))!;
		var otfiPath = Path.ChangeExtension(archivePath, ".otfi");

		if (!File.Exists(otfiPath))
		{
			var files = Directory.GetFiles(directory, "*.otfi");
			if (files.Length == 0)
			{
				warning = "No .otfi file was found beside the archive.";
				return null;
			}

			OtfiFile? matchedOtfi = null;
			string? matchedPath = null;
			var matchCount = 0;
			var targetFileName = Path.GetFileName(archivePath);
			var isSpr = archivePath.EndsWith(".spr", StringComparison.OrdinalIgnoreCase);

			foreach (var file in files)
			{
				try
				{
					var otfi = OtfiFile.Load(file);
					var referencedFile = isSpr ? otfi.SpritesFile : otfi.MetadataFile;
					if (!string.IsNullOrEmpty(referencedFile))
					{
						var referencedName = Path.GetFileName(referencedFile);
						if (string.Equals(referencedName, targetFileName, StringComparison.OrdinalIgnoreCase))
						{
							matchedOtfi = otfi;
							matchedPath = file;
							matchCount++;
						}
					}
				}
				catch
				{
					// Skip invalid files during search
				}
			}

			if (matchCount == 1)
			{
				otfiPath = matchedPath!;
				return matchedOtfi;
			}

			warning = matchCount == 0
				? $"No '{Path.GetFileName(otfiPath)}' was found, and none of the {files.Length} .otfi files found reference '{targetFileName}'."
				: $"No '{Path.GetFileName(otfiPath)}' was found, and multiple .otfi files reference '{targetFileName}'.";
			return null;
		}

		try
		{
			return OtfiFile.Load(otfiPath);
		}
		catch (Exception ex)
		{
			warning = $"Could not read '{Path.GetFileName(otfiPath)}': {ex.Message}.";
			return null;
		}
	}
}
