using System;
using System.IO;
using NyxAssets.Sprites;
using NyxAssets.Things;
using NyxAssets.Things.Exchange;
using NyxAssetsEditor.Services.Archive;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.Common;

namespace NyxAssetsEditor.Services.Exchange;

public static class ThingExchangeHelper
{
	public static ThingDocument LoadFromPath(string path, ClientDataReadOptions options)
	{
		if (SupportedFileFormats.HasExtension(path, SupportedFileFormats.ExtObd))
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

	public static void ImportDocument(ThingDocument source, ThingCatalog catalog, uint assignId, SpriteLoader? loader, Action<uint>? onSpriteAdded = null)
	{
		var thing = ThingCloner.Clone(source.Thing, assignId);
		var spriteIdMap = new System.Collections.Generic.Dictionary<uint, uint>();

		if (loader != null)
		{
			var oldSpriteIds = new System.Collections.Generic.List<uint>();
			foreach (var group in source.Thing.FrameGroups)
			{
				if (group.SpriteIds != null)
				{
					foreach (var id in group.SpriteIds)
					{
						if (id != 0 && !oldSpriteIds.Contains(id))
							oldSpriteIds.Add(id);
					}
				}
			}

			if (source.SpritesRgba != null)
			{
				foreach (var oldId in oldSpriteIds)
				{
					if (source.SpritesRgba.TryGetValue(oldId, out var pixels))
					{
						bool reused = false;
						if (oldId <= loader.SpriteCount)
						{
							try
							{
								var existing = loader.LoadSpritePixels(oldId);
								if (existing != null && ArePixelsEqual(existing, pixels))
								{
									spriteIdMap[oldId] = oldId;
									reused = true;
								}
							}
							catch
							{
							}
						}

						if (!reused)
						{
							var newId = loader.AddNewSprite();
							loader.SetSpritePixels(newId, pixels);
							spriteIdMap[oldId] = newId;
							onSpriteAdded?.Invoke(newId);
						}
					}
				}
			}
		}

		foreach (var group in thing.FrameGroups)
		{
			if (group.SpriteIds != null)
			{
				for (int i = 0; i < group.SpriteIds.Length; i++)
				{
					var oldId = group.SpriteIds[i];
					if (oldId != 0 && spriteIdMap.TryGetValue(oldId, out var newId))
					{
						group.SpriteIds[i] = newId;
					}
					else
					{
						group.SpriteIds[i] = 0;
					}
				}
			}
		}

		var document = new ThingDocument
		{
			Thing = thing,
			ClientVersion = source.ClientVersion,
			ObdVersion = source.ObdVersion,
			SpritesRgba = source.SpritesRgba,
		};

		document.ImportInto(catalog, assignId: assignId);
	}

	private static bool ArePixelsEqual(byte[] a, byte[] b)
	{
		if (a.Length != b.Length) return false;
		for (int i = 0; i < a.Length; i++)
		{
			if (a[i] != b[i]) return false;
		}
		return true;
	}
}
