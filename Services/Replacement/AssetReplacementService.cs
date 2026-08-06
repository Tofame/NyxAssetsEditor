using System;
using System.Collections.Generic;
using System.Linq;
using NyxAssets.Sprites;
using NyxAssets.Things;
using NyxAssets.Things.Exchange;
using NyxAssetsEditor.Services.Exchange;
using NyxAssetsEditor.Services.Archive;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;
using NyxAssetsEditor.ViewModels.Common;
using NyxAssetsEditor.ViewModels.Pages;

namespace NyxAssetsEditor.Services.Replacement;

public enum AssetReplacementMode
{
	Things,
	Sprites,
}

public sealed record AssetReplacementRequest(
	LinkedArchivePair SourcePair,
	LinkedArchivePair TargetPair,
	AssetReplacementMode Mode,
	ThingKind? ThingKind,
	uint FromId,
	uint ToId,
	bool AddMissingTargetIds);

public sealed record ReplacementSkippedId(uint Id, string Reason);

public sealed class AppliedReplacementTransaction
{
	private readonly LinkedArchivePair _targetPair;
	private readonly SpriteUndoAction? _spriteAction;
	private readonly ThingUndoAction? _thingAction;

	internal AppliedReplacementTransaction(
		LinkedArchivePair targetPair,
		SpriteUndoAction? spriteAction,
		ThingUndoAction? thingAction)
	{
		_targetPair = targetPair;
		_spriteAction = spriteAction;
		_thingAction = thingAction;
	}

	public bool CanUndo =>
		(_spriteAction == null || _targetPair.SpritePanel.CanUndoReplacement(_spriteAction))
		&& (_thingAction == null || _targetPair.ThingsPanel.CanUndoReplacement(_thingAction));

	public bool CanRedo =>
		(_spriteAction == null || _targetPair.SpritePanel.CanRedoReplacement(_spriteAction))
		&& (_thingAction == null || _targetPair.ThingsPanel.CanRedoReplacement(_thingAction));

	public bool TryUndo(out string? error)
	{
		if (!CanUndo)
		{
			error = "This replacement is no longer the latest change in its target viewer.";
			return false;
		}

		var thingUndone = false;
		try
		{
			if (_thingAction != null)
			{
				thingUndone = _targetPair.ThingsPanel.TryUndoReplacement(_thingAction);
				if (!thingUndone)
					throw new InvalidOperationException("The Thing replacement could not be undone.");
			}
			if (_spriteAction != null && !_targetPair.SpritePanel.TryUndoReplacement(_spriteAction))
				throw new InvalidOperationException("The Sprite replacement could not be undone.");
			error = null;
			return true;
		}
		catch (Exception ex)
		{
			if (thingUndone && _thingAction != null)
				_targetPair.ThingsPanel.TryRedoReplacement(_thingAction);
			error = $"Undo could not be completed: {ex.Message}";
			return false;
		}
	}

	public bool TryRedo(out string? error)
	{
		if (!CanRedo)
		{
			error = "This replacement is no longer the latest undone change in its target viewer.";
			return false;
		}

		var spriteRedone = false;
		try
		{
			if (_spriteAction != null)
			{
				spriteRedone = _targetPair.SpritePanel.TryRedoReplacement(_spriteAction);
				if (!spriteRedone)
					throw new InvalidOperationException("The Sprite replacement could not be redone.");
			}
			if (_thingAction != null && !_targetPair.ThingsPanel.TryRedoReplacement(_thingAction))
				throw new InvalidOperationException("The Thing replacement could not be redone.");
			error = null;
			return true;
		}
		catch (Exception ex)
		{
			if (spriteRedone && _spriteAction != null)
				_targetPair.SpritePanel.TryUndoReplacement(_spriteAction);
			error = $"Redo could not be completed: {ex.Message}";
			return false;
		}
	}
}

public sealed class PreparedReplacementBatch
{
	internal PreparedReplacementBatch(
		AssetReplacementRequest request,
		IReadOnlyList<ThingType> things,
		IReadOnlyDictionary<uint, byte[]> spritePixels,
		IReadOnlyList<ReplacementSkippedId> skipped,
		string? error,
		IReadOnlyList<string>? warnings = null)
	{
		Request = request;
		Things = things;
		SpritePixels = spritePixels;
		Skipped = skipped;
		Error = error;
		Warnings = warnings ?? Array.Empty<string>();
	}

	public AssetReplacementRequest Request { get; }
	public IReadOnlyList<ThingType> Things { get; }
	public IReadOnlyDictionary<uint, byte[]> SpritePixels { get; }
	public IReadOnlyList<ReplacementSkippedId> Skipped { get; }
	public IReadOnlyList<string> Warnings { get; }
	public string? Error { get; }
	public bool CanApply => string.IsNullOrEmpty(Error)
		&& (Things.Count > 0 || SpritePixels.Count > 0);
}

public sealed record AssetReplacementResult(
	bool Succeeded,
	int ReplacedThings,
	int ReplacedSprites,
	IReadOnlyList<ReplacementSkippedId> Skipped,
	string Message,
	AppliedReplacementTransaction? Transaction = null,
	IReadOnlyList<string>? Warnings = null);

public static class AssetReplacementService
{
	public static PreparedReplacementBatch Prepare(AssetReplacementRequest request)
	{
		if (request.SourcePair == null || request.TargetPair == null)
			return Invalid(request, "Select both a source and target archive pair.");
		if (ReferenceEquals(request.SourcePair.SpritePanel, request.TargetPair.SpritePanel)
			&& ReferenceEquals(request.SourcePair.ThingsPanel, request.TargetPair.ThingsPanel))
			return Invalid(request, "Source and target archive pairs must be different.");
		if (request.FromId == 0 || request.ToId == 0)
			return Invalid(request, "IDs must be greater than zero.");
		if (request.FromId > request.ToId)
			return Invalid(request, "From ID must be less than or equal to To ID.");
		if (request.Mode == AssetReplacementMode.Things && request.ThingKind == null)
			return Invalid(request, "Select a Thing category.");
		if (!request.SourcePair.SpritePanel.IsArchiveLoaded || !request.TargetPair.SpritePanel.IsArchiveLoaded)
			return Invalid(request, "Both Sprite archives must be loaded.");

		try
		{
			return request.Mode == AssetReplacementMode.Sprites
				? PrepareSprites(request)
				: PrepareThings(request, request.ThingKind!.Value);
		}
		catch (Exception ex)
		{
			return Invalid(request, $"The replacement range could not be prepared: {ex.Message}");
		}
	}

	public static PreparedReplacementBatch PrepareSingleThing(
		ThingDocument document,
		LinkedArchivePair targetPair,
		ThingKind targetKind,
		uint targetId)
	{
		var request = new AssetReplacementRequest(
			targetPair,
			targetPair,
			AssetReplacementMode.Things,
			targetKind,
			targetId,
			targetId,
			AddMissingTargetIds: false);

		if (targetPair.ThingsPanel.Catalog == null || !targetPair.SpritePanel.IsArchiveLoaded)
			return Invalid(request, "The target archive pair is not fully loaded.");
		if (document.Thing.Kind != targetKind)
			return Invalid(request, $"The imported {document.Thing.Kind.ToString().ToLowerInvariant()} cannot replace a {targetKind.ToString().ToLowerInvariant()}.");
		var targetThing = TryGetThing(targetPair.ThingsPanel.Catalog, targetKind, targetId);
		if (targetThing == null)
			return Invalid(request, $"Target {targetKind.ToString().ToLowerInvariant()} #{targetId} does not exist.");
		if (!TryCreateTargetCompatibleThing(document.Thing, targetPair.ThingsPanel, out var compatibleThing, out var warnings, out var compatibilityError))
			return Invalid(request, compatibilityError);
		var compatibleDocument = new ThingDocument
		{
			Thing = compatibleThing,
			ClientVersion = document.ClientVersion,
			ObdVersion = document.ObdVersion,
			SpritesRgba = document.SpritesRgba,
		};

		if (!TryPrepareMappedDocumentThing(compatibleDocument, targetThing, targetPair.SpritePanel, targetId, out var replacementThing, out var pixels, out var reason))
			return Invalid(request, reason);

		return new PreparedReplacementBatch(
			request,
			new[] { replacementThing },
			pixels,
			Array.Empty<ReplacementSkippedId>(),
			null,
			warnings);
	}

	private static bool TryPrepareMappedDocumentThing(
		ThingDocument document,
		ThingType targetThing,
		FloatingSpriteLoaderViewModel targetSprites,
		uint targetId,
		out ThingType replacementThing,
		out Dictionary<uint, byte[]> pixels,
		out string reason)
	{
		var sourceSlots = EnumerateSpriteSlots(document.Thing).ToList();
		var targetSlots = EnumerateSpriteSlots(targetThing).ToList();
		var mappedTargetIds = new uint[sourceSlots.Count];
		var targetSources = new Dictionary<uint, uint>();
		pixels = new Dictionary<uint, byte[]>();
		replacementThing = null!;
		reason = string.Empty;

		for (var index = 0; index < sourceSlots.Count; index++)
		{
			var sourceSpriteId = sourceSlots[index];
			if (sourceSpriteId == 0)
			{
				mappedTargetIds[index] = 0;
				continue;
			}
			if (document.SpritesRgba == null || !document.SpritesRgba.TryGetValue(sourceSpriteId, out var rgba))
			{
				reason = $"The imported file does not contain referenced sprite #{sourceSpriteId}.";
				return false;
			}
			if (index >= targetSlots.Count)
			{
				reason = $"The imported Thing uses {sourceSlots.Count} sprite slot(s), but the target Thing only provides {targetSlots.Count}.";
				return false;
			}
			var targetSpriteId = targetSlots[index];
			if (!IsValidTargetSprite(targetSprites, targetSpriteId))
			{
				reason = $"Corresponding target sprite #{targetSpriteId} does not exist.";
				return false;
			}
			if (rgba.Length != SpritePixelCodec.RgbaBufferLength)
			{
				reason = $"Sprite #{sourceSpriteId} does not contain a complete 32x32 RGBA image.";
				return false;
			}
			if (targetSources.TryGetValue(targetSpriteId, out var earlierSourceId) && earlierSourceId != sourceSpriteId)
			{
				reason = $"Target sprite #{targetSpriteId} is reused by slots that require different imported sprites.";
				return false;
			}
			targetSources[targetSpriteId] = sourceSpriteId;
			mappedTargetIds[index] = targetSpriteId;
			pixels[targetSpriteId] = rgba.ToArray();
		}

		replacementThing = ThingCloner.Clone(document.Thing, targetId);
		var mappedIndex = 0;
		foreach (var group in replacementThing.FrameGroups)
		{
			var slotCount = group.SpriteIds.Length;
			group.SpriteIds = mappedTargetIds.Skip(mappedIndex).Take(slotCount).ToArray();
			mappedIndex += slotCount;
		}
		return true;
	}

	public static AssetReplacementResult Apply(PreparedReplacementBatch batch)
	{
		if (!batch.CanApply)
			return new AssetReplacementResult(false, 0, 0, batch.Skipped, batch.Error ?? "There is nothing to replace.", Warnings: batch.Warnings);

		var targetSprites = batch.Request.TargetPair.SpritePanel;
		var targetThings = batch.Request.TargetPair.ThingsPanel;
		SpriteUndoAction? spriteAction = null;
		ThingUndoAction? thingAction = null;

		try
		{
			if (batch.SpritePixels.Count > 0)
				spriteAction = targetSprites.ApplyReplacementPixels(batch.SpritePixels, batch.Request.AddMissingTargetIds);
			if (batch.Things.Count > 0)
				thingAction = targetThings.ApplyReplacementThings(batch.Things, batch.Request.AddMissingTargetIds);
		}
		catch (Exception ex)
		{
			if (thingAction != null)
				targetThings.RollbackReplacementThings(thingAction);
			if (spriteAction != null)
				targetSprites.RollbackReplacementPixels(spriteAction);
			return new AssetReplacementResult(false, 0, 0, batch.Skipped, $"Replacement failed and was rolled back: {ex.Message}", Warnings: batch.Warnings);
		}

		var addedThings = thingAction == null ? 0 : CountAddedThings(thingAction);
		var addedSprites = spriteAction == null ? 0 : checked((int)(spriteAction.SpriteCountAfter - spriteAction.SpriteCountBefore));
		var message = $"Replaced {batch.Things.Count} thing(s) and {batch.SpritePixels.Count} sprite(s).";
		if (addedThings > 0 || addedSprites > 0)
			message += $" Added {addedThings} missing target thing ID(s) and {addedSprites} missing target sprite ID(s).";
		if (batch.Skipped.Count > 0)
			message += $" Skipped {batch.Skipped.Count} ID(s).";
		return new AssetReplacementResult(
			true,
			batch.Things.Count,
			batch.SpritePixels.Count,
			batch.Skipped,
			message,
			new AppliedReplacementTransaction(batch.Request.TargetPair, spriteAction, thingAction),
			batch.Warnings);
	}

	private static PreparedReplacementBatch PrepareSprites(AssetReplacementRequest request)
	{
		var pixels = new Dictionary<uint, byte[]>();
		var skipped = new List<ReplacementSkippedId>();

		ForEachId(request.FromId, request.ToId, id =>
		{
			if (!IsValidSourceSprite(request.SourcePair.SpritePanel, id))
			{
				skipped.Add(new(id, "Source sprite does not exist."));
				return;
			}
			if (!IsValidTargetSprite(request.TargetPair.SpritePanel, id) && !request.AddMissingTargetIds)
			{
				skipped.Add(new(id, "Target sprite does not exist."));
				return;
			}
			if (request.AddMissingTargetIds && !TargetUsesExtendedSpriteIds(request.TargetPair.SpritePanel) && id > ushort.MaxValue)
			{
				var targetSprites = request.TargetPair.SpritePanel;
				skipped.Add(new(id,
					$"Raw Sprite replacement preserves IDs, but the target archive was detected as legacy " +
					$"(16-bit sprite IDs, signature 0x{targetSprites.Loader.SprSignature:X8}). " +
					$"Source sprite #{id} would require target sprite #{id}; the highest supported target ID is #65535. " +
					$"The target currently reports {targetSprites.Loader.SpriteCount} sprite(s)."));
				return;
			}

			try
			{
				var rgba = request.SourcePair.SpritePanel.Loader.LoadSpritePixels(id);
				if (rgba.Length != SpritePixelCodec.RgbaBufferLength)
				{
					skipped.Add(new(id, "Source sprite data is incomplete."));
					return;
				}
				pixels[id] = rgba.ToArray();
			}
			catch (Exception ex)
			{
				skipped.Add(new(id, $"Source sprite could not be read: {ex.Message}"));
			}
		});

		return FinishPreparation(request, Array.Empty<ThingType>(), pixels, skipped);
	}

	private static PreparedReplacementBatch PrepareThings(AssetReplacementRequest request, ThingKind kind)
	{
		var things = new List<ThingType>();
		var pixels = new Dictionary<uint, byte[]>();
		var skipped = new List<ReplacementSkippedId>();
		var warnings = new List<string>();
		var sourceCatalog = request.SourcePair.ThingsPanel.Catalog;
		var targetCatalog = request.TargetPair.ThingsPanel.Catalog;
		if (sourceCatalog == null || targetCatalog == null)
			return Invalid(request, "Both Things archives must be loaded.");

		var nextTargetId = ThingExchangeHelper.GetNextAppendId(targetCatalog, kind);
		var nextTargetSpriteId = request.TargetPair.SpritePanel.Loader.SpriteCount + 1;
		var appendedTargetIdsBySourceSpriteId = new Dictionary<uint, uint>();
		ForEachId(request.FromId, request.ToId, id =>
		{
			var rawSourceThing = TryGetThing(sourceCatalog, kind, id);
			if (rawSourceThing == null)
			{
				skipped.Add(new(id, "Source Thing does not exist."));
				return;
			}
			if (!TryCreateTargetCompatibleThing(
				rawSourceThing,
				request.TargetPair.ThingsPanel,
				out var sourceThing,
				out var compatibilityWarnings,
				out var compatibilityError))
			{
				skipped.Add(new(id, compatibilityError));
				return;
			}
			var targetThing = TryGetThing(targetCatalog, kind, id);
			var addsTargetThing = targetThing == null;
			if (addsTargetThing && !request.AddMissingTargetIds)
			{
				skipped.Add(new(id, "Target Thing does not exist."));
				return;
			}
			if (addsTargetThing && id != nextTargetId)
			{
				skipped.Add(new(id, $"Target Thing cannot be added until missing target ID #{nextTargetId} is included and replaceable."));
				return;
			}
			ThingType replacementThing;
			Dictionary<uint, byte[]> thingPixels;
			Dictionary<uint, uint> thingTargetSources;
			var proposedNextTargetSpriteId = nextTargetSpriteId;
			var proposedAppendedTargetIds = new Dictionary<uint, uint>(appendedTargetIdsBySourceSpriteId);
			if (targetThing != null)
			{
				if (!TryPrepareMappedThing(
					sourceThing,
					targetThing,
					request.SourcePair.SpritePanel,
					request.TargetPair.SpritePanel,
					request.AddMissingTargetIds,
					nextTargetSpriteId,
					proposedAppendedTargetIds,
					out proposedNextTargetSpriteId,
					out replacementThing,
					out thingPixels,
					out thingTargetSources,
					out var reason))
				{
					skipped.Add(new(id, reason));
					return;
				}
			}
			else
			{
				if (!TryPrepareNewThing(
					sourceThing,
					request.SourcePair.SpritePanel,
					nextTargetSpriteId,
					proposedAppendedTargetIds,
					out proposedNextTargetSpriteId,
					out replacementThing,
					out thingPixels,
					out thingTargetSources,
					out var reason))
				{
					skipped.Add(new(id, reason));
					return;
				}
			}

			foreach (var pair in thingPixels.ToList())
			{
				if (pixels.TryGetValue(pair.Key, out var existingPixels) && !existingPixels.SequenceEqual(pair.Value))
				{
					if (!request.AddMissingTargetIds)
					{
						skipped.Add(new(id, $"Target sprite #{pair.Key} is shared by selected Things that require different source pixels. Enable Create missing target IDs to separate them."));
						return;
					}

					var sourceSpriteId = thingTargetSources[pair.Key];
					var newTargetSpriteId = GetOrAllocateTargetSpriteId(
						sourceSpriteId,
						proposedAppendedTargetIds,
						ref proposedNextTargetSpriteId);
					foreach (var group in replacementThing.FrameGroups)
						for (var slot = 0; slot < group.SpriteIds.Length; slot++)
							if (group.SpriteIds[slot] == pair.Key)
								group.SpriteIds[slot] = newTargetSpriteId;
					thingPixels.Remove(pair.Key);
					thingPixels[newTargetSpriteId] = pair.Value;
					thingTargetSources.Remove(pair.Key);
					thingTargetSources[newTargetSpriteId] = sourceSpriteId;
				}
			}
			// Gate on sprite IDs this Thing would write, not SpriteCount (count can exceed the
			// legacy max while the replace only overwrites existing low IDs).
			if (!TargetUsesExtendedSpriteIds(request.TargetPair.SpritePanel))
			{
				var highestRequiredSpriteId = 0u;
				foreach (var spriteId in thingPixels.Keys)
					if (spriteId > highestRequiredSpriteId)
						highestRequiredSpriteId = spriteId;
				if (highestRequiredSpriteId > ushort.MaxValue)
				{
					var targetSprites = request.TargetPair.SpritePanel;
					var appendedSpriteCount = proposedNextTargetSpriteId - nextTargetSpriteId;
					var capacityDetails = appendedSpriteCount > 0 && targetSprites.Loader.SpriteCount <= ushort.MaxValue
						? $"It currently contains {targetSprites.Loader.SpriteCount} sprite(s), leaving {ushort.MaxValue - targetSprites.Loader.SpriteCount} ID(s) available. " +
							$"Earlier Things in this batch reserved {nextTargetSpriteId - (targetSprites.Loader.SpriteCount + 1)} additional ID(s), " +
							$"and this Thing needs {appendedSpriteCount} more, which would require IDs through #{highestRequiredSpriteId}."
						: $"This replace would write sprite #{highestRequiredSpriteId}. " +
							$"The archive reports {targetSprites.Loader.SpriteCount} sprite(s); check that the correct client version and Sprite file were loaded.";
					skipped.Add(new(id,
						$"The target Sprite archive was detected as legacy (16-bit sprite IDs, signature 0x{targetSprites.Loader.SprSignature:X8}), " +
						$"so its highest supported sprite ID is #65535. {capacityDetails}"));
					return;
				}
			}

			foreach (var warning in compatibilityWarnings)
				warnings.Add($"#{id}: {warning}");
			if (targetThing != null)
				AddFrameConversionWarning(
					sourceThing,
					targetThing,
					id,
					proposedNextTargetSpriteId - nextTargetSpriteId,
					warnings);

			things.Add(replacementThing);
			if (addsTargetThing)
				nextTargetId++;
			nextTargetSpriteId = proposedNextTargetSpriteId;
			appendedTargetIdsBySourceSpriteId = proposedAppendedTargetIds;
			foreach (var pair in thingPixels)
				pixels[pair.Key] = pair.Value;
		});

		return FinishPreparation(request, things, pixels, skipped, warnings);
	}

	private static void AddFrameConversionWarning(
		ThingType sourceThing,
		ThingType targetThing,
		uint id,
		uint appendedSpriteCount,
		ICollection<string> warnings)
	{
		var sourceFrames = sourceThing.FrameGroups.Aggregate(0UL, (total, group) => total + group.Frames);
		var targetFrames = targetThing.FrameGroups.Aggregate(0UL, (total, group) => total + group.Frames);
		if (sourceFrames > targetFrames)
		{
			var spriteAdjustment = appendedSpriteCount > 0
				? $" {appendedSpriteCount} additional target sprite ID(s) are appended and remapped."
				: " No additional target sprite IDs are required.";
			warnings.Add($"#{id}: source has {sourceFrames} frame(s), target has {targetFrames}. The source frame layout replaces the target layout.{spriteAdjustment}");
		}
		else if (sourceFrames < targetFrames)
		{
			warnings.Add($"#{id}: source has {sourceFrames} frame(s), target has {targetFrames}. The copied definition will use fewer frames; surplus target sprites are left untouched and may become unreferenced.");
		}
		else if (sourceThing.FrameGroups.Count != targetThing.FrameGroups.Count)
		{
			warnings.Add($"#{id}: source and target both have {sourceFrames} frame(s), but their frame-group counts differ ({sourceThing.FrameGroups.Count} vs {targetThing.FrameGroups.Count}). The source grouping will replace the target grouping.");
		}
	}

	private static bool TryCreateTargetCompatibleThing(
		ThingType sourceThing,
		FloatingThingsLoaderViewModel targetPanel,
		out ThingType compatibleThing,
		out IReadOnlyList<string> warnings,
		out string error)
	{
		compatibleThing = ThingCloner.Clone(sourceThing, sourceThing.Id);
		var notes = new List<string>();
		warnings = notes;
		error = string.Empty;

		if (compatibleThing.Kind == ThingKind.Outfit && !TargetUsesFrameGroups(targetPanel))
		{
			if (!TryCollapseOutfitForLegacyTarget(compatibleThing, out var collapseError))
			{
				error = collapseError;
				return false;
			}
			if (sourceThing.FrameGroups.Count > 1)
				notes.Add($"collapsed {sourceThing.FrameGroups.Count} outfit frame groups into one legacy group.");
			if (sourceThing.FrameGroups.Any(group => group.PatternZ > 1))
				notes.Add("removed mounted PatternZ variants because the legacy target only supports the base outfit layer.");
		}

		if (!TargetUsesImprovedAnimations(targetPanel))
		{
			var removedTimings = false;
			foreach (var group in compatibleThing.FrameGroups)
			{
				removedTimings |= group.FrameTimings is { Length: > 0 };
				group.FrameTimings = null;
				group.AnimationMode = 0;
				group.LoopCount = 0;
				group.StartFrame = 0;
				group.IsAnimation = group.Frames > 1;
			}
			if (removedTimings)
				notes.Add("converted improved per-frame timing metadata to the target's legacy animation format.");
		}
		else
		{
			var duration = SettingsViewModel.GetDefaultAnimationDurationMs(compatibleThing.Kind);
			if (duration == 0) duration = 150;
			foreach (var group in compatibleThing.FrameGroups)
			{
				if (group.Frames <= 1) continue;
				group.IsAnimation = true;
				if (group.FrameTimings == null || group.FrameTimings.Length != group.Frames)
				{
					group.FrameTimings = Enumerable.Range(0, checked((int)group.Frames))
						.Select(_ => new AnimationFrameTiming(duration, duration))
						.ToArray();
				}
			}
		}

		return true;
	}

	private static bool TryCollapseOutfitForLegacyTarget(ThingType thing, out string error)
	{
		error = string.Empty;
		if (thing.FrameGroups.Count == 0)
			return true;

		var groups = thing.FrameGroups.OrderBy(group => group.GroupTypeId).ToList();
		var first = groups[0];
		if (groups.Any(group => group.Width != first.Width || group.Height != first.Height
			|| group.Layers != first.Layers || group.PatternX != first.PatternX
			|| group.PatternY != first.PatternY))
		{
			error = "The source outfit frame groups use different layouts and cannot be represented by the legacy target.";
			return false;
		}

		var totalFrames = checked((uint)groups.Sum(group => (long)group.Frames));
		var merged = new ThingFrameGroup
		{
			GroupTypeId = 0,
			Width = first.Width,
			Height = first.Height,
			ExactSize = first.ExactSize,
			Layers = first.Layers,
			PatternX = first.PatternX,
			PatternY = first.PatternY,
			PatternZ = 1,
			Frames = totalFrames,
			IsAnimation = totalFrames > 1,
		};
		merged.SpriteIds = new uint[checked((int)merged.GetTotalSpriteSlots())];

		var targetFrame = 0u;
		foreach (var group in groups)
		{
			if (group.PatternZ == 0)
			{
				error = "A source outfit frame group has no PatternZ variants.";
				return false;
			}
			for (var frame = 0u; frame < group.Frames; frame++)
			for (var patternY = 0u; patternY < group.PatternY; patternY++)
			for (var patternX = 0u; patternX < group.PatternX; patternX++)
			for (var layer = 0u; layer < group.Layers; layer++)
			for (var innerWidth = 0u; innerWidth < group.Width; innerWidth++)
			for (var innerHeight = 0u; innerHeight < group.Height; innerHeight++)
			{
				var sourceIndex = group.GetSpriteIndex(innerWidth, innerHeight, layer, patternX, patternY, 0, frame);
				var targetIndex = merged.GetSpriteIndex(innerWidth, innerHeight, layer, patternX, patternY, 0, targetFrame + frame);
				merged.SpriteIds[targetIndex] = group.SpriteIds[sourceIndex];
			}
			targetFrame += group.Frames;
		}

		thing.FrameGroups.Clear();
		thing.FrameGroups.Add(merged);
		return true;
	}

	private static bool TargetUsesImprovedAnimations(FloatingThingsLoaderViewModel panel)
	{
		var version = ResolveCatalogVersion(panel);
		return version.HasValue
			? DatThingFormatRules.UsesImprovedAnimationsByDefault(version.Value)
			: panel.UseFrameAnimations;
	}

	private static bool TargetUsesFrameGroups(FloatingThingsLoaderViewModel panel)
	{
		var version = ResolveCatalogVersion(panel);
		return version.HasValue
			? DatThingFormatRules.UsesOutfitFrameGroupsByDefault(version.Value)
			: panel.UseFrameGroups;
	}

	private static ClientDataVersion? ResolveCatalogVersion(FloatingThingsLoaderViewModel panel)
	{
		var signature = panel.Catalog?.DatSignature ?? 0;
		var entry = ClientVersion.AvailableVersions.Find(version => version.DatSignature == signature);
		return entry == null ? null : new ClientDataVersion { Value = entry.Version };
	}

	private static bool TargetUsesExtendedSpriteIds(FloatingSpriteLoaderViewModel panel) =>
		// Trust how the SPR was opened. Signature→version defaults are only a load hint; a sheet
		// can keep an old signature while still using the extended (32-bit) SPR header.
		panel.UseExtendedSpriteIds;

	private static bool TryPrepareMappedThing(
		ThingType sourceThing,
		ThingType targetThing,
		FloatingSpriteLoaderViewModel sourceSprites,
		FloatingSpriteLoaderViewModel targetSprites,
		bool createMissingTargetIds,
		uint firstNewTargetSpriteId,
		Dictionary<uint, uint> appendedTargetIdsBySourceSpriteId,
		out uint nextTargetSpriteId,
		out ThingType replacementThing,
		out Dictionary<uint, byte[]> pixels,
		out Dictionary<uint, uint> targetSources,
		out string reason)
	{
		var sourceSlots = EnumerateSpriteSlots(sourceThing).ToList();
		var targetSlots = EnumerateSpriteSlots(targetThing).ToList();
		var mappedTargetIds = new uint[sourceSlots.Count];
		targetSources = new Dictionary<uint, uint>();
		var sourcePixels = new Dictionary<uint, byte[]>();
		pixels = new Dictionary<uint, byte[]>();
		nextTargetSpriteId = firstNewTargetSpriteId;
		replacementThing = null!;
		reason = string.Empty;

		for (var index = 0; index < sourceSlots.Count; index++)
		{
			var sourceSpriteId = sourceSlots[index];
			if (sourceSpriteId == 0)
			{
				mappedTargetIds[index] = 0;
				continue;
			}
			if (!IsValidSourceSprite(sourceSprites, sourceSpriteId))
			{
				reason = $"Referenced source sprite #{sourceSpriteId} does not exist.";
				return false;
			}

			uint targetSpriteId;
			if (index < targetSlots.Count && IsValidTargetSprite(targetSprites, targetSlots[index]))
			{
				targetSpriteId = targetSlots[index];
			}
			else if (createMissingTargetIds)
			{
				targetSpriteId = GetOrAllocateTargetSpriteId(
					sourceSpriteId,
					appendedTargetIdsBySourceSpriteId,
					ref nextTargetSpriteId);
			}
			else
			{
				reason = index < targetSlots.Count
					? $"Corresponding target sprite #{targetSlots[index]} does not exist."
					: $"Source Thing uses {sourceSlots.Count} sprite slot(s), but the target Thing only provides {targetSlots.Count}. Enable Create missing target IDs to add the extra sprites.";
				return false;
			}

			if (targetSources.TryGetValue(targetSpriteId, out var earlierSourceId) && earlierSourceId != sourceSpriteId)
			{
				if (createMissingTargetIds)
					targetSpriteId = GetOrAllocateTargetSpriteId(
						sourceSpriteId,
						appendedTargetIdsBySourceSpriteId,
						ref nextTargetSpriteId);
				else
				{
					reason = $"Target sprite #{targetSpriteId} is reused by multiple slots that require different source sprites. Enable Create missing target IDs to separate them.";
					return false;
				}
			}

			if (!sourcePixels.TryGetValue(sourceSpriteId, out var rgba))
			{
				try
				{
					rgba = sourceSprites.Loader.LoadSpritePixels(sourceSpriteId);
					if (rgba.Length != SpritePixelCodec.RgbaBufferLength)
					{
						reason = $"Referenced source sprite #{sourceSpriteId} is incomplete.";
						return false;
					}
					rgba = rgba.ToArray();
					sourcePixels[sourceSpriteId] = rgba;
				}
				catch (Exception ex)
				{
					reason = $"Referenced source sprite #{sourceSpriteId} could not be read: {ex.Message}";
					return false;
				}
			}

			targetSources[targetSpriteId] = sourceSpriteId;
			mappedTargetIds[index] = targetSpriteId;
			pixels[targetSpriteId] = rgba;
		}

		replacementThing = ThingCloner.Clone(sourceThing, sourceThing.Id);
		var mappedIndex = 0;
		foreach (var group in replacementThing.FrameGroups)
		{
			var slotCount = group.SpriteIds.Length;
			group.SpriteIds = mappedTargetIds.Skip(mappedIndex).Take(slotCount).ToArray();
			mappedIndex += slotCount;
		}
		return true;
	}

	private static bool TryPrepareNewThing(
		ThingType sourceThing,
		FloatingSpriteLoaderViewModel sourceSprites,
		uint firstNewTargetSpriteId,
		Dictionary<uint, uint> appendedTargetIdsBySourceSpriteId,
		out uint nextTargetSpriteId,
		out ThingType replacementThing,
		out Dictionary<uint, byte[]> pixels,
		out Dictionary<uint, uint> targetSources,
		out string reason)
	{
		var sourceSlots = EnumerateSpriteSlots(sourceThing).ToList();
		var mappedTargetIds = new uint[sourceSlots.Count];
		var sourcePixels = new Dictionary<uint, byte[]>();
		pixels = new Dictionary<uint, byte[]>();
		targetSources = new Dictionary<uint, uint>();
		nextTargetSpriteId = firstNewTargetSpriteId;
		replacementThing = null!;
		reason = string.Empty;

		for (var index = 0; index < sourceSlots.Count; index++)
		{
			var sourceSpriteId = sourceSlots[index];
			if (sourceSpriteId == 0)
			{
				mappedTargetIds[index] = 0;
				continue;
			}
			if (!IsValidSourceSprite(sourceSprites, sourceSpriteId))
			{
				reason = $"Referenced source sprite #{sourceSpriteId} does not exist.";
				return false;
			}

			var targetSpriteId = GetOrAllocateTargetSpriteId(
				sourceSpriteId,
				appendedTargetIdsBySourceSpriteId,
				ref nextTargetSpriteId);
			if (!sourcePixels.TryGetValue(sourceSpriteId, out var rgba))
			{
				try
				{
					rgba = sourceSprites.Loader.LoadSpritePixels(sourceSpriteId);
					if (rgba.Length != SpritePixelCodec.RgbaBufferLength)
					{
						reason = $"Referenced source sprite #{sourceSpriteId} is incomplete.";
						return false;
					}
					rgba = rgba.ToArray();
					sourcePixels[sourceSpriteId] = rgba;
				}
				catch (Exception ex)
				{
					reason = $"Referenced source sprite #{sourceSpriteId} could not be read: {ex.Message}";
					return false;
				}
			}

			mappedTargetIds[index] = targetSpriteId;
			targetSources[targetSpriteId] = sourceSpriteId;
			pixels[targetSpriteId] = rgba;
		}

		replacementThing = ThingCloner.Clone(sourceThing, sourceThing.Id);
		var mappedIndex = 0;
		foreach (var group in replacementThing.FrameGroups)
		{
			var slotCount = group.SpriteIds.Length;
			group.SpriteIds = mappedTargetIds.Skip(mappedIndex).Take(slotCount).ToArray();
			mappedIndex += slotCount;
		}
		return true;
	}

	private static uint GetOrAllocateTargetSpriteId(
		uint sourceSpriteId,
		Dictionary<uint, uint> appendedTargetIdsBySourceSpriteId,
		ref uint nextTargetSpriteId)
	{
		if (appendedTargetIdsBySourceSpriteId.TryGetValue(sourceSpriteId, out var targetSpriteId))
			return targetSpriteId;
		if (nextTargetSpriteId == uint.MaxValue)
			throw new InvalidOperationException("The target Sprite archive cannot be extended further.");

		targetSpriteId = nextTargetSpriteId++;
		appendedTargetIdsBySourceSpriteId[sourceSpriteId] = targetSpriteId;
		return targetSpriteId;
	}

	private static PreparedReplacementBatch FinishPreparation(
		AssetReplacementRequest request,
		IReadOnlyList<ThingType> things,
		IReadOnlyDictionary<uint, byte[]> pixels,
		IReadOnlyList<ReplacementSkippedId> skipped,
		IReadOnlyList<string>? warnings = null)
	{
		if (things.Count == 0 && pixels.Count == 0)
			return new PreparedReplacementBatch(request, things, pixels, skipped, "No IDs could be replaced. Review the skipped-ID details below.", warnings);
		return new PreparedReplacementBatch(request, things, pixels, skipped, null, warnings);
	}

	private static PreparedReplacementBatch Invalid(AssetReplacementRequest request, string error) =>
		new(request, Array.Empty<ThingType>(), new Dictionary<uint, byte[]>(), Array.Empty<ReplacementSkippedId>(), error);

	private static IEnumerable<uint> EnumerateSpriteSlots(ThingType thing) =>
		thing.FrameGroups.SelectMany(group => group.SpriteIds);

	private static bool IsValidSourceSprite(FloatingSpriteLoaderViewModel panel, uint id) =>
		panel.IsArchiveLoaded && id > 0 && id <= panel.Loader.SpriteCount;

	private static bool IsValidTargetSprite(FloatingSpriteLoaderViewModel panel, uint id) =>
		panel.IsArchiveLoaded && id > 0 && id <= panel.Loader.SpriteCount;

	private static int CountAddedThings(ThingUndoAction action) => checked((int)(
		(action.ItemCountAfter - action.ItemCountBefore)
		+ (action.OutfitCountAfter - action.OutfitCountBefore)
		+ (action.EffectCountAfter - action.EffectCountBefore)
		+ (action.MissileCountAfter - action.MissileCountBefore)));

	private static ThingType? TryGetThing(ThingCatalog catalog, ThingKind kind, uint id)
	{
		try
		{
			return ThingExchangeHelper.GetThingFromCatalog(catalog, kind, id);
		}
		catch
		{
			return null;
		}
	}

	private static void ForEachId(uint fromId, uint toId, Action<uint> action)
	{
		var id = fromId;
		while (true)
		{
			action(id);
			if (id == toId)
				break;
			id++;
		}
	}
}
