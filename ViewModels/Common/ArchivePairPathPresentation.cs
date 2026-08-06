using System.IO;

namespace NyxAssetsEditor.ViewModels.Common;

public sealed record ArchivePairPathPresentation(
	string DisplayName,
	string DirectoryPath,
	string DetailsText,
	string ToolTipText)
{
	public static ArchivePairPathPresentation Create(string? spritePath, string? thingsPath)
	{
		spritePath ??= string.Empty;
		thingsPath ??= string.Empty;
		var spriteName = string.IsNullOrEmpty(spritePath) ? string.Empty : Path.GetFileName(spritePath);
		var thingsName = string.IsNullOrEmpty(thingsPath) ? string.Empty : Path.GetFileName(thingsPath);
		string displayName;
		string directoryPath;
		string toolTipText;

		if (!string.IsNullOrEmpty(spriteName) && !string.IsNullOrEmpty(thingsName))
		{
			displayName = $"{thingsName} + {spriteName}";
			directoryPath = Path.GetDirectoryName(thingsPath) ?? string.Empty;
			toolTipText = $"DAT: {thingsPath}\nSPR: {spritePath}";
		}
		else if (!string.IsNullOrEmpty(thingsName))
		{
			displayName = thingsName;
			directoryPath = Path.GetDirectoryName(thingsPath) ?? string.Empty;
			toolTipText = $"DAT: {thingsPath}";
		}
		else if (!string.IsNullOrEmpty(spriteName))
		{
			displayName = spriteName;
			directoryPath = Path.GetDirectoryName(spritePath) ?? string.Empty;
			toolTipText = $"SPR: {spritePath}";
		}
		else
		{
			displayName = "Unknown Archive";
			directoryPath = string.Empty;
			toolTipText = string.Empty;
		}

		return new ArchivePairPathPresentation(
			displayName,
			directoryPath,
			CompactPath(directoryPath),
			toolTipText);
	}

	public static string CompactPath(string path, int maxLength = 35)
	{
		if (string.IsNullOrEmpty(path))
			return string.Empty;
		if (path.Length <= maxLength)
			return path;

		var separator = Path.DirectorySeparatorChar;
		var parts = path.Split(
			new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
			System.StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0)
			return path;

		var result = parts[^1];
		for (var index = parts.Length - 2; index >= 0; index--)
		{
			var candidate = parts[index] + separator + result;
			if (($"...{separator}{candidate}").Length > maxLength)
				break;
			result = candidate;
		}

		return $"...{separator}{result}";
	}
}
