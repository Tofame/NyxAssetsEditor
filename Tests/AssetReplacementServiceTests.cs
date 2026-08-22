using System.Threading.Tasks;
using NyxAssets.Things;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.Services.Replacement;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;
using NyxAssetsEditor.ViewModels.Pages;
using Xunit;

namespace NyxAssetsEditor.Tests;

public class AssetReplacementServiceTests
{
	[Fact]
	public async Task ThingRange_ReplacesDefinitionAndReferencedPixelsAtSameIds()
	{
		var source = await CreatePair(spriteCount: 1);
		var target = await CreatePair(spriteCount: 1);
		var sourcePixels = SolidPixels(27);
		var originalTargetPixels = SolidPixels(91);
		source.SpritePanel.Loader.SetSpritePixels(1, sourcePixels);
		target.SpritePanel.Loader.SetSpritePixels(1, originalTargetPixels);
		PutItem(source, 100, 1, pickupable: true);
		PutItem(target, 100, 1, pickupable: false);

		var batch = AssetReplacementService.Prepare(new AssetReplacementRequest(
			source, target, AssetReplacementMode.Things, ThingKind.Item, 100, 100, AddMissingTargetIds: false));
		var result = AssetReplacementService.Apply(batch);

		Assert.True(result.Succeeded);
		Assert.True(target.ThingsPanel.Catalog!.TryGetItem(100)!.Pickupable);
		Assert.Equal(new[] { 2u }, target.ThingsPanel.Catalog.TryGetItem(100)!.FrameGroups[0].SpriteIds);
		Assert.Equal(originalTargetPixels, target.SpritePanel.Loader.LoadSpritePixels(1));
		Assert.Equal(sourcePixels, target.SpritePanel.Loader.LoadSpritePixels(2));
		Assert.Contains(100u, target.ThingsPanel.ModifiedThingIds);
		Assert.Contains(2u, target.SpritePanel.AddedSpriteIds);

		Assert.NotNull(result.Transaction);
		Assert.True(result.Transaction.TryUndo(out var undoError), undoError);
		Assert.False(target.ThingsPanel.Catalog.TryGetItem(100)!.Pickupable);
		Assert.Equal(originalTargetPixels, target.SpritePanel.Loader.LoadSpritePixels(1));
		Assert.Equal(1u, target.SpritePanel.Loader.SpriteCount);

		Assert.True(result.Transaction.TryRedo(out var redoError), redoError);
		Assert.True(target.ThingsPanel.Catalog.TryGetItem(100)!.Pickupable);
		Assert.Equal(sourcePixels, target.SpritePanel.Loader.LoadSpritePixels(2));
	}

	[Fact]
	public async Task Range_WithNoSafeMatchesDoesNotMutate()
	{
		var source = await CreatePair(spriteCount: 1);
		var target = await CreatePair(spriteCount: 1);
		PutItem(source, 100, 1, pickupable: true);
		PutItem(target, 100, 1, pickupable: false);

		var batch = AssetReplacementService.Prepare(new AssetReplacementRequest(
			source, target, AssetReplacementMode.Things, ThingKind.Item, 101, 101, AddMissingTargetIds: false));

		Assert.False(batch.CanApply);
		Assert.Single(batch.Skipped);
		Assert.False(target.ThingsPanel.Catalog!.TryGetItem(100)!.Pickupable);
	}

	[Fact]
	public async Task Range_ReplacesSafeIntersectionAndReportsGaps()
	{
		var source = await CreatePair(spriteCount: 1);
		var target = await CreatePair(spriteCount: 1);
		PutItem(source, 100, 1, pickupable: true);
		PutItem(target, 100, 1, pickupable: false);

		var batch = AssetReplacementService.Prepare(new AssetReplacementRequest(
			source, target, AssetReplacementMode.Things, ThingKind.Item, 100, 101, AddMissingTargetIds: false));
		var result = AssetReplacementService.Apply(batch);

		Assert.True(result.Succeeded);
		Assert.Single(result.Skipped);
		Assert.Equal(101u, result.Skipped[0].Id);
		Assert.True(target.ThingsPanel.Catalog!.TryGetItem(100)!.Pickupable);
	}

	[Fact]
	public async Task Range_SkipsMissingTargetWithoutCreatingIt()
	{
		var source = await CreatePair(spriteCount: 1);
		var target = await CreatePair(spriteCount: 1);
		PutItem(source, 100, 1, pickupable: true);
		PutItem(source, 101, 1, pickupable: true);
		PutItem(target, 100, 1, pickupable: false);

		var batch = AssetReplacementService.Prepare(new AssetReplacementRequest(
			source, target, AssetReplacementMode.Things, ThingKind.Item, 100, 101, AddMissingTargetIds: false));
		var result = AssetReplacementService.Apply(batch);

		Assert.True(result.Succeeded);
		Assert.Single(result.Skipped);
		Assert.Equal(101u, result.Skipped[0].Id);
		Assert.Null(target.ThingsPanel.Catalog!.TryGetItem(101));
	}

	[Fact]
	public async Task ThingRange_AppendsMissingTargetSpritesInsteadOfSkipping()
	{
		var source = await CreatePair(spriteCount: 2);
		var target = await CreatePair(spriteCount: 1);
		var expected = SolidPixels(19);
		source.SpritePanel.Loader.SetSpritePixels(2, expected);
		PutItem(source, 100, 2, pickupable: true);
		PutItem(target, 100, 2, pickupable: false);

		var batch = AssetReplacementService.Prepare(new AssetReplacementRequest(
			source, target, AssetReplacementMode.Things, ThingKind.Item, 100, 100, AddMissingTargetIds: false));
		var result = AssetReplacementService.Apply(batch);

		Assert.True(result.Succeeded);
		Assert.Empty(batch.Skipped);
		Assert.True(target.ThingsPanel.Catalog!.TryGetItem(100)!.Pickupable);
		Assert.Equal(new[] { 2u }, target.ThingsPanel.Catalog.TryGetItem(100)!.FrameGroups[0].SpriteIds);
		Assert.Equal(expected, target.SpritePanel.Loader.LoadSpritePixels(2));
	}

	[Fact]
	public async Task ThingRange_ImportsDifferingPixelsInsteadOfOverwritingExistingSprite()
	{
		var source = await CreatePair(spriteCount: 2);
		var target = await CreatePair(spriteCount: 1);
		var expected = SolidPixels(52);
		var originalTarget = SolidPixels(11);
		source.SpritePanel.Loader.SetSpritePixels(2, expected);
		target.SpritePanel.Loader.SetSpritePixels(1, originalTarget);
		PutItem(source, 100, 2, pickupable: true);
		PutItem(target, 100, 1, pickupable: false);

		var batch = AssetReplacementService.Prepare(new AssetReplacementRequest(
			source, target, AssetReplacementMode.Things, ThingKind.Item, 100, 100,
			AddMissingTargetIds: false));
		var result = AssetReplacementService.Apply(batch);

		Assert.True(result.Succeeded);
		var replaced = target.ThingsPanel.Catalog!.TryGetItem(100)!;
		Assert.True(replaced.Pickupable);
		Assert.Equal(new[] { 2u }, replaced.FrameGroups[0].SpriteIds);
		Assert.Equal(originalTarget, target.SpritePanel.Loader.LoadSpritePixels(1));
		Assert.Equal(expected, target.SpritePanel.Loader.LoadSpritePixels(2));
	}

	[Fact]
	public async Task ThingRange_CanAppendAdditionalMappedSpriteSlots()
	{
		var source = await CreatePair(spriteCount: 2);
		var target = await CreatePair(spriteCount: 1);
		var expected = SolidPixels(68);
		source.SpritePanel.Loader.SetSpritePixels(2, expected);
		PutItemWithSprites(source, 100, true, 1, 2);
		PutItem(target, 100, 1, pickupable: false);

		var batch = AssetReplacementService.Prepare(new AssetReplacementRequest(
			source, target, AssetReplacementMode.Things, ThingKind.Item, 100, 100,
			AddMissingTargetIds: true));
		var result = AssetReplacementService.Apply(batch);

		Assert.True(result.Succeeded);
		Assert.Equal(new[] { 1u, 2u }, target.ThingsPanel.Catalog!.TryGetItem(100)!.FrameGroups[0].SpriteIds);
		Assert.Equal(expected, target.SpritePanel.Loader.LoadSpritePixels(2));
	}

	[Fact]
	public async Task ThingRange_DeduplicatesExtraSourceSpritesAcrossTheBatch()
	{
		var source = await CreatePair(spriteCount: 2);
		var target = await CreatePair(spriteCount: 1);
		PutItemWithSprites(source, 100, true, 1, 2);
		PutItemWithSprites(source, 101, true, 1, 2);
		PutItem(target, 100, 1, pickupable: false);
		PutItem(target, 101, 1, pickupable: false);

		var batch = AssetReplacementService.Prepare(new AssetReplacementRequest(
			source, target, AssetReplacementMode.Things, ThingKind.Item, 100, 101,
			AddMissingTargetIds: true));
		var result = AssetReplacementService.Apply(batch);

		Assert.True(result.Succeeded);
		Assert.Equal(2u, target.SpritePanel.Loader.SpriteCount);
		Assert.Equal(new[] { 1u, 2u }, target.ThingsPanel.Catalog!.TryGetItem(100)!.FrameGroups[0].SpriteIds);
		Assert.Equal(new[] { 1u, 2u }, target.ThingsPanel.Catalog.TryGetItem(101)!.FrameGroups[0].SpriteIds);
	}

	[Fact]
	public async Task EffectRange_ImportsDifferingPixelsInsteadOfOverwritingExistingSprite()
	{
		var source = await CreatePair(spriteCount: 2);
		var target = await CreatePair(spriteCount: 1);
		var expected = SolidPixels(84);
		var originalTarget = SolidPixels(3);
		source.SpritePanel.Loader.SetSpritePixels(2, expected);
		target.SpritePanel.Loader.SetSpritePixels(1, originalTarget);
		PutEffect(source, 1, 2, hasLight: true);
		PutEffect(target, 1, 1, hasLight: false);

		var batch = AssetReplacementService.Prepare(new AssetReplacementRequest(
			source, target, AssetReplacementMode.Things, ThingKind.Effect, 1, 1,
			AddMissingTargetIds: false));
		var result = AssetReplacementService.Apply(batch);

		Assert.True(result.Succeeded);
		var replaced = target.ThingsPanel.Catalog!.TryGetEffect(1)!;
		Assert.True(replaced.HasLight);
		Assert.Equal(new[] { 2u }, replaced.FrameGroups[0].SpriteIds);
		Assert.Equal(originalTarget, target.SpritePanel.Loader.LoadSpritePixels(1));
		Assert.Equal(expected, target.SpritePanel.Loader.LoadSpritePixels(2));
	}

	[Fact]
	public async Task EffectRange_TreatsZeroSpriteIdsAsEmptyFrameSlots()
	{
		var source = await CreatePair(spriteCount: 2);
		var target = await CreatePair(spriteCount: 1);
		PutEffectWithSprites(source, 1, true, 2, 2, 0);
		PutEffect(target, 1, 1, hasLight: false);

		var batch = AssetReplacementService.Prepare(new AssetReplacementRequest(
			source, target, AssetReplacementMode.Things, ThingKind.Effect, 1, 1,
			AddMissingTargetIds: false));
		var result = AssetReplacementService.Apply(batch);

		Assert.True(result.Succeeded);
		Assert.NotEmpty(batch.Warnings);
		Assert.Equal(new[] { 2u, 2u, 0u }, target.ThingsPanel.Catalog!.TryGetEffect(1)!.FrameGroups[0].SpriteIds);
	}

	[Fact]
	public async Task EffectRange_WithTargetCreationEnabledReportsCompletedFrameAdjustment()
	{
		var source = await CreatePair(spriteCount: 2);
		var target = await CreatePair(spriteCount: 1);
		PutEffectWithSprites(source, 1, true, 2, 1, 2);
		PutEffect(target, 1, 1, hasLight: false);

		var batch = AssetReplacementService.Prepare(new AssetReplacementRequest(
			source, target, AssetReplacementMode.Things, ThingKind.Effect, 1, 1,
			AddMissingTargetIds: true));
		var result = AssetReplacementService.Apply(batch);

		Assert.True(result.Succeeded);
		Assert.Contains(batch.Warnings, warning => warning.Contains("No additional target sprite IDs are required"));
		Assert.DoesNotContain(batch.Warnings, warning => warning.Contains("Enable Create missing target IDs"));
		Assert.Equal(new[] { 1u, 1u, 1u }, target.ThingsPanel.Catalog!.TryGetEffect(1)!.FrameGroups[0].SpriteIds);
	}

	[Fact]
	public async Task SpriteRange_AppendsMissingTargetIdsAndSkipsIdenticalPixels()
	{
		var source = await CreatePair(spriteCount: 2);
		var target = await CreatePair(spriteCount: 1);
		var expected = SolidPixels(44);
		var identical = SolidPixels(8);
		source.SpritePanel.Loader.SetSpritePixels(1, identical);
		target.SpritePanel.Loader.SetSpritePixels(1, identical);
		source.SpritePanel.Loader.SetSpritePixels(2, expected);

		var batch = AssetReplacementService.Prepare(new AssetReplacementRequest(
			source, target, AssetReplacementMode.Sprites, null, 1, 2, AddMissingTargetIds: false));
		var result = AssetReplacementService.Apply(batch);

		Assert.True(result.Succeeded);
		Assert.Single(batch.Skipped);
		Assert.Equal(1u, batch.Skipped[0].Id);
		Assert.Empty(batch.DiscardedSpritePixels);
		Assert.Equal(identical, target.SpritePanel.Loader.LoadSpritePixels(1));
		Assert.Equal(expected, target.SpritePanel.Loader.LoadSpritePixels(2));
	}

	[Fact]
	public async Task SpriteRange_RecordsDiscardedTargetPixelsWhenTheyDiffer()
	{
		var source = await CreatePair(spriteCount: 1);
		var target = await CreatePair(spriteCount: 1);
		var incoming = SolidPixels(44);
		var discarded = SolidPixels(9);
		source.SpritePanel.Loader.SetSpritePixels(1, incoming);
		target.SpritePanel.Loader.SetSpritePixels(1, discarded);

		var batch = AssetReplacementService.Prepare(new AssetReplacementRequest(
			source, target, AssetReplacementMode.Sprites, null, 1, 1, AddMissingTargetIds: false));

		Assert.True(batch.CanApply);
		Assert.Equal(discarded, batch.DiscardedSpritePixels[1]);
		var result = AssetReplacementService.Apply(batch);
		Assert.True(result.Succeeded);
		Assert.Equal(incoming, target.SpritePanel.Loader.LoadSpritePixels(1));
	}

	[Fact]
	public async Task AddMissingTargets_AppendsThingAndReferencedSpriteAndSupportsUndo()
	{
		var source = await CreatePair(spriteCount: 2);
		var target = await CreatePair(spriteCount: 1);
		var expected = SolidPixels(63);
		source.SpritePanel.Loader.SetSpritePixels(2, expected);
		PutItem(source, 100, 2, pickupable: true);
		var originalItemCount = target.ThingsPanel.Catalog!.ItemCount;

		var batch = AssetReplacementService.Prepare(new AssetReplacementRequest(
			source, target, AssetReplacementMode.Things, ThingKind.Item, 100, 100,
			AddMissingTargetIds: true));
		var result = AssetReplacementService.Apply(batch);

		Assert.True(result.Succeeded);
		Assert.True(target.ThingsPanel.Catalog.TryGetItem(100)!.Pickupable);
		Assert.Equal(2u, target.SpritePanel.Loader.SpriteCount);
		Assert.Equal(expected, target.SpritePanel.Loader.LoadSpritePixels(2));
		Assert.Contains(100u, target.ThingsPanel.AddedThingIds);
		Assert.Contains(2u, target.SpritePanel.AddedSpriteIds);

		target.ThingsPanel.UndoCommand.Execute(null);
		target.SpritePanel.UndoCommand.Execute(null);
		Assert.Equal(originalItemCount, target.ThingsPanel.Catalog.ItemCount);
		Assert.Equal(1u, target.SpritePanel.Loader.SpriteCount);
	}

	[Fact]
	public async Task AddMissingTargets_AppendsRawSprite()
	{
		var source = await CreatePair(spriteCount: 2);
		var target = await CreatePair(spriteCount: 1);
		var expected = SolidPixels(77);
		source.SpritePanel.Loader.SetSpritePixels(2, expected);

		var batch = AssetReplacementService.Prepare(new AssetReplacementRequest(
			source, target, AssetReplacementMode.Sprites, null, 2, 2,
			AddMissingTargetIds: true));
		var result = AssetReplacementService.Apply(batch);

		Assert.True(result.Succeeded);
		Assert.Equal(2u, target.SpritePanel.Loader.SpriteCount);
		Assert.Equal(expected, target.SpritePanel.Loader.LoadSpritePixels(2));
		Assert.Contains(2u, target.SpritePanel.AddedSpriteIds);
	}

	[Fact]
	public async Task AddMissingTargets_RejectsNonContiguousThingAppend()
	{
		var source = await CreatePair(spriteCount: 1);
		var target = await CreatePair(spriteCount: 1);
		PutItem(source, 100, 1, pickupable: true);
		PutItem(source, 101, 1, pickupable: true);

		var batch = AssetReplacementService.Prepare(new AssetReplacementRequest(
			source, target, AssetReplacementMode.Things, ThingKind.Item, 101, 101,
			AddMissingTargetIds: true));

		Assert.False(batch.CanApply);
		Assert.Single(batch.Skipped);
		Assert.Contains("#100", batch.Skipped[0].Reason);
		Assert.Null(target.ThingsPanel.Catalog!.TryGetItem(101));
	}

	private static async Task<LinkedArchivePair> CreatePair(int spriteCount)
	{
		var sprites = new FloatingSpriteLoaderViewModel(new SpriteRenderer());
		await sprites.CreateNewArchiveAsync("assets", 1098);
		for (var i = 0; i < spriteCount; i++)
			sprites.Loader.AddNewSprite();
		sprites.NotifyExternalArchiveMutation();

		var things = new FloatingThingsLoaderViewModel();
		await things.CreateNewArchiveAsync("things", 1098, true, true, true);
		things.LinkedSpritePanel = sprites;
		return new LinkedArchivePair(sprites, things);
	}

	private static void PutItem(LinkedArchivePair pair, uint id, uint spriteId, bool pickupable)
	{
		PutItemWithSprites(pair, id, pickupable, spriteId);
	}

	private static void PutItemWithSprites(LinkedArchivePair pair, uint id, bool pickupable, params uint[] spriteIds)
	{
		var thing = new ThingType { Id = id, Kind = ThingKind.Item, Pickupable = pickupable };
		thing.FrameGroups.Add(new ThingFrameGroup
		{
			Width = (uint)spriteIds.Length,
			Height = 1,
			Layers = 1,
			PatternX = 1,
			PatternY = 1,
			PatternZ = 1,
			Frames = 1,
			SpriteIds = spriteIds,
		});
		pair.ThingsPanel.Catalog!.PutItem(thing);
	}

	private static void PutEffect(LinkedArchivePair pair, uint id, uint spriteId, bool hasLight)
	{
		PutEffectWithSprites(pair, id, hasLight, 1, spriteId);
	}

	private static void PutEffectWithSprites(LinkedArchivePair pair, uint id, bool hasLight, uint frames, params uint[] spriteIds)
	{
		var thing = new ThingType { Id = id, Kind = ThingKind.Effect, HasLight = hasLight, LightLevel = 4 };
		thing.FrameGroups.Add(new ThingFrameGroup
		{
			Width = 1,
			Height = 1,
			Layers = 1,
			PatternX = 1,
			PatternY = 1,
			PatternZ = 1,
			Frames = frames,
			SpriteIds = spriteIds,
		});
		pair.ThingsPanel.Catalog!.PutEffect(thing);
	}

	private static byte[] SolidPixels(byte value)
	{
		var pixels = new byte[32 * 32 * 4];
		for (var i = 0; i < pixels.Length; i += 4)
		{
			pixels[i] = value;
			pixels[i + 1] = value;
			pixels[i + 2] = value;
			pixels[i + 3] = 255;
		}
		return pixels;
	}
}
