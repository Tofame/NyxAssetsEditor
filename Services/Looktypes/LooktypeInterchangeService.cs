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
		if (profile.IncludePreviewSettings) output += $"<!-- {Metadata(profile)} -->" + Environment.NewLine;
		return output;
	}

	public static LooktypeImportResult ImportLua(string text)
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

			var profile = CreateImportedProfile(lookType, lookTypeEx, values, warnings);
			ApplyMetadata(profile, text, warnings);
			return new(profile, warnings, null);
		}
		catch (Exception ex)
		{
			return new(null, warnings, $"Could not import Lua looktype: {ex.Message}");
		}
	}

	public static LooktypeImportResult ImportXml(string text)
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

			var profile = CreateImportedProfile(lookType, lookTypeEx, values, warnings);
			ApplyMetadata(profile, text, warnings);
			return new(profile, warnings, null);
		}
		catch (Exception ex)
		{
			return new(null, warnings, $"Could not import XML looktype: {ex.Message}");
		}
	}

	private static LooktypeProfile CreateImportedProfile(uint lookType, uint lookTypeEx,
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
			AppearanceKind = lookType > 0 ? LooktypeAppearanceKind.Outfit : LooktypeAppearanceKind.Item,
			LookType = lookType,
			LookTypeEx = lookType > 0 ? 0 : lookTypeEx,
			Head = Color("lookHead"), Body = Color("lookBody"), Legs = Color("lookLegs"), Feet = Color("lookFeet"),
			Addons = (byte)Math.Min(addons, byte.MaxValue),
			Mount = values.GetValueOrDefault("lookMount"),
			Corpse = values.GetValueOrDefault("corpse"),
		};
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

	[GeneratedRegex(@"nyx-preview\s+(direction\s*=[^\r\n<]*?(?:rotationIntervalMs\s*=\s*\d+))", RegexOptions.IgnoreCase)]
	private static partial Regex MetadataRegex();

}
