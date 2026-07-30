using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyxAssets.Sprites;
using NyxAssets.Things;
using NyxAssetsEditor.ViewModels.Core;

namespace NyxAssetsEditor.ViewModels.Pages;

public partial class ConverterViewModel : ViewModelBase
{
	[ObservableProperty]
	private string _title = "Archive & Assets Converter";

	[ObservableProperty]
	private string _description = "Convert asset formats, migrate target client versions, or compress asset media folders.";

	[ObservableProperty]
	private string _statusText = "Ready";

	[ObservableProperty]
	private bool _isBusy;

	#region TAB 1: Format Converter (SPR / Assets)
	[ObservableProperty]
	private string _sprSourcePath = string.Empty;

	[ObservableProperty]
	private string _sprTargetPath = string.Empty;

	[ObservableProperty]
	private bool _sprToAssetsMode = true; // true = spr to assets, false = assets to spr

	[ObservableProperty]
	private bool _sprExtendedIds = true;

	[ObservableProperty]
	private bool _sprTransparentPixels = true;

	[ObservableProperty]
	private int _sprCompressionLevel = 3;

	[ObservableProperty]
	private string _sprSignatureText = "0";
	#endregion

	#region TAB 2: Dat / Things Converter
	[ObservableProperty]
	private string _datSourcePath = string.Empty;

	[ObservableProperty]
	private string _datTargetPath = string.Empty;

	[ObservableProperty]
	private bool _datToThingsMode = true; // true = dat to json, false = json to dat

	[ObservableProperty]
	private string _datClientVersionText = "1098";

	[ObservableProperty]
	private bool _datExtendedIds = true;

	[ObservableProperty]
	private bool _datImprovedAnimations = true;

	[ObservableProperty]
	private bool _datOutfitFrameGroups = true;

	[ObservableProperty]
	private bool _datTransparentSprites = true;
	#endregion

	#region TAB 3: Target Version Migrator
	[ObservableProperty]
	private string _migSourceSprPath = string.Empty;

	[ObservableProperty]
	private string _migSourceDatPath = string.Empty;

	[ObservableProperty]
	private string _migTargetSprPath = string.Empty;

	[ObservableProperty]
	private string _migTargetDatPath = string.Empty;

	[ObservableProperty]
	private string _migSourceVersionText = "860";

	[ObservableProperty]
	private string _migTargetVersionText = "1098";

	[ObservableProperty]
	private bool _migSourceExtendedIds = false;

	[ObservableProperty]
	private bool _migSourceTransparent = true;

	[ObservableProperty]
	private bool _migTargetExtendedIds = true;

	[ObservableProperty]
	private bool _migTargetTransparent = true;

	[ObservableProperty]
	private bool _migTargetImprovedAnimations = true;

	[ObservableProperty]
	private bool _migTargetOutfitGroups = true;

	[ObservableProperty]
	private string _migTargetSignatureText = "0";
	#endregion

	#region TAB 4: Folder Compressor
	[ObservableProperty]
	private string _compSourceFolderPath = string.Empty;

	[ObservableProperty]
	private string _compTargetZipPath = string.Empty;

	[ObservableProperty]
	private bool _compIncludePng = true;

	[ObservableProperty]
	private bool _compIncludeGif = true;

	[ObservableProperty]
	private bool _compIncludeMp3 = true;

	[ObservableProperty]
	private bool _compIncludeMp4 = true;
	#endregion

	public ConverterViewModel()
	{
	}

	partial void OnDatClientVersionTextChanged(string value)
	{
		if (uint.TryParse(value, out var verVal))
		{
			var ver = new ClientDataVersion(verVal);
			DatExtendedIds = DatThingFormatRules.UsesExtendedSpriteIdsByDefault(ver);
			DatImprovedAnimations = DatThingFormatRules.UsesImprovedAnimationsByDefault(ver);
			DatOutfitFrameGroups = DatThingFormatRules.UsesOutfitFrameGroupsByDefault(ver);
		}
	}

	partial void OnMigTargetVersionTextChanged(string value)
	{
		if (uint.TryParse(value, out var verVal))
		{
			var ver = new ClientDataVersion(verVal);
			MigTargetExtendedIds = DatThingFormatRules.UsesExtendedSpriteIdsByDefault(ver);
			MigTargetImprovedAnimations = DatThingFormatRules.UsesImprovedAnimationsByDefault(ver);
			MigTargetOutfitGroups = DatThingFormatRules.UsesOutfitFrameGroupsByDefault(ver);
		}
	}

	partial void OnMigSourceVersionTextChanged(string value)
	{
		if (uint.TryParse(value, out var verVal))
		{
			var ver = new ClientDataVersion(verVal);
			MigSourceExtendedIds = DatThingFormatRules.UsesExtendedSpriteIdsByDefault(ver);
		}
	}

	[RelayCommand]
	private async Task ExecuteSprConversion()
	{
		if (string.IsNullOrWhiteSpace(SprSourcePath) || string.IsNullOrWhiteSpace(SprTargetPath))
		{
			StatusText = "Error: Please specify both source and target paths.";
			return;
		}

		IsBusy = true;
		StatusText = "Converting Sprites...";

		try
		{
			await Task.Run(() =>
			{
				if (SprToAssetsMode)
				{
					AssetArchiveWriter.ConvertSprToAssets(
						SprSourcePath,
						SprTargetPath,
						SprExtendedIds,
						SprTransparentPixels,
						SprCompressionLevel
					);
				}
				else
				{
					using var assets = AssetArchive.OpenReadOnlyFile(SprSourcePath, preloadPages: false);
					var count = assets.SpriteCount;
					byte[]?[] list = new byte[count + 1][];
					
					Parallel.For(1, (int)count + 1, id =>
					{
						if (assets.IsEmptySprite((uint)id))
						{
							list[id] = null;
						}
						else
						{
							var rgba = new byte[SpritePixelCodec.RgbaBufferLength];
							if (assets.TryDecodeSpriteById((uint)id, rgba))
								list[id] = rgba;
						}
					});

					uint sig = 0;
					if (uint.TryParse(SprSignatureText, out var parsedSig))
						sig = parsedSig;

					using var output = File.Create(SprTargetPath);
					SpriteSheetCompiler.WriteToStream(
						output,
						sig,
						SprExtendedIds,
						SprTransparentPixels,
						list
					);
				}
			});

			StatusText = "Sprite conversion completed successfully!";
		}
		catch (Exception ex)
		{
			StatusText = $"Error: {ex.Message}";
		}
		finally
		{
			IsBusy = false;
		}
	}

	[RelayCommand]
	private async Task ExecuteDatConversion()
	{
		if (string.IsNullOrWhiteSpace(DatSourcePath) || string.IsNullOrWhiteSpace(DatTargetPath))
		{
			StatusText = "Error: Please specify both source and target paths.";
			return;
		}

		if (!uint.TryParse(DatClientVersionText, out var verVal))
		{
			StatusText = "Error: Invalid client version value.";
			return;
		}

		IsBusy = true;
		StatusText = "Converting Things...";

		try
		{
			await Task.Run(() =>
			{
				var ver = new ClientDataVersion(verVal);
				var options = new ClientDataReadOptions
				{
					ClientVersion = ver,
					ExtendedSpriteIds = DatExtendedIds,
					ImprovedAnimations = DatImprovedAnimations,
					OutfitFrameGroups = DatOutfitFrameGroups,
					TransparentSprites = DatTransparentSprites
				};

				if (DatToThingsMode)
				{
					var catalog = ThingCatalog.Load(File.ReadAllBytes(DatSourcePath), options);
					catalog.ExportJson(DatTargetPath, options);
				}
				else
				{
					var catalog = ThingCatalog.LoadJson(DatSourcePath, options);
					using var output = File.Create(DatTargetPath);
					new DatThingCatalogWriter().Write(catalog, output, options);
				}
			});

			StatusText = "Thing conversion completed successfully!";
		}
		catch (Exception ex)
		{
			StatusText = $"Error: {ex.Message}";
		}
		finally
		{
			IsBusy = false;
		}
	}

	[RelayCommand]
	private async Task ExecuteMigration()
	{
		if (string.IsNullOrWhiteSpace(MigSourceSprPath) || string.IsNullOrWhiteSpace(MigSourceDatPath) ||
			string.IsNullOrWhiteSpace(MigTargetSprPath) || string.IsNullOrWhiteSpace(MigTargetDatPath))
		{
			StatusText = "Error: Please specify all source and target paths.";
			return;
		}

		if (!uint.TryParse(MigSourceVersionText, out var srcVerVal) || !uint.TryParse(MigTargetVersionText, out var tgtVerVal))
		{
			StatusText = "Error: Invalid version values.";
			return;
		}

		IsBusy = true;
		StatusText = "Migrating client version...";

		try
		{
			await Task.Run(() =>
			{
				var srcVer = new ClientDataVersion(srcVerVal);
				var tgtVer = new ClientDataVersion(tgtVerVal);

				var srcOptions = new ClientDataReadOptions
				{
					ClientVersion = srcVer,
					ExtendedSpriteIds = MigSourceExtendedIds,
					TransparentSprites = MigSourceTransparent
				};

				var tgtOptions = new ClientDataReadOptions
				{
					ClientVersion = tgtVer,
					ExtendedSpriteIds = MigTargetExtendedIds,
					ImprovedAnimations = MigTargetImprovedAnimations,
					OutfitFrameGroups = MigTargetOutfitGroups,
					TransparentSprites = MigTargetTransparent
				};

				// 1. Convert/recompile SPR
				StatusText = "Migrating sprites...";
				using var srcSpr = SpriteArchive.OpenReadOnlyFile(MigSourceSprPath, srcOptions.ResolveExtendedSpriteIds(), srcOptions.TransparentSprites);
				var count = srcSpr.SpriteCount;
				byte[]?[] spritesList = new byte[count + 1][];
				Parallel.For(1, (int)count + 1, id =>
				{
					if (srcSpr.IsEmptySprite((uint)id))
					{
						spritesList[id] = null;
					}
					else
					{
						var rgba = new byte[SpritePixelCodec.RgbaBufferLength];
						if (srcSpr.TryDecodeSpriteById((uint)id, rgba))
							spritesList[id] = rgba;
					}
				});

				uint sig = srcSpr.Signature;
				if (uint.TryParse(MigTargetSignatureText, out var parsedSig) && parsedSig != 0)
					sig = parsedSig;

				using (var outputSpr = File.Create(MigTargetSprPath))
				{
					SpriteSheetCompiler.WriteToStream(
						outputSpr,
						sig,
						tgtOptions.ResolveExtendedSpriteIds(),
						tgtOptions.TransparentSprites,
						spritesList
					);
				}

				// 2. Convert DAT
				StatusText = "Migrating catalog metadata...";
				var catalog = ThingCatalog.Load(File.ReadAllBytes(MigSourceDatPath), srcOptions);
				catalog.DatFormat = tgtOptions.ResolveDatThingFormat();
				catalog.DatSignature = sig;

				// Sanitize animations for improved animation targets (>= 10.50)
				if (tgtOptions.ResolveImprovedAnimations())
				{
					void SanitizeSection(IEnumerable<ThingType> collection, ThingKind kind)
					{
						foreach (var thing in collection)
						{
							foreach (var fg in thing.FrameGroups)
							{
								if (fg.Frames > 1)
								{
									fg.IsAnimation = true;
									if (fg.FrameTimings == null || fg.FrameTimings.Length != fg.Frames)
									{
										var duration = tgtOptions.ResolveDefaultFrameDurationMs(kind);
										if (duration == 0) duration = 150;

										fg.FrameTimings = new AnimationFrameTiming[fg.Frames];
										for (int i = 0; i < fg.Frames; i++)
										{
											fg.FrameTimings[i] = new AnimationFrameTiming(duration, duration);
										}
									}
								}
							}
						}
					}
					SanitizeSection(catalog.EnumerateItems(), ThingKind.Item);
					SanitizeSection(catalog.EnumerateOutfits(), ThingKind.Outfit);
					SanitizeSection(catalog.EnumerateEffects(), ThingKind.Effect);
					SanitizeSection(catalog.EnumerateMissiles(), ThingKind.Missile);
				}

				using (var outputDat = File.Create(MigTargetDatPath))
				{
					new DatThingCatalogWriter().Write(catalog, outputDat, tgtOptions, sig);
				}
			});

			StatusText = "Version migration completed successfully!";
		}
		catch (Exception ex)
		{
			StatusText = $"Migration Error: {ex.Message}";
		}
		finally
		{
			IsBusy = false;
		}
	}

	[RelayCommand]
	private async Task ExecuteCompression()
	{
		if (string.IsNullOrWhiteSpace(CompSourceFolderPath) || string.IsNullOrWhiteSpace(CompTargetZipPath))
		{
			StatusText = "Error: Please specify source folder and target ZIP paths.";
			return;
		}

		if (!Directory.Exists(CompSourceFolderPath))
		{
			StatusText = "Error: Source folder does not exist.";
			return;
		}

		IsBusy = true;
		StatusText = "Compressing files...";

		try
		{
			await Task.Run(() =>
			{
				if (File.Exists(CompTargetZipPath))
					File.Delete(CompTargetZipPath);

				using var zip = ZipFile.Open(CompTargetZipPath, ZipArchiveMode.Create);
				var searchPatterns = new List<string>();
				if (CompIncludePng) searchPatterns.Add(".png");
				if (CompIncludeGif) searchPatterns.Add(".gif");
				if (CompIncludeMp3) searchPatterns.Add(".mp3");
				if (CompIncludeMp4) searchPatterns.Add(".mp4");

				if (searchPatterns.Count == 0)
					throw new InvalidOperationException("No file formats selected to compress.");

				var files = Directory.EnumerateFiles(CompSourceFolderPath, "*.*", SearchOption.AllDirectories);
				foreach (var file in files)
				{
					var ext = Path.GetExtension(file).ToLower();
					if (searchPatterns.Contains(ext))
					{
						var relativePath = Path.GetRelativePath(CompSourceFolderPath, file);
						zip.CreateEntryFromFile(file, relativePath);
					}
				}
			});

			StatusText = "Folder compression completed successfully!";
		}
		catch (Exception ex)
		{
			StatusText = $"Compression Error: {ex.Message}";
		}
		finally
		{
			IsBusy = false;
		}
	}
}
