using System;
using System.Collections.Generic;
using System.IO;

namespace NyxAssetsEditor.Services.Archive;

public sealed record OtfiSettings(
	bool? Extended,
	bool? Transparency,
	bool? FrameDurations,
	bool? FrameGroups);

public static class OtfiSettingsReader
{
	public static OtfiSettings? ReadForArchive(string archivePath, out string? warning)
	{
		warning = null;
		var directory = Path.GetDirectoryName(Path.GetFullPath(archivePath))!;
		var otfiPath = Path.ChangeExtension(archivePath, ".otfi");

		if (!File.Exists(otfiPath))
		{
			var files = Directory.GetFiles(directory, "*.otfi");
			if (files.Length != 1)
			{
				warning = files.Length == 0
					? "No .otfi file was found beside the archive."
					: $"No '{Path.GetFileName(otfiPath)}' was found and the directory contains multiple .otfi files.";
				return null;
			}
			otfiPath = files[0];
		}

		try
		{
			var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var hasDatSprSection = false;
			foreach (var rawLine in File.ReadLines(otfiPath))
			{
				var line = rawLine.Trim();
				if (line.Equals("DatSpr", StringComparison.OrdinalIgnoreCase))
				{
					hasDatSprSection = true;
					continue;
				}

				var separator = line.IndexOf(':');
				if (separator > 0)
					values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
			}

			if (!hasDatSprSection)
				throw new InvalidDataException("the DatSpr section is missing");

			bool? ReadBool(string key)
			{
				if (!values.TryGetValue(key, out var raw)) return null;
				if (bool.TryParse(raw, out var value)) return value;
				return null;
			}

			var settings = new OtfiSettings(
				ReadBool("extended"),
				ReadBool("transparency"),
				ReadBool("frame-durations"),
				ReadBool("frame-groups"));

			return settings;
		}
		catch (Exception ex)
		{
			warning = $"Could not read '{Path.GetFileName(otfiPath)}': {ex.Message}.";
			return null;
		}
	}
}
