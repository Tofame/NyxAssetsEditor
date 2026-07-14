using System;
using System.Collections.Generic;
using System.Linq;
using NyxAssets.Sprites;
using NyxAssets.Things;
using NyxAssets.Things.Frames;
using NyxAssetsEditor.Models.Looktypes;
using NyxAssetsEditor.Services.Archive;
using NyxAssetsEditor.Services.Looktypes;

namespace NyxAssetsEditor.Services.Rendering;

public sealed record LooktypeRenderResult(
	byte[]? Pixels,
	int Width,
	int Height,
	int ResolvedFrame,
	int FrameCount,
	int SuggestedDelayMs,
	IReadOnlyList<string> Warnings)
{
	public bool HasImage => Pixels != null && Width > 0 && Height > 0;
	public int AnimationStartFrame { get; init; }
	public bool IsPingPongAnimation { get; init; }
}

public enum MountedOutfitAlignment
{
	OtClientCompatible,
	IndependentAssetDisplacement,
}

public sealed record LooktypeRenderOptions(
	MountedOutfitAlignment MountedAlignment = MountedOutfitAlignment.OtClientCompatible,
	int MountedRiderOffsetX = 0,
	int MountedRiderOffsetY = 0);

public static class LooktypeRenderer
{
	private readonly record struct DrawEntry(ThingType Thing, ThingFrameSelection Selection, bool IsMount);

	public static LooktypeRenderResult Render(LooktypeProfile profile, ThingCatalog? catalog, SpriteLoader? loader,
		int fallbackDelayMs, LooktypeRenderOptions? options = null, bool improvedAnimations = true)
	{
		var warnings = new List<string>();
		options ??= new LooktypeRenderOptions();
		if (catalog == null || loader == null)
			return Empty("Load a compatible things and sprite archive pair to preview this looktype.", warnings);

		try
		{
			return profile.AppearanceKind == LooktypeAppearanceKind.Item
				? RenderItem(profile, catalog, loader, fallbackDelayMs, warnings, options, improvedAnimations)
				: RenderOutfit(profile, catalog, loader, fallbackDelayMs, warnings, options, improvedAnimations);
		}
		catch (Exception ex)
		{
			warnings.Add($"Preview could not be rendered: {ex.Message}");
			return new(null, 0, 0, 0, 0, fallbackDelayMs, warnings);
		}
	}

	private static LooktypeRenderResult RenderItem(LooktypeProfile profile, ThingCatalog catalog, SpriteLoader loader,
		int fallbackDelayMs, List<string> warnings, LooktypeRenderOptions options, bool improvedAnimations)
	{
		var item = catalog.TryGetItem(profile.LookTypeEx);
		if (item == null) return Empty($"Item {profile.LookTypeEx} is not present in the selected archive.", warnings);
		if (item.FrameGroups.Count == 0) return Empty($"Item {profile.LookTypeEx} has no frame groups.", warnings);

		var group = item.FrameGroups[0];
		uint? patternX = null;
		if (profile.AutoRotate || profile.Direction != LooktypeDirection.South)
		{
			if (item.Rotatable && group.PatternX > 1)
				patternX = (uint)profile.Direction % group.PatternX;
			else
				warnings.Add("This item does not support rotation; its default pattern is shown.");
		}

		var selection = ThingFrameResolver.GetItemFrame(item, new ItemFrameRequest
		{
			Frame = profile.AnimationEnabled ? (uint)Math.Max(0, profile.AnimationPhase) : 0,
			PatternX = patternX,
			PatternY = patternX.HasValue ? 0u : null,
		});
		return Compose(new[] { new DrawEntry(item, selection, false) }, loader, profile, fallbackDelayMs, warnings,
			colorizeRider: false, options: options, improvedAnimations: improvedAnimations);
	}

	private static LooktypeRenderResult RenderOutfit(LooktypeProfile profile, ThingCatalog catalog, SpriteLoader loader,
		int fallbackDelayMs, List<string> warnings, LooktypeRenderOptions options, bool improvedAnimations)
	{
		var outfit = catalog.TryGetOutfit(profile.LookType);
		if (outfit == null) return Empty($"Outfit {profile.LookType} is not present in the selected archive.", warnings);
		if (outfit.FrameGroups.Count == 0) return Empty($"Outfit {profile.LookType} has no frame groups.", warnings);

		var request = new OutfitFrameRequest
		{
			Direction = (int)profile.Direction,
			// Phase zero resolves the idle/stand frame group. Walking phases are only
			// requested while playback is enabled.
			WalkPhase = profile.AnimationEnabled ? (uint)Math.Max(1, profile.AnimationPhase + 1) : 0,
			AddonMask = profile.Addons,
			Mounted = profile.Mount > 0,
		};

		var baseSelection = ThingFrameResolver.GetOutfitFrame(outfit, request);
		var supportedAddonCount = Math.Max(0, (int)baseSelection.FrameGroup.PatternY - 1);
		var supportedMask = supportedAddonCount >= 8 ? byte.MaxValue : (byte)((1 << supportedAddonCount) - 1);
		if ((profile.Addons & ~supportedMask) != 0)
			warnings.Add("One or more selected addons are not available on this outfit and were ignored.");
		if (profile.Mount > 0 && baseSelection.FrameGroup.PatternZ < 2)
			warnings.Add("This outfit has no mounted rider pose; its base pose is used.");

		ThingType? mount = null;
		if (profile.Mount > 0)
		{
			mount = catalog.TryGetOutfit(profile.Mount);
			if (mount == null) warnings.Add($"Mount {profile.Mount} is unavailable; the rider is shown without it.");
		}

		var entries = ThingFrameResolver.EnumerateMountedOutfitFrames(outfit, mount, request)
			.Select(value => new DrawEntry(value.Thing, value.Selection, value.IsMount)).ToList();
		return Compose(entries, loader, profile, fallbackDelayMs, warnings, colorizeRider: true, options: options,
			improvedAnimations: improvedAnimations);
	}

	private static LooktypeRenderResult Compose(IReadOnlyList<DrawEntry> entries, SpriteLoader loader, LooktypeProfile profile,
		int fallbackDelayMs, List<string> warnings, bool colorizeRider, LooktypeRenderOptions options,
		bool improvedAnimations)
	{
		if (entries.Count == 0) return Empty("No drawable frames were resolved.", warnings);
		// Mounts are always behind the rider. Alignment is configurable because clients
		// disagree on whether the rider keeps its own asset displacement or shares the
		// mount anchor used by OTClient-compatible renderers.
		var orderedEntries = entries.OrderByDescending(entry => entry.IsMount).ToArray();
		var mountEntry = orderedEntries.FirstOrDefault(entry => entry.IsMount);
		var hasMount = orderedEntries.Any(entry => entry.IsMount);
		var mountOffsetX = hasMount && mountEntry.Thing.HasOffset ? (int)mountEntry.Thing.OffsetX : 0;
		var mountOffsetY = hasMount && mountEntry.Thing.HasOffset ? (int)mountEntry.Thing.OffsetY : 0;
		var edge = SpritePixelCodec.SpriteEdgeLength;
		var contentWidth = orderedEntries.Max(entry => (int)entry.Selection.FrameGroup.Width * edge +
			ResolveDrawOffset(entry, hasMount, mountOffsetX, horizontal: true, options.MountedAlignment));
		var contentHeight = orderedEntries.Max(entry => (int)entry.Selection.FrameGroup.Height * edge +
			ResolveDrawOffset(entry, hasMount, mountOffsetY, horizontal: false, options.MountedAlignment));
		var riderOffsetX = hasMount ? Math.Clamp(options.MountedRiderOffsetX, -128, 128) : 0;
		var riderOffsetY = hasMount ? Math.Clamp(options.MountedRiderOffsetY, -128, 128) : 0;
		var width = contentWidth + Math.Abs(riderOffsetX);
		var height = contentHeight + Math.Abs(riderOffsetY);
		if (width <= 0 || height <= 0) return Empty("The resolved frame has no dimensions.", warnings);

		var canvas = new byte[width * height * 4];
		var drewAny = false;
		foreach (var entry in orderedEntries)
		{
			var fg = entry.Selection.FrameGroup;
			var entryWidth = (int)fg.Width * edge;
			var entryHeight = (int)fg.Height * edge;
			var baseX = contentWidth - entryWidth -
				ResolveDrawOffset(entry, hasMount, mountOffsetX, horizontal: true, options.MountedAlignment) +
				Math.Max(0, -riderOffsetX) + (!entry.IsMount && hasMount ? riderOffsetX : 0);
			var baseY = contentHeight - entryHeight -
				ResolveDrawOffset(entry, hasMount, mountOffsetY, horizontal: false, options.MountedAlignment) +
				Math.Max(0, -riderOffsetY) + (!entry.IsMount && hasMount ? riderOffsetY : 0);

			foreach (var slot in entry.Selection.EnumerateSpriteSlots().OrderBy(s => s.Layer))
			{
				if (slot.SpriteId == 0) continue;
				byte[] pixels;
				try { pixels = loader.LoadSpritePixels(slot.SpriteId); }
				catch { warnings.Add($"Sprite {slot.SpriteId} could not be decoded and was skipped."); continue; }
				var x = baseX + (int)((fg.Width - slot.InnerWidth - 1) * edge);
				var y = baseY + (int)((fg.Height - slot.InnerHeight - 1) * edge);

				if (!colorizeRider || entry.IsMount || fg.Layers <= 1 || slot.Layer == 0)
				{
					if (!colorizeRider || !entry.IsMount || slot.Layer == 0)
					{
						Blit(canvas, width, height, x, y, pixels);
						drewAny = true;
					}
					continue;
				}

				if (fg.Layers == 2)
					ApplyCombinedMask(canvas, width, height, x, y, pixels, profile);
				else
					ApplyLogicalMask(canvas, width, height, x, y, pixels, ColorForLogicalLayer((int)slot.Layer, profile));
			}
		}

		if (!drewAny) return Empty("No sprites could be decoded for this looktype.", warnings);
		var primary = orderedEntries.Last(e => !e.IsMount).Selection;
		var frameCount = Math.Max(1, (int)primary.FrameGroup.Frames);
		var resolvedFrame = (int)primary.Frame;
		var delay = profile.WalkIntervalMs > 0
			? profile.WalkIntervalMs
			: (int)ThingAnimationPlayback.GetFrameDelayMs(primary.FrameGroup, resolvedFrame, (uint)fallbackDelayMs,
				improvedAnimations, colorizeRider ? ThingKind.Outfit : ThingKind.Item);
		return new(canvas, width, height, resolvedFrame, frameCount, Math.Max(16, delay), warnings.Distinct().ToArray())
		{
			AnimationStartFrame = primary.FrameGroup.StartFrame,
			IsPingPongAnimation = primary.FrameGroup.LoopCount < 0,
		};
	}

	private static int ResolveDrawOffset(DrawEntry entry, bool hasMount, int mountOffset, bool horizontal,
		MountedOutfitAlignment alignment)
	{
		if (!hasMount || alignment == MountedOutfitAlignment.IndependentAssetDisplacement)
		{
			if (!entry.Thing.HasOffset) return 0;
			return horizontal ? (int)entry.Thing.OffsetX : (int)entry.Thing.OffsetY;
		}

		// OTClient-compatible renderers adjust dest by the mount displacement before
		// drawing it. ThingType::draw then applies that displacement once more.
		// Before drawing the rider, the rider displacement is added to dest and is then
		// subtracted inside ThingType::draw, leaving one mount displacement for the rider.
		return entry.IsMount ? mountOffset * 2 : mountOffset;
	}

	private static TibiaOutfitColor ColorForLogicalLayer(int layer, LooktypeProfile profile) => layer switch
	{
		1 => TibiaOutfitPalette.Get(profile.Body),
		2 => TibiaOutfitPalette.Get(profile.Legs),
		3 => TibiaOutfitPalette.Get(profile.Feet),
		4 => TibiaOutfitPalette.Get(profile.Head),
		_ => TibiaOutfitPalette.Get(0),
	};

	private static void ApplyCombinedMask(byte[] canvas, int width, int height, int x, int y, byte[] mask, LooktypeProfile profile)
	{
		var edge = SpritePixelCodec.SpriteEdgeLength;
		for (var py = 0; py < edge; py++) for (var px = 0; px < edge; px++)
		{
			var source = (py * edge + px) * 4;
			if (mask[source + 3] == 0) continue;
			var r = mask[source]; var g = mask[source + 1]; var b = mask[source + 2];
			TibiaOutfitColor color;
			if (r > 128 && g > 128 && b < 128) color = TibiaOutfitPalette.Get(profile.Head);
			else if (r >= g && r >= b) color = TibiaOutfitPalette.Get(profile.Body);
			else if (g >= r && g >= b) color = TibiaOutfitPalette.Get(profile.Legs);
			else color = TibiaOutfitPalette.Get(profile.Feet);
			MultiplyPixel(canvas, width, height, x + px, y + py, color, mask[source + 3]);
		}
	}

	private static void ApplyLogicalMask(byte[] canvas, int width, int height, int x, int y, byte[] mask, TibiaOutfitColor color)
	{
		var edge = SpritePixelCodec.SpriteEdgeLength;
		for (var py = 0; py < edge; py++) for (var px = 0; px < edge; px++)
		{
			var source = (py * edge + px) * 4;
			if (mask[source + 3] > 0) MultiplyPixel(canvas, width, height, x + px, y + py, color, mask[source + 3]);
		}
	}

	private static void MultiplyPixel(byte[] canvas, int width, int height, int x, int y, TibiaOutfitColor color, byte alpha)
	{
		if (x < 0 || y < 0 || x >= width || y >= height) return;
		var offset = (y * width + x) * 4;
		if (canvas[offset + 3] == 0) return;
		var amount = alpha / 255f;
		canvas[offset] = (byte)(canvas[offset] * ((1 - amount) + amount * color.Red / 255f));
		canvas[offset + 1] = (byte)(canvas[offset + 1] * ((1 - amount) + amount * color.Green / 255f));
		canvas[offset + 2] = (byte)(canvas[offset + 2] * ((1 - amount) + amount * color.Blue / 255f));
	}

	private static void Blit(byte[] target, int width, int height, int x, int y, byte[] source)
	{
		var edge = SpritePixelCodec.SpriteEdgeLength;
		for (var sy = 0; sy < edge; sy++) for (var sx = 0; sx < edge; sx++)
		{
			var dx = x + sx; var dy = y + sy;
			if (dx < 0 || dy < 0 || dx >= width || dy >= height) continue;
			var si = (sy * edge + sx) * 4; var di = (dy * width + dx) * 4;
			var sa = source[si + 3] / 255f;
			if (sa <= 0) continue;
			var da = target[di + 3] / 255f; var oa = sa + da * (1 - sa);
			if (oa <= 0) continue;
			for (var c = 0; c < 3; c++) target[di + c] = (byte)((source[si + c] * sa + target[di + c] * da * (1 - sa)) / oa);
			target[di + 3] = (byte)(oa * 255);
		}
	}

	private static LooktypeRenderResult Empty(string warning, List<string> warnings)
	{
		warnings.Add(warning);
		return new(null, 0, 0, 0, 0, 300, warnings);
	}
}
