using System;
using System.Linq;
using NyxAssetsEditor.Models.Looktypes;
using NyxAssetsEditor.Services.Looktypes;
using Xunit;

namespace NyxAssetsEditor.Tests;

public sealed class LooktypeInterchangeServiceTests
{
	private static LooktypeProfile Sample() => new()
	{
		Name = "Citizen", AppearanceKind = LooktypeAppearanceKind.Outfit, LookType = 128,
		Head = 95, Body = 116, Legs = 121, Feet = 115, Addons = 3, Mount = 368, Corpse = 3058,
		Direction = LooktypeDirection.East, AnimationEnabled = true, AnimationPhase = 2,
		WalkIntervalMs = 175, AutoRotate = true, RotationIntervalMs = 900,
	};

	[Fact]
	public void LuaRoundTripPreservesAppearanceFieldsOnly()
	{
		var exported = LooktypeInterchangeService.ExportLua(Sample());
		Assert.DoesNotContain("nyx-", exported);
		var imported = LooktypeInterchangeService.ImportLua(exported, "Imported");
		Assert.True(imported.Success, imported.Error);
		Assert.Equal(128u, imported.Profile!.LookType);
		Assert.Equal((byte)3, imported.Profile.Addons);
		Assert.Equal(368u, imported.Profile.Mount);
		Assert.Equal(3058u, imported.Profile.Corpse);
		Assert.Contains("creature.outfit = {", exported);
		Assert.Contains("creature.corpse = 3058", exported);
		Assert.Equal(LooktypeDirection.South, imported.Profile.Direction);
		Assert.Equal(0, imported.Profile.WalkIntervalMs);
		Assert.False(imported.Profile.AutoRotate);
	}

	[Fact]
	public void SingleExportsIncludeNameWhenEnabled()
	{
		var profile = Sample(); profile.Name = "hehe xd";

		var lua = LooktypeInterchangeService.ExportLua(profile);
		var xml = LooktypeInterchangeService.ExportXml(profile);

		Assert.Contains("-- nyx-looktype name=\"hehe xd\"", lua);
		Assert.Contains("<!-- nyx-looktype name=\"hehe xd\" -->", xml);
		Assert.DoesNotContain("name = \"hehe xd\"", lua);
		Assert.DoesNotContain("name=\"hehe xd\"", xml);
		Assert.Equal("hehe xd", LooktypeInterchangeService.ImportLuaDocument(lua, "Fallback").Profiles[0].Name);
		Assert.Equal("hehe xd", LooktypeInterchangeService.ImportXmlDocument(xml, "Fallback").Profiles[0].Name);
	}

	[Fact]
	public void NameMetadataSafelyRoundTripsSpecialCharacters()
	{
		var profile = Sample(); profile.Name = "A \"quoted\" -- name \\ path";

		var lua = LooktypeInterchangeService.ImportLuaDocument(LooktypeInterchangeService.ExportLua(profile), "Fallback");
		var xml = LooktypeInterchangeService.ImportXmlDocument(LooktypeInterchangeService.ExportXml(profile), "Fallback");

		Assert.Equal(profile.Name, lua.Profiles[0].Name);
		Assert.Equal(profile.Name, xml.Profiles[0].Name);
	}

	[Fact]
	public void NameMetadataIsNotParsedAsPreviewMetadata()
	{
		var profile = Sample(); profile.Name = "rotationIntervalMs=42";
		var imported = LooktypeInterchangeService.ImportLuaDocument(
			LooktypeInterchangeService.ExportLua(profile), "Fallback");

		Assert.Equal(profile.Name, imported.Profiles[0].Name);
		Assert.False(imported.Profiles[0].IncludePreviewSettings);
	}

	[Fact]
	public void NamesOutsideNyxMetadataAreIgnored()
	{
		var lua = LooktypeInterchangeService.ImportLuaDocument(
			"creature.outfit = { name = \"Data Name\", lookType = 128 }", "Lua Fallback");
		var xml = LooktypeInterchangeService.ImportXmlDocument(
			"<look name=\"Attribute Name\" type=\"128\" />", "XML Fallback");

		Assert.Equal("Lua Fallback", lua.Profiles[0].Name);
		Assert.Equal("XML Fallback", xml.Profiles[0].Name);
	}

	[Fact]
	public void ExportsOmitNameWhenDisabled()
	{
		var profile = Sample(); profile.IncludeNameInExport = false;

		Assert.False(LooktypeInterchangeService.ExportLua(profile).Contains("name", StringComparison.OrdinalIgnoreCase));
		Assert.False(LooktypeInterchangeService.ExportXml(profile).Contains("name", StringComparison.OrdinalIgnoreCase));
		Assert.False(LooktypeInterchangeService.ExportLuaDocument(new[] { profile }).Contains("name", StringComparison.OrdinalIgnoreCase));
		Assert.False(LooktypeInterchangeService.ExportXmlDocument(new[] { profile }).Contains("name", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void OptInPreviewMetadataRoundTripPreservesPreviewSettings()
	{
		var profile = Sample(); profile.IncludePreviewSettings = true;
		var exported = LooktypeInterchangeService.ExportXml(profile);
		Assert.Contains("nyx-preview", exported);
		var imported = LooktypeInterchangeService.ImportXml(exported, "Imported");
		Assert.True(imported.Success, imported.Error);
		Assert.True(imported.Profile!.IncludePreviewSettings);
		Assert.Equal(LooktypeDirection.East, imported.Profile.Direction);
		Assert.Equal(175, imported.Profile.WalkIntervalMs);
		Assert.True(imported.Profile.AutoRotate);
		Assert.Equal(900, imported.Profile.RotationIntervalMs);
	}

	[Fact]
	public void XmlRoundTripPreservesItemMode()
	{
		var profile = Sample(); profile.AppearanceKind = LooktypeAppearanceKind.Item; profile.LookType = 0; profile.LookTypeEx = 2160;
		var imported = LooktypeInterchangeService.ImportXml(LooktypeInterchangeService.ExportXml(profile), "Item");
		Assert.True(imported.Success, imported.Error);
		Assert.Equal(LooktypeAppearanceKind.Item, imported.Profile!.AppearanceKind);
		Assert.Equal(2160u, imported.Profile.LookTypeEx);
		Assert.Equal(3058u, imported.Profile.Corpse);
	}

	[Fact]
	public void ImportsWrappedTfsXmlAndClampsColors()
	{
		var imported = LooktypeInterchangeService.ImportXml("<npc><look type=\"130\" head=\"999\" body=\"2\" legs=\"3\" feet=\"4\" addons=\"999\"/></npc>", "Npc");
		Assert.True(imported.Success, imported.Error);
		Assert.Equal((byte)132, imported.Profile!.Head);
		Assert.Equal(byte.MaxValue, imported.Profile.Addons);
		Assert.Equal(2, imported.Warnings.Count);
	}

	[Fact]
	public void LuaImporterNeverAcceptsExecutableExpressionsAsValues()
	{
		var imported = LooktypeInterchangeService.ImportLua("monster.outfit = { lookType = getOutfitId() }", "Unsafe");
		Assert.False(imported.Success);
		Assert.Contains("integer literal", imported.Error);
	}

	[Fact]
	public void LuaImporterRejectsExpressionsStartingWithAnInteger()
	{
		var imported = LooktypeInterchangeService.ImportLua("monster.outfit = { lookType = 128 + getOutfitId() }", "Unsafe");
		Assert.False(imported.Success);
		Assert.Contains("integer literal", imported.Error);
	}

	[Fact]
	public void LuaDocumentRoundTripPreservesMultipleLooktypes()
	{
		var second = Sample(); second.Name = "Item"; second.AppearanceKind = LooktypeAppearanceKind.Item;
		second.LookType = 0; second.LookTypeEx = 2160;
		var imported = LooktypeInterchangeService.ImportLuaDocument(
			LooktypeInterchangeService.ExportLuaDocument(new[] { Sample(), second }), "Outfits");
		Assert.True(imported.Success, imported.Error);
		Assert.Equal(2, imported.Profiles.Count);
		Assert.Equal("Citizen", imported.Profiles[0].Name);
		Assert.Equal(2160u, imported.Profiles[1].LookTypeEx);
	}

	[Fact]
	public void XmlDocumentRoundTripPreservesMultipleLooktypes()
	{
		var second = Sample(); second.Name = "Mage"; second.LookType = 130;
		var imported = LooktypeInterchangeService.ImportXmlDocument(
			LooktypeInterchangeService.ExportXmlDocument(new[] { Sample(), second }), "Outfits");
		Assert.True(imported.Success, imported.Error);
		Assert.Equal(new uint[] { 128, 130 }, imported.Profiles.Select(profile => profile.LookType));
		Assert.Equal(new[] { "Citizen", "Mage" }, imported.Profiles.Select(profile => profile.Name));
	}

	[Fact]
	public void ImportsReferenceRepositoryCorpseSyntax()
	{
		var lua = LooktypeInterchangeService.ImportLua(
			"monster.outfit = { lookType = 128 }\nmonster.corpse = 3058", "Lua Monster");
		var xml = LooktypeInterchangeService.ImportXml(
			"<monster><look type=\"128\" corpse=\"3058\"/></monster>", "XML Monster");

		Assert.True(lua.Success, lua.Error);
		Assert.True(xml.Success, xml.Error);
		Assert.Equal(3058u, lua.Profile!.Corpse);
		Assert.Equal(3058u, xml.Profile!.Corpse);
	}

	[Fact]
	public void LuaImportAcceptsAnyOutfitTableOwner()
	{
		var imported = LooktypeInterchangeService.ImportLua(
			"dragon.outfit = { lookType = 39, lookHead = 12 }\ndragon.corpse = 2881", "Dragon");

		Assert.True(imported.Success, imported.Error);
		Assert.Equal(39u, imported.Profile!.LookType);
		Assert.Equal((byte)12, imported.Profile.Head);
		Assert.Equal(2881u, imported.Profile.Corpse);
	}
}
