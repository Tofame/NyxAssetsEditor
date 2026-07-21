using NyxAssetsEditor.Models.Looktypes;
using NyxAssetsEditor.Services.Looktypes;
using Xunit;

namespace NyxAssetsEditor.Tests;

public sealed class LooktypeInterchangeServiceTests
{
	private static LooktypeProfile Sample() => new()
	{
		AppearanceKind = LooktypeAppearanceKind.Outfit,
		LookType = 128,
		Head = 95,
		Body = 116,
		Legs = 121,
		Feet = 115,
		Addons = 3,
		Mount = 368,
		Corpse = 3058,
		Direction = LooktypeDirection.East,
		AnimationEnabled = true,
		AnimationPhase = 2,
		WalkIntervalMs = 175,
		AutoRotate = true,
		RotationIntervalMs = 900,
	};

	[Fact]
	public void LuaRoundTripPreservesAppearanceFields()
	{
		var exported = LooktypeInterchangeService.ExportLua(Sample());
		var imported = LooktypeInterchangeService.ImportLua(exported);

		Assert.True(imported.Success, imported.Error);
		Assert.Equal(128u, imported.Profile!.LookType);
		Assert.Equal((byte)95, imported.Profile.Head);
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
	public void XmlRoundTripPreservesItemModeAndCorpse()
	{
		var profile = Sample();
		profile.AppearanceKind = LooktypeAppearanceKind.Item;
		profile.LookType = 0;
		profile.LookTypeEx = 2160;

		var imported = LooktypeInterchangeService.ImportXml(LooktypeInterchangeService.ExportXml(profile));

		Assert.True(imported.Success, imported.Error);
		Assert.Equal(LooktypeAppearanceKind.Item, imported.Profile!.AppearanceKind);
		Assert.Equal(2160u, imported.Profile.LookTypeEx);
		Assert.Equal(3058u, imported.Profile.Corpse);
	}

	[Fact]
	public void PreviewMetadataIsOnlyExportedWhenEnabled()
	{
		var profile = Sample();
		Assert.DoesNotContain("nyx-preview", LooktypeInterchangeService.ExportLua(profile));

		profile.IncludePreviewSettings = true;
		var imported = LooktypeInterchangeService.ImportXml(LooktypeInterchangeService.ExportXml(profile));

		Assert.True(imported.Success, imported.Error);
		Assert.True(imported.Profile!.IncludePreviewSettings);
		Assert.Equal(LooktypeDirection.East, imported.Profile.Direction);
		Assert.True(imported.Profile.AnimationEnabled);
		Assert.Equal(2, imported.Profile.AnimationPhase);
		Assert.Equal(175, imported.Profile.WalkIntervalMs);
		Assert.True(imported.Profile.AutoRotate);
		Assert.Equal(900, imported.Profile.RotationIntervalMs);
	}

	[Fact]
	public void ImportsWrappedXmlAndClampsByteFields()
	{
		var imported = LooktypeInterchangeService.ImportXml(
			"<npc><look type=\"130\" head=\"999\" body=\"2\" legs=\"3\" feet=\"4\" addons=\"999\"/></npc>");

		Assert.True(imported.Success, imported.Error);
		Assert.Equal((byte)132, imported.Profile!.Head);
		Assert.Equal(byte.MaxValue, imported.Profile.Addons);
		Assert.Equal(2, imported.Warnings.Count);
	}

	[Theory]
	[InlineData("monster.outfit = { lookType = getOutfitId() }")]
	[InlineData("monster.outfit = { lookType = 128 + getOutfitId() }")]
	public void LuaImporterRejectsExecutableExpressions(string text)
	{
		var imported = LooktypeInterchangeService.ImportLua(text);

		Assert.False(imported.Success);
		Assert.Contains("integer literal", imported.Error ?? string.Empty);
	}

	[Fact]
	public void ImportsCommonCorpseSyntaxes()
	{
		var lua = LooktypeInterchangeService.ImportLua(
			"monster.outfit = { lookType = 128 }\nmonster.corpse = 3058");
		var xml = LooktypeInterchangeService.ImportXml(
			"<monster><look type=\"128\" corpse=\"3058\"/></monster>");

		Assert.True(lua.Success, lua.Error);
		Assert.True(xml.Success, xml.Error);
		Assert.Equal(3058u, lua.Profile!.Corpse);
		Assert.Equal(3058u, xml.Profile!.Corpse);
	}

	[Fact]
	public void LuaImportAcceptsAnyOutfitTableOwner()
	{
		var imported = LooktypeInterchangeService.ImportLua(
			"dragon.outfit = { lookType = 39, lookHead = 12 }\ndragon.corpse = 2881");

		Assert.True(imported.Success, imported.Error);
		Assert.Equal(39u, imported.Profile!.LookType);
		Assert.Equal((byte)12, imported.Profile.Head);
		Assert.Equal(2881u, imported.Profile.Corpse);
	}

	[Fact]
	public void BothAppearanceIdsPreferOutfitWithWarning()
	{
		var imported = LooktypeInterchangeService.ImportLua(
			"creature.outfit = { lookType = 128, lookTypeEx = 2160 }");

		Assert.True(imported.Success, imported.Error);
		Assert.Equal(LooktypeAppearanceKind.Outfit, imported.Profile!.AppearanceKind);
		Assert.Equal(128u, imported.Profile.LookType);
		Assert.Equal(0u, imported.Profile.LookTypeEx);
		Assert.Single(imported.Warnings);
	}
}
