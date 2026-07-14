using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NyxAssetsEditor.Models.Looktypes;

namespace NyxAssetsEditor.Services.Looktypes;

public static class LooktypeLibraryService
{
	private static readonly string LibraryPath = Path.Combine(AppContext.BaseDirectory, "looktypes.json");
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
	};

	public static LooktypeLibraryDocument Load(out string? error)
	{
		error = null;
		try
		{
			if (!File.Exists(LibraryPath))
				return new LooktypeLibraryDocument();

			var document = JsonSerializer.Deserialize<LooktypeLibraryDocument>(File.ReadAllText(LibraryPath), JsonOptions);
			if (document == null || document.Version != 1)
				throw new InvalidDataException($"Unsupported looktype library version {document?.Version}.");
			document.Profiles ??= new();
			return document;
		}
		catch (Exception ex)
		{
			error = $"Could not load looktypes.json: {ex.Message}";
			Debug.WriteLine(error);
			return new LooktypeLibraryDocument();
		}
	}

	public static bool Save(LooktypeLibraryDocument document, out string? error)
	{
		error = null;
		var tempPath = LibraryPath + ".tmp";
		try
		{
			document.Version = 1;
			File.WriteAllText(tempPath, JsonSerializer.Serialize(document, JsonOptions));
			File.Move(tempPath, LibraryPath, true);
			return true;
		}
		catch (Exception ex)
		{
			error = $"Could not save looktypes.json: {ex.Message}";
			Debug.WriteLine(error);
			try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
			return false;
		}
	}
}
