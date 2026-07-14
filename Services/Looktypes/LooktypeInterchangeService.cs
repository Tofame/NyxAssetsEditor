using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NyxAssetsEditor.Models.Looktypes;

namespace NyxAssetsEditor.Services.Looktypes;

public sealed record LooktypeImportResult(LooktypeProfile? Profile, IReadOnlyList<string> Warnings, string? Error)
{
	public bool Success => Profile != null && Error == null;
}

public sealed record LooktypeDocumentImportResult(IReadOnlyList<LooktypeProfile> Profiles, IReadOnlyList<string> Warnings, string? Error)
{
	public bool Success => Profiles.Count > 0 && Error == null;
}

public static partial class LooktypeInterchangeService
{
	public static string ExportLua(LooktypeProfile profile)
	{
		var sb = new StringBuilder();
		sb.AppendLine("creature.outfit = {");
		if (profile.AppearanceKind == LooktypeAppearanceKind.Outfit)
			sb.AppendLine($"    lookType = {profile.LookType},");
		else
			sb.AppendLine($"    lookTypeEx = {profile.LookTypeEx},");
		sb.AppendLine($"    lookHead = {profile.Head},");
		sb.AppendLine($"    lookBody = {profile.Body},");
		sb.AppendLine($"    lookLegs = {profile.Legs},");
		sb.AppendLine($"    lookFeet = {profile.Feet},");
		sb.AppendLine($"    lookAddons = {profile.Addons},");
		sb.AppendLine($"    lookMount = {profile.Mount}");
		sb.AppendLine("}");
		if (profile.Corpse > 0) sb.AppendLine($"creature.corpse = {profile.Corpse}");
		if (profile.IncludeNameInExport) sb.AppendLine(NameMetadataComment(profile, "--"));
		if (profile.IncludePreviewSettings) sb.AppendLine(MetadataComment(profile, "--"));
		return sb.ToString();
	}

	public static string ExportXml(LooktypeProfile profile)
	{
		var look = new XElement("look");
		look.SetAttributeValue(profile.AppearanceKind == LooktypeAppearanceKind.Outfit ? "type" : "typeex",
			profile.AppearanceKind == LooktypeAppearanceKind.Outfit ? profile.LookType : profile.LookTypeEx);
		look.SetAttributeValue("head", profile.Head);
		look.SetAttributeValue("body", profile.Body);
		look.SetAttributeValue("legs", profile.Legs);
		look.SetAttributeValue("feet", profile.Feet);
		look.SetAttributeValue("addons", profile.Addons);
		look.SetAttributeValue("mount", profile.Mount);
		if (profile.Corpse > 0) look.SetAttributeValue("corpse", profile.Corpse);
		var output = look.ToString(SaveOptions.DisableFormatting) + Environment.NewLine;
		if (profile.IncludeNameInExport) output += $"<!-- {NameMetadata(profile)} -->" + Environment.NewLine;
		if (profile.IncludePreviewSettings) output += $"<!-- {Metadata(profile)} -->" + Environment.NewLine;
		return output;
	}

	public static string ExportLuaDocument(IEnumerable<LooktypeProfile> profiles)
	{
		var items = profiles.ToList();
		var sb = new StringBuilder();
		sb.AppendLine("local outfits = {");
		foreach (var profile in items)
		{
			sb.AppendLine("    {");
			if (profile.AppearanceKind == LooktypeAppearanceKind.Outfit) sb.AppendLine($"        lookType = {profile.LookType},");
			else sb.AppendLine($"        lookTypeEx = {profile.LookTypeEx},");
			sb.AppendLine($"        lookHead = {profile.Head},");
			sb.AppendLine($"        lookBody = {profile.Body},");
			sb.AppendLine($"        lookLegs = {profile.Legs},");
			sb.AppendLine($"        lookFeet = {profile.Feet},");
			sb.AppendLine($"        lookAddons = {profile.Addons},");
			sb.AppendLine($"        lookMount = {profile.Mount},");
			sb.AppendLine($"        corpse = {profile.Corpse}");
			if (profile.IncludeNameInExport) sb.AppendLine($"        {NameMetadataComment(profile, "--")}");
			if (profile.IncludePreviewSettings) sb.AppendLine($"        {MetadataComment(profile, "--")}");
			sb.AppendLine("    },");
		}
		sb.AppendLine("}");
		sb.AppendLine("return outfits");
		return sb.ToString();
	}

	public static string ExportXmlDocument(IEnumerable<LooktypeProfile> profiles)
	{
		var root = new XElement("looktypes");
		foreach (var profile in profiles)
		{
			var look = CreateLookElement(profile);
			root.Add(look);
			if (profile.IncludeNameInExport) root.Add(new XComment($" {NameMetadata(profile)} "));
			if (profile.IncludePreviewSettings) root.Add(new XComment($" {Metadata(profile)} "));
		}
		return new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString() + Environment.NewLine;
	}

	public static LooktypeDocumentImportResult ImportLuaDocument(string text, string suggestedName)
	{
		var tables = LuaTableRegex().Matches(text).Cast<Match>().ToList();
		if (tables.Count <= 1)
		{
			var name = ExtractNameMetadata(text) ?? suggestedName;
			var single = ImportLua(text, name);
			return single.Success
				? new(new[] { single.Profile! }, single.Warnings, null)
				: new(Array.Empty<LooktypeProfile>(), single.Warnings, single.Error);
		}

		var profiles = new List<LooktypeProfile>();
		var warnings = new List<string>();
		for (var index = 0; index < tables.Count; index++)
		{
			var table = tables[index];
			var end = index + 1 < tables.Count ? tables[index + 1].Index : text.Length;
			var entryText = text.Substring(table.Index, end - table.Index);
			var name = ExtractNameMetadata(entryText) ?? $"{suggestedName} {index + 1}";
			var result = ImportLua(entryText, name);
			if (result.Success) profiles.Add(result.Profile!);
			else warnings.Add($"Entry {index + 1}: {result.Error}");
			warnings.AddRange(result.Warnings.Select(warning => $"Entry {index + 1}: {warning}"));
		}

		return profiles.Count > 0
			? new(profiles, warnings, null)
			: new(profiles, warnings, "No valid Lua looktypes were found.");
	}

	public static LooktypeDocumentImportResult ImportXmlDocument(string text, string suggestedName)
	{
		var profiles = new List<LooktypeProfile>();
		var warnings = new List<string>();
		try
		{
			XDocument document;
			try { document = XDocument.Parse(text, LoadOptions.PreserveWhitespace); }
			catch { document = XDocument.Parse($"<nyx-root>{text}</nyx-root>", LoadOptions.PreserveWhitespace); }
			var looks = document.Descendants().Where(element => element.Name.LocalName.Equals("look", StringComparison.OrdinalIgnoreCase)).ToList();
			for (var index = 0; index < looks.Count; index++)
			{
				var look = looks[index];
				var metadata = string.Concat(look.NodesAfterSelf().TakeWhile(node => node is not XElement).OfType<XComment>());
				var name = ExtractNameMetadata(metadata);
				if (string.IsNullOrWhiteSpace(name)) name = looks.Count == 1 ? suggestedName : $"{suggestedName} {index + 1}";
				var result = ImportXml(look.ToString(SaveOptions.DisableFormatting) + metadata, name);
				if (result.Success) profiles.Add(result.Profile!);
				else warnings.Add($"Entry {index + 1}: {result.Error}");
				warnings.AddRange(result.Warnings.Select(warning => $"Entry {index + 1}: {warning}"));
			}
			return profiles.Count > 0
				? new(profiles, warnings, null)
				: new(profiles, warnings, "No valid <look> elements were found.");
		}
		catch (Exception ex)
		{
			return new(profiles, warnings, $"Could not import XML looktypes: {ex.Message}");
		}
	}

	public static LooktypeImportResult ImportLua(string text, string suggestedName)
	{
		var warnings = new List<string>();
		try
		{
			var values = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
			var dataText = Regex.Replace(text, @"--[^\r\n]*", string.Empty);
			foreach (Match match in LuaFieldRegex().Matches(dataText))
			{
				var valueText = match.Groups[2].Value.Trim();
				if (!uint.TryParse(valueText, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
					return new(null, warnings, $"Field '{match.Groups[1].Value}' must be a non-negative integer literal.");
				values[match.Groups[1].Value] = value;
			}

			var hasLookType = values.TryGetValue("lookType", out var lookType);
			var hasLookTypeEx = values.TryGetValue("lookTypeEx", out var lookTypeEx);
			if (!hasLookType && !hasLookTypeEx)
				return new(null, warnings, "No literal lookType or lookTypeEx field was found. Imported Lua is never executed.");
			if (lookType == 0 && lookTypeEx == 0)
				return new(null, warnings, "The imported looktype has no nonzero lookType or lookTypeEx value.");
			if (lookType > 0 && lookTypeEx > 0)
				warnings.Add("Both lookType and lookTypeEx were present; lookType was used.");

			var profile = CreateImportedProfile(suggestedName, lookType, lookTypeEx, values, warnings);
			ApplyMetadata(profile, text, warnings);
			return new(profile, warnings, null);
		}
		catch (Exception ex)
		{
			return new(null, warnings, $"Could not import Lua looktype: {ex.Message}");
		}
	}

	public static LooktypeImportResult ImportXml(string text, string suggestedName)
	{
		var warnings = new List<string>();
		try
		{
			XDocument document;
			try { document = XDocument.Parse(text, LoadOptions.PreserveWhitespace); }
			catch { document = XDocument.Parse($"<nyx-root>{text}</nyx-root>", LoadOptions.PreserveWhitespace); }

			var look = document.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("look", StringComparison.OrdinalIgnoreCase));
			if (look == null)
				return new(null, warnings, "No <look> element was found.");

			var values = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
			foreach (var attr in look.Attributes())
			{
				var key = attr.Name.LocalName.ToLowerInvariant() switch
				{
					"type" => "lookType", "typeex" => "lookTypeEx", "head" => "lookHead",
					"body" => "lookBody", "legs" => "lookLegs", "feet" => "lookFeet",
					"addons" => "lookAddons", "mount" => "lookMount", var other => other,
				};
				if (uint.TryParse(attr.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
					values[key] = value;
				else if (key.StartsWith("look", StringComparison.OrdinalIgnoreCase) || key.Equals("corpse", StringComparison.OrdinalIgnoreCase))
					return new(null, warnings, $"Attribute '{attr.Name}' must be a non-negative integer.");
			}

			var lookType = values.GetValueOrDefault("lookType");
			var lookTypeEx = values.GetValueOrDefault("lookTypeEx");
			if (lookType == 0 && lookTypeEx == 0)
				return new(null, warnings, "The <look> element has no nonzero type or typeex attribute.");
			if (lookType > 0 && lookTypeEx > 0)
				warnings.Add("Both type and typeex were present; type was used.");

			var profile = CreateImportedProfile(suggestedName, lookType, lookTypeEx, values, warnings);
			ApplyMetadata(profile, text, warnings);
			return new(profile, warnings, null);
		}
		catch (Exception ex)
		{
			return new(null, warnings, $"Could not import XML looktype: {ex.Message}");
		}
	}

	private static LooktypeProfile CreateImportedProfile(string name, uint lookType, uint lookTypeEx,
		IReadOnlyDictionary<string, uint> values, ICollection<string> warnings)
	{
		byte Color(string key)
		{
			var original = values.GetValueOrDefault(key);
			var clamped = Math.Min(original, TibiaOutfitPalette.ColorCount - 1);
			if (original != clamped) warnings.Add($"{key} was clamped to {TibiaOutfitPalette.ColorCount - 1}.");
			return (byte)clamped;
		}
		var addons = values.GetValueOrDefault("lookAddons");
		if (addons > byte.MaxValue) warnings.Add($"lookAddons was clamped to {byte.MaxValue}.");

		return new LooktypeProfile
		{
			Id = Guid.NewGuid(),
			Name = string.IsNullOrWhiteSpace(name) ? "Imported Looktype" : name,
			AppearanceKind = lookType > 0 ? LooktypeAppearanceKind.Outfit : LooktypeAppearanceKind.Item,
			LookType = lookType,
			LookTypeEx = lookType > 0 ? 0 : lookTypeEx,
			Head = Color("lookHead"), Body = Color("lookBody"), Legs = Color("lookLegs"), Feet = Color("lookFeet"),
			Addons = (byte)Math.Min(addons, byte.MaxValue),
			Mount = values.GetValueOrDefault("lookMount"),
			Corpse = values.GetValueOrDefault("corpse"),
		};
	}

	private static XElement CreateLookElement(LooktypeProfile profile)
	{
		var look = new XElement("look");
		look.SetAttributeValue(profile.AppearanceKind == LooktypeAppearanceKind.Outfit ? "type" : "typeex",
			profile.AppearanceKind == LooktypeAppearanceKind.Outfit ? profile.LookType : profile.LookTypeEx);
		look.SetAttributeValue("head", profile.Head); look.SetAttributeValue("body", profile.Body);
		look.SetAttributeValue("legs", profile.Legs); look.SetAttributeValue("feet", profile.Feet);
		look.SetAttributeValue("addons", profile.Addons); look.SetAttributeValue("mount", profile.Mount);
		if (profile.Corpse > 0) look.SetAttributeValue("corpse", profile.Corpse);
		return look;
	}
	private static string NameMetadataComment(LooktypeProfile profile, string prefix) => $"{prefix} {NameMetadata(profile)}";
	private static string NameMetadata(LooktypeProfile profile) =>
		$"nyx-looktype name=\"{EscapeMetadataName(profile.Name)}\"";
	private static string? ExtractNameMetadata(string text)
	{
		var match = NameMetadataRegex().Match(text);
		if (!match.Success) return null;
		return UnescapeMetadataName(match.Groups[1].Value);
	}
	private static string EscapeMetadataName(string value)
	{
		var sb = new StringBuilder(value.Length);
		foreach (var character in value)
		{
			switch (character)
			{
				case '\\': sb.Append("\\\\"); break;
				case '"': sb.Append("\\\""); break;
				case '\r': sb.Append("\\r"); break;
				case '\n': sb.Append("\\n"); break;
				default:
					if (character < ' ') sb.Append($"\\u{(int)character:X4}");
					else sb.Append(character);
					break;
			}
		}
		return sb.ToString().Replace("--", "-\\u002D", StringComparison.Ordinal);
	}
	private static string UnescapeMetadataName(string value)
	{
		var sb = new StringBuilder(value.Length);
		for (var index = 0; index < value.Length; index++)
		{
			if (value[index] != '\\' || index + 1 >= value.Length)
			{
				sb.Append(value[index]);
				continue;
			}

			var escaped = value[++index];
			switch (escaped)
			{
				case '\\': sb.Append('\\'); break;
				case '"': sb.Append('"'); break;
				case 'r': sb.Append('\r'); break;
				case 'n': sb.Append('\n'); break;
				case 'u' when index + 4 < value.Length &&
					int.TryParse(value.AsSpan(index + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint):
					sb.Append((char)codePoint);
					index += 4;
					break;
				default: sb.Append('\\').Append(escaped); break;
			}
		}
		return sb.ToString();
	}
	private static string MetadataComment(LooktypeProfile profile, string prefix) => $"{prefix} {Metadata(profile)}";
	private static string Metadata(LooktypeProfile profile) =>
		$"nyx-preview direction={profile.Direction}; animation={profile.AnimationEnabled.ToString().ToLowerInvariant()}; phase={profile.AnimationPhase}; walkIntervalMs={profile.WalkIntervalMs}; autoRotate={profile.AutoRotate.ToString().ToLowerInvariant()}; rotationIntervalMs={profile.RotationIntervalMs}";

	private static void ApplyMetadata(LooktypeProfile profile, string text, ICollection<string> warnings)
	{
		var match = MetadataRegex().Match(text);
		if (!match.Success) return;
		var fields = match.Groups[1].Value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
			.Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries)).Where(parts => parts.Length == 2)
			.ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
		if (Enum.TryParse<LooktypeDirection>(fields.GetValueOrDefault("direction"), true, out var direction) &&
			Enum.IsDefined(direction)) profile.Direction = direction;
		else if (fields.ContainsKey("direction")) warnings.Add("Nyx preview direction was invalid and was ignored.");
		if (bool.TryParse(fields.GetValueOrDefault("animation"), out var animation)) profile.AnimationEnabled = animation;
		if (int.TryParse(fields.GetValueOrDefault("phase"), out var phase)) profile.AnimationPhase = Math.Max(0, phase);
		if (int.TryParse(fields.GetValueOrDefault("walkIntervalMs"), out var interval)) profile.WalkIntervalMs = Math.Max(0, interval);
		if (bool.TryParse(fields.GetValueOrDefault("autoRotate"), out var rotate)) profile.AutoRotate = rotate;
		if (int.TryParse(fields.GetValueOrDefault("rotationIntervalMs"), out var rotationInterval)) profile.RotationIntervalMs = Math.Max(16, rotationInterval);
		profile.IncludePreviewSettings = true;
	}
	[GeneratedRegex(@"\b(lookTypeEx|lookType|lookHead|lookBody|lookLegs|lookFeet|lookAddons|lookMount|corpse)\s*=\s*([^,;}\r\n]+)", RegexOptions.IgnoreCase)]
	private static partial Regex LuaFieldRegex();

	[GeneratedRegex(@"\{[^{}]*\b(?:lookTypeEx|lookType)\s*=\s*[^{}]*\}", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex LuaTableRegex();

	[GeneratedRegex(@"nyx-looktype\s+name=""((?:\\.|[^""\\])*)""", RegexOptions.IgnoreCase)]
	private static partial Regex NameMetadataRegex();

	[GeneratedRegex(@"nyx-preview\s+(direction\s*=[^\r\n<]*?(?:rotationIntervalMs\s*=\s*\d+))", RegexOptions.IgnoreCase)]
	private static partial Regex MetadataRegex();

}
