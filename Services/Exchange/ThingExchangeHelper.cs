using System;
using System.IO;
using NyxAssets.Sprites;
using NyxAssets.Things;
using NyxAssets.Things.Exchange;
using NyxAssetsEditor.Services.Archive;
using NyxAssetsEditor.Services.Rendering;

namespace NyxAssetsEditor.Services.Exchange;

public static class ThingExchangeHelper
{
	public static ThingDocument LoadFromPath(string path, ClientDataReadOptions options)
	{
		if (path.EndsWith(".obd", StringComparison.OrdinalIgnoreCase))
			return ObdThingCodec.Read(path, options);

		return ThingDocumentJsonCodec.Read(path);
	}

	public static void ApplyEmbeddedSprites(ThingDocument document, SpriteLoader loader)
	{
		if (document.SpritesRgba == null)
			return;

		foreach (var pair in document.SpritesRgba)
			loader.SetSpritePixels(pair.Key, pair.Value);
	}

	public static ThingDocument CreatePortableDocument(ThingType thing, SpriteLoader loader, ClientDataReadOptions options, bool includeSprites = true)
	{
		using var spriteSource = new SpriteLoaderSpriteSource(loader);
		return ThingDocument.FromThing(thing, spriteSource, options, embedSprites: includeSprites);
	}

	public static uint GetNextAppendId(ThingCatalog catalog, ThingKind kind) =>
		kind switch
		{
			ThingKind.Item => catalog.ItemCount < ThingCatalog.FirstItemId
				? ThingCatalog.FirstItemId
				: catalog.ItemCount + 1,
			ThingKind.Outfit => catalog.OutfitCount < ThingCatalog.FirstOutfitId
				? ThingCatalog.FirstOutfitId
				: catalog.OutfitCount + 1,
			ThingKind.Effect => catalog.EffectCount < ThingCatalog.FirstEffectId
				? ThingCatalog.FirstEffectId
				: catalog.EffectCount + 1,
			ThingKind.Missile => catalog.MissileCount < ThingCatalog.FirstMissileId
				? ThingCatalog.FirstMissileId
				: catalog.MissileCount + 1,
			_ => throw new ArgumentOutOfRangeException(nameof(kind)),
		};

	public static ThingType? GetThingFromCatalog(ThingCatalog catalog, ThingKind kind, uint id) =>
		kind switch
		{
			ThingKind.Item => catalog.TryGetItem(id),
			ThingKind.Outfit => catalog.TryGetOutfit(id),
			ThingKind.Effect => catalog.TryGetEffect(id),
			ThingKind.Missile => catalog.TryGetMissile(id),
			_ => null,
		};

	public static void WriteNyxThingJson(string path, ThingDocument document, bool includeSprites = true) =>
		ThingDocumentJsonCodec.Write(path, document, includeSprites: includeSprites);

	public static void WriteObd(string path, ThingDocument document, ClientDataReadOptions options) =>
		ObdThingCodec.Write(path, document, options, ObdVersions.Version3);

	public static void ImportDocument(ThingDocument source, ThingCatalog catalog, uint assignId, SpriteLoader? loader)
	{
		var thing = ThingCloner.Clone(source.Thing, assignId);
		var document = new ThingDocument
		{
			Thing = thing,
			ClientVersion = source.ClientVersion,
			ObdVersion = source.ObdVersion,
			SpritesRgba = source.SpritesRgba,
		};

		document.ImportInto(catalog, assignId: assignId);

		if (loader != null)
			ApplyEmbeddedSprites(document, loader);
	}
}
