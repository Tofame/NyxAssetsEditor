using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NyxAssetsEditor.Services.Things;

public class CustomFlagDefinition
{
	public string Name { get; set; } = string.Empty;
	public string Label { get; set; } = string.Empty;
	public string Type { get; set; } = "bool";
	public string? Default { get; set; }
	public string? Description { get; set; }
	public string? Group { get; set; }
	public string? GroupType { get; set; }
	public List<string>? Options { get; set; }
	public int? Min { get; set; }
	public int? Max { get; set; }
}

public class CustomFlagGroupDefinition
{
	public string Key { get; set; } = string.Empty;
	public string Label { get; set; } = string.Empty;
	public int Order { get; set; }
}

public class CustomFlagSchema
{
	public List<CustomFlagDefinition> Flags { get; set; } = new();
	public Dictionary<string, CustomFlagGroupDefinition> Groups { get; set; } = new(StringComparer.Ordinal);
}

// TOML deserialization models
public class CustomFlagsTomlFlagEntry
{
	public string? label { get; set; }
	public string? type { get; set; }
	public string? @default { get; set; }
	public string? description { get; set; }
	public string? group { get; set; }
	public string? group_type { get; set; }
	public List<string>? options { get; set; }
	public int? min { get; set; }
	public int? max { get; set; }
}

public class CustomFlagsTomlGroupEntry
{
	public string? label { get; set; }
	public int order { get; set; }
}

public class CustomFlagsTomlModel
{
	public Dictionary<string, CustomFlagsTomlFlagEntry>? flag { get; set; }
	public Dictionary<string, CustomFlagsTomlGroupEntry>? groups { get; set; }
}

public static class CustomFlagSchemaLoader
{
	public static CustomFlagSchema Load(string? archiveFilePath, string datVersionDirName)
	{
		string? tomlText = null;

		// Priority 1: per-archive override
		if (!string.IsNullOrEmpty(archiveFilePath))
		{
			string? dir = Path.GetDirectoryName(archiveFilePath);
			if (dir != null)
			{
				string overridePath = Path.Combine(dir, "flags_custom_override.toml");
				if (File.Exists(overridePath))
					tomlText = TryReadFile(overridePath);

				if (string.IsNullOrEmpty(tomlText))
				{
					string defaultPath = Path.Combine(dir, "flags_custom.toml");
					if (File.Exists(defaultPath))
						tomlText = TryReadFile(defaultPath);
				}
			}
		}

		// Priority 2: global from Assets/jsonProtocols/default/
		if (string.IsNullOrEmpty(tomlText))
		{
			tomlText = TryLoadFromGlobal("flags_custom_override.toml");
		}

		if (string.IsNullOrEmpty(tomlText))
		{
			tomlText = TryLoadFromGlobal("flags_custom.toml");
		}

		// Priority 3: embedded resource
		if (string.IsNullOrEmpty(tomlText))
		{
			tomlText = TryLoadFromEmbedded("flags_custom.toml");
		}

		if (string.IsNullOrEmpty(tomlText))
			return new CustomFlagSchema();

		return ParseToml(tomlText);
	}

	private static string? TryReadFile(string path)
	{
		try { return File.ReadAllText(path); }
		catch { return null; }
	}

	private static string? TryLoadFromGlobal(string fileName)
	{
		string relativePath = Path.Combine("Assets", "jsonProtocols", "default", fileName);

		string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
		if (File.Exists(path)) return TryReadFile(path);

		path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
		if (File.Exists(path)) return TryReadFile(path);

		path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", relativePath);
		if (File.Exists(path)) return TryReadFile(path);

		return null;
	}

	private static string? TryLoadFromEmbedded(string fileName)
	{
		try
		{
			using var stream = Avalonia.Platform.AssetLoader.Open(
				new Uri($"avares://NyxAssetsEditor/Assets/jsonProtocols/default/{fileName}"));
			using var reader = new StreamReader(stream);
			return reader.ReadToEnd();
		}
		catch
		{
			return null;
		}
	}

	private static CustomFlagSchema ParseToml(string tomlText)
	{
		var schema = new CustomFlagSchema();
		try
		{
			var model = Tomlyn.TomlSerializer.Deserialize<CustomFlagsTomlModel>(tomlText);
			if (model == null) return schema;

			if (model.flag != null)
			{
				foreach (var (key, entry) in model.flag)
				{
					schema.Flags.Add(new CustomFlagDefinition
					{
						Name = key,
						Label = entry.label ?? key,
						Type = entry.type ?? "bool",
						Default = entry.@default,
						Description = entry.description,
						Group = entry.group,
						GroupType = entry.group_type,
						Options = entry.options,
						Min = entry.min,
						Max = entry.max,
					});
				}
			}

			if (model.groups != null)
			{
				foreach (var (key, entry) in model.groups)
				{
					schema.Groups[key] = new CustomFlagGroupDefinition
					{
						Key = key,
						Label = entry.label ?? key,
						Order = entry.order,
					};
				}
			}
		}
		catch
		{
			// Malformed TOML → empty schema
		}

		return schema;
	}

	public static void SaveDefinition(string? archiveFilePath, string datVersionDirName, CustomFlagDefinition def, string? groupLabel = null)
	{
		string targetPath;
		if (!string.IsNullOrEmpty(archiveFilePath))
		{
			string? dir = Path.GetDirectoryName(archiveFilePath);
			targetPath = Path.Combine(dir ?? AppDomain.CurrentDomain.BaseDirectory, "flags_custom.toml");
		}
		else
		{
			string relativePath = Path.Combine("Assets", "jsonProtocols", "default", "flags_custom.toml");
			targetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
		}

		var sb = new System.Text.StringBuilder();
		if (File.Exists(targetPath))
		{
			sb.Append(File.ReadAllText(targetPath));
			if (!sb.ToString().EndsWith("\n")) sb.AppendLine();
		}

		sb.AppendLine();
		sb.AppendLine($"[flag.{def.Name}]");
		sb.AppendLine($"label = \"{def.Label}\"");
		sb.AppendLine($"type = \"{def.Type}\"");

		if (!string.IsNullOrWhiteSpace(def.Default))
			sb.AppendLine($"default = \"{def.Default}\"");
		if (!string.IsNullOrWhiteSpace(def.Description))
			sb.AppendLine($"description = \"{def.Description}\"");
		if (!string.IsNullOrWhiteSpace(def.Group))
			sb.AppendLine($"group = \"{def.Group}\"");
		if (!string.IsNullOrWhiteSpace(def.GroupType))
			sb.AppendLine($"group_type = \"{def.GroupType}\"");
		if (def.Min.HasValue)
			sb.AppendLine($"min = {def.Min.Value}");
		if (def.Max.HasValue)
			sb.AppendLine($"max = {def.Max.Value}");
		if (def.Options is { Count: > 0 })
		{
			var optsStr = string.Join(", ", def.Options.Select(o => $"\"{o}\""));
			sb.AppendLine($"options = [{optsStr}]");
		}

		if (!string.IsNullOrWhiteSpace(def.Group) && !string.IsNullOrWhiteSpace(groupLabel))
		{
			sb.AppendLine();
			sb.AppendLine($"[groups.{def.Group}]");
			sb.AppendLine($"label = \"{groupLabel}\"");
			sb.AppendLine("order = 50");
		}

		try
		{
			string? parentDir = Path.GetDirectoryName(targetPath);
			if (parentDir != null && !Directory.Exists(parentDir))
				Directory.CreateDirectory(parentDir);
			File.WriteAllText(targetPath, sb.ToString());
		}
		catch { }
	}
}
