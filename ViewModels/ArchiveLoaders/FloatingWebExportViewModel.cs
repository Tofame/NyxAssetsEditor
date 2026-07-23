using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyxAssets.Things;
using NyxAssets.Things.Frames;
using NyxAssets.Sprites;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Pages;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.Services.Archive;

namespace NyxAssetsEditor.ViewModels.ArchiveLoaders;

public sealed class WebExportArchivePairViewModel
{
	public LinkedArchivePair Pair { get; }
	public string DisplayName => $"{Pair.ThingsPanel.FileName} + {Pair.SpritePanel.FileName}";
	public WebExportArchivePairViewModel(LinkedArchivePair pair) => Pair = pair;
}

public partial class FloatingWebExportViewModel : PanelViewModelBase, IDisposable
{
	private readonly AssetsViewModel _parent;
	private CancellationTokenSource? _cts;

	[ObservableProperty]
	private string _title = "Web Export";

	[ObservableProperty]
	private ObservableCollection<WebExportArchivePairViewModel> _archivePairs = new();

	[ObservableProperty]
	private WebExportArchivePairViewModel? _selectedArchivePair;

	[ObservableProperty]
	private string _exportPath = string.Empty;

	[ObservableProperty]
	private bool _exportItems = true;

	[ObservableProperty]
	private string _itemFilter = "All"; // "All", "Pickupable", "Stackable"

	[ObservableProperty]
	private bool _exportOutfits = false;

	[ObservableProperty]
	private bool _exportEffects = false;

	[ObservableProperty]
	private bool _exportMissiles = false;

	[ObservableProperty]
	private string _outfitMode = "FirstFrame"; // "FirstFrame", "Spritesheet"

	[ObservableProperty]
	private string _outfitDirection = "South"; // "South", "East", "North", "West"

	[ObservableProperty]
	private string _outputFormat = "png"; // "webp", "png", "jpg", "bmp"

	[ObservableProperty]
	private int _compressionLevel = 8; // 0 to 9

	[ObservableProperty]
	private bool _isExporting;

	[ObservableProperty]
	private string _statusText = "Idle";

	[ObservableProperty]
	private double _progressValue;

	[ObservableProperty]
	private bool _optimizeWithOxiPng = true;

	public bool CanExport => SelectedArchivePair != null && !IsExporting && !string.IsNullOrWhiteSpace(ExportPath) && (ExportItems || ExportOutfits || ExportEffects || ExportMissiles);
	public bool CanCancel => IsExporting;

	public bool IsOutfitFirstFrameMode => ExportOutfits && OutfitMode == "FirstFrame";
	public bool IsPngFormat => OutputFormat == "png";

	partial void OnExportOutfitsChanged(bool value) => OnPropertyChanged(nameof(IsOutfitFirstFrameMode));
	partial void OnOutfitModeChanged(string value) => OnPropertyChanged(nameof(IsOutfitFirstFrameMode));
	partial void OnOutputFormatChanged(string value) => OnPropertyChanged(nameof(IsPngFormat));

	public FloatingWebExportViewModel(AssetsViewModel parent)
	{
		_parent = parent;
		RefreshArchivePairs();
		PropertyChanged += (s, e) =>
		{
			if (e.PropertyName == nameof(SelectedArchivePair) ||
				e.PropertyName == nameof(ExportPath) ||
				e.PropertyName == nameof(ExportItems) ||
				e.PropertyName == nameof(ExportOutfits) ||
				e.PropertyName == nameof(ExportEffects) ||
				e.PropertyName == nameof(ExportMissiles) ||
				e.PropertyName == nameof(IsExporting))
			{
				ExportCommand.NotifyCanExecuteChanged();
				CancelCommand.NotifyCanExecuteChanged();
			}
		};
	}

	public void RefreshArchivePairs()
	{
		ArchivePairs.Clear();
		foreach (var pair in _parent.GetCompilePairs())
		{
			ArchivePairs.Add(new WebExportArchivePairViewModel(pair));
		}
		SelectedArchivePair = ArchivePairs.FirstOrDefault();
	}

	[RelayCommand(CanExecute = nameof(CanExport))]
	private async Task Export()
	{
		if (SelectedArchivePair == null || string.IsNullOrWhiteSpace(ExportPath)) return;

		IsExporting = true;
		StatusText = "Initializing export...";
		ProgressValue = 0;
		_cts = new CancellationTokenSource();

		var pair = SelectedArchivePair.Pair;
		var catalog = pair.ThingsPanel.Catalog;
		var loader = pair.SpritePanel.Loader;
		var destPath = ExportPath;
		var filter = ItemFilter;
		var doItems = ExportItems;
		var doOutfits = ExportOutfits;
		var doEffects = ExportEffects;
		var doMissiles = ExportMissiles;
		var modeOutfit = OutfitMode;
		var dirOutfit = OutfitDirection;
		var format = OutputFormat;
		var compression = CompressionLevel;

		if (catalog == null)
		{
			StatusText = "Error: Things catalog not loaded.";
			IsExporting = false;
			return;
		}

		try
		{
			await Task.Run(() => DoExportWork(catalog, loader, destPath, doItems, filter, doOutfits, doEffects, doMissiles, modeOutfit, dirOutfit, format, compression, _cts.Token)).ConfigureAwait(true);
			StatusText = _cts.Token.IsCancellationRequested ? "Export cancelled." : "Export completed successfully!";
		}
		catch (Exception ex)
		{
			StatusText = $"Error: {ex.Message}";
		}
		finally
		{
			IsExporting = false;
			_cts = null;
		}
	}

	[RelayCommand(CanExecute = nameof(CanCancel))]
	private void Cancel()
	{
		_cts?.Cancel();
		StatusText = "Cancelling...";
	}

	private void DoExportWork(
		ThingCatalog catalog,
		SpriteLoader loader,
		string destFolder,
		bool doItems,
		string itemFilter,
		bool doOutfits,
		bool doEffects,
		bool doMissiles,
		string outfitMode,
		string outfitDirection,
		string format,
		int compression,
		CancellationToken token)
	{
		var tasks = new List<Action>();

		if (doItems)
		{
			var items = catalog.EnumerateItems().ToList();
			if (itemFilter == "Pickupable")
				items = items.Where(i => i.Pickupable).ToList();
			else if (itemFilter == "Stackable")
				items = items.Where(i => i.Stackable).ToList();

			foreach (var item in items)
			{
				var it = item;
				tasks.Add(() =>
				{
					if (token.IsCancellationRequested) return;
					var pixels = ThingPreviewRenderer.RenderPreviewRgba(it, loader);
					if (pixels != null)
					{
						var outputPath = Path.Combine(destFolder, $"item_{it.Id}.{format}");
						WriteImage(pixels, outputPath, 32, 32, format, compression);
					}
				});
			}
		}

		if (doOutfits)
		{
			var outfits = catalog.EnumerateOutfits().ToList();
			var dir = outfitDirection switch
			{
				"East" => Direction4.East,
				"North" => Direction4.North,
				"West" => Direction4.West,
				_ => Direction4.South
			};

			foreach (var outfit in outfits)
			{
				var ot = outfit;
				tasks.Add(() =>
				{
					if (token.IsCancellationRequested) return;

					if (outfitMode == "FirstFrame")
					{
						var req = new OutfitFrameRequest { Direction = (int)dir, WalkPhase = 0, AddonMask = 0 };
						var pixels = RenderOutfitFrameRgba(ot, loader, req);
						if (pixels != null)
						{
							var outputPath = Path.Combine(destFolder, $"outfit_{ot.Id}.{format}");
							WriteImage(pixels, outputPath, 32, 32, format, compression);
						}
					}
					else
					{
						// Spritesheet Mode
						var options = new ThingAppearanceOptions { FrameGroupIndex = 0, ShowGrid = false };
						var pixels = ThingAppearanceRenderer.RenderPatternGrid(ot, loader, options);
						if (pixels != null && ot.FrameGroups.Count > 0)
						{
							var fg = ot.FrameGroups[0];
							var edge = SpritePixelCodec.SpriteEdgeLength;
							var sheetW = Math.Max(edge, (int)(fg.PatternX * fg.Width * edge));
							var sheetH = Math.Max(edge, (int)(fg.PatternY * fg.Height * edge));
							var outputPath = Path.Combine(destFolder, $"outfit_{ot.Id}_sheet.{format}");
							WriteImage(pixels, outputPath, sheetW, sheetH, format, compression);
						}
					}
				});
			}
		}

		if (doEffects)
		{
			var effects = catalog.EnumerateEffects().ToList();
			foreach (var effect in effects)
			{
				var ef = effect;
				tasks.Add(() =>
				{
					if (token.IsCancellationRequested) return;
					var pixels = ThingPreviewRenderer.RenderPreviewRgba(ef, loader);
					if (pixels != null)
					{
						var outputPath = Path.Combine(destFolder, $"effect_{ef.Id}.{format}");
						WriteImage(pixels, outputPath, 32, 32, format, compression);
					}
				});
			}
		}

		if (doMissiles)
		{
			var missiles = catalog.EnumerateMissiles().ToList();
			foreach (var missile in missiles)
			{
				var mi = missile;
				tasks.Add(() =>
				{
					if (token.IsCancellationRequested) return;
					var pixels = ThingPreviewRenderer.RenderPreviewRgba(mi, loader);
					if (pixels != null)
					{
						var outputPath = Path.Combine(destFolder, $"missile_{mi.Id}.{format}");
						WriteImage(pixels, outputPath, 32, 32, format, compression);
					}
				});
			}
		}

		int total = tasks.Count;
		int completed = 0;

		Parallel.ForEach(tasks, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, (task, state) =>
		{
			if (token.IsCancellationRequested)
			{
				state.Stop();
				return;
			}

			task();

			var currentCompleted = Interlocked.Increment(ref completed);
			var progress = (double)currentCompleted / total * 100.0;
			
			// Update UI progress safely
			Avalonia.Threading.Dispatcher.UIThread.Post(() =>
			{
				ProgressValue = progress;
				StatusText = $"Exported {currentCompleted} / {total} images...";
			});
		});

		if (format == "png" && OptimizeWithOxiPng && !token.IsCancellationRequested)
		{
			RunOxiPng(destFolder, token);
		}
	}

	private void RunOxiPng(string directory, CancellationToken token)
	{
		Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			StatusText = $"Optimizing PNGs in {Path.GetFileName(directory)} with OxiPNG...";
		});

		try
		{
			var psi = new System.Diagnostics.ProcessStartInfo
			{
				FileName = "oxipng",
				Arguments = $"-o 3 --strip safe --quiet \"{Path.Combine(directory, "*.png")}\"",
				UseShellExecute = false,
				CreateNoWindow = true
			};
			using var process = System.Diagnostics.Process.Start(psi);
			if (process != null)
			{
				process.WaitForExit();
			}
		}
		catch (Exception ex)
		{
			Avalonia.Threading.Dispatcher.UIThread.Post(() =>
			{
				StatusText = $"Warning: OxiPNG optimization failed: {ex.Message}";
			});
			// Wait 1.5s to let the warning be readable
			Thread.Sleep(1500);
		}
	}

	private static byte[]? RenderOutfitFrameRgba(ThingType outfit, SpriteLoader loader, OutfitFrameRequest request)
	{
		if (outfit.FrameGroups.Count == 0) return null;
		
		ThingFrameSelection selection;
		try
		{
			selection = ThingFrameResolver.GetOutfitFrame(outfit, request);
		}
		catch
		{
			return null;
		}

		var fg = selection.FrameGroup;
		var edge = SpritePixelCodec.SpriteEdgeLength;
		var canvasW = (int)(fg.Width * edge);
		var canvasH = (int)(fg.Height * edge);
		if (canvasW <= 0 || canvasH <= 0) return null;
		
		var canvas = new byte[canvasW * canvasH * 4];
		var drewAny = false;
		
		foreach (var slot in selection.EnumerateSpriteSlots().OrderBy(s => s.Layer))
		{
			if (slot.Layer != 0) continue; // base layer only
			if (slot.SpriteId == 0) continue;
			
			byte[] pixels;
			try
			{
				pixels = loader.LoadSpritePixels(slot.SpriteId);
			}
			catch
			{
				continue;
			}
			
			var innerX = (int)((fg.Width - slot.InnerWidth - 1) * edge);
			var innerY = (int)((fg.Height - slot.InnerHeight - 1) * edge);
			BlitSpriteBuffer(canvas, canvasW, canvasH, innerX, innerY, pixels);
			drewAny = true;
		}
		
		if (!drewAny) return null;
		if (canvasW == edge && canvasH == edge) return canvas;
		
		return ResizeToSpriteEdge(canvas, canvasW, canvasH);
	}

	private static byte[] ResizeToSpriteEdge(byte[] source, int srcW, int srcH)
	{
		var edge = SpritePixelCodec.SpriteEdgeLength;
		var srcInfo = new SkiaSharp.SKImageInfo(srcW, srcH, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
		using var original = new SkiaSharp.SKBitmap();
		var pin = System.Runtime.InteropServices.GCHandle.Alloc(source, System.Runtime.InteropServices.GCHandleType.Pinned);
		try
		{
			original.InstallPixels(srcInfo, pin.AddrOfPinnedObject(), srcInfo.RowBytes);
			var dstInfo = new SkiaSharp.SKImageInfo(edge, edge, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
			using var resized = original.Resize(dstInfo, new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Nearest, SkiaSharp.SKMipmapMode.None));
			return resized.Bytes;
		}
		finally
		{
			pin.Free();
		}
	}

	private static void BlitSpriteBuffer(byte[] dst, int dstW, int dstH, int x, int y, byte[] src)
	{
		var edge = SpritePixelCodec.SpriteEdgeLength;
		for (var sy = 0; sy < edge; sy++)
		{
			var dy = y + sy;
			if (dy < 0 || dy >= dstH) continue;
			for (var sx = 0; sx < edge; sx++)
			{
				var dx = x + sx;
				if (dx < 0 || dx >= dstW) continue;

				var srcOffset = (sy * edge + sx) * 4;
				var dstOffset = (dy * dstW + dx) * 4;

				var srcA = src[srcOffset + 3];
				if (srcA == 0) continue;

				if (srcA == 255)
				{
					dst[dstOffset] = src[srcOffset];
					dst[dstOffset + 1] = src[srcOffset + 1];
					dst[dstOffset + 2] = src[srcOffset + 2];
					dst[dstOffset + 3] = src[srcOffset + 3];
				}
				else
				{
					var sA = srcA / 255f;
					var dA = dst[dstOffset + 3] / 255f;
					var outA = sA + dA * (1 - sA);
					if (outA > 0)
					{
						dst[dstOffset] = (byte)Math.Clamp((src[srcOffset] * sA + dst[dstOffset] * dA * (1 - sA)) / outA, 0, 255);
						dst[dstOffset + 1] = (byte)Math.Clamp((src[srcOffset + 1] * sA + dst[dstOffset + 1] * dA * (1 - sA)) / outA, 0, 255);
						dst[dstOffset + 2] = (byte)Math.Clamp((src[srcOffset + 2] * sA + dst[dstOffset + 2] * dA * (1 - sA)) / outA, 0, 255);
						dst[dstOffset + 3] = (byte)(outA * 255);
					}
				}
			}
		}
	}

	private static void WriteImage(byte[] pixels, string outputPath, int width, int height, string format, int compressionLevel)
	{
		var info = new SkiaSharp.SKImageInfo(width, height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
		using var bitmap = new SkiaSharp.SKBitmap();
		var pin = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
		try
		{
			bitmap.InstallPixels(info, pin.AddrOfPinnedObject(), info.RowBytes);
			using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
			if (image == null) return;

			var encodedFormat = format.ToLowerInvariant() switch
			{
				"jpg" or "jpeg" => SkiaSharp.SKEncodedImageFormat.Jpeg,
				"bmp" => SkiaSharp.SKEncodedImageFormat.Bmp,
				"webp" => SkiaSharp.SKEncodedImageFormat.Webp,
				_ => SkiaSharp.SKEncodedImageFormat.Png,
			};

			int quality = 100 - (compressionLevel * 10);
			if (encodedFormat == SkiaSharp.SKEncodedImageFormat.Png)
			{
				quality = 100;
			}
			else if (encodedFormat == SkiaSharp.SKEncodedImageFormat.Webp)
			{
				// Map 0-9 compression level to 100-73 quality for WebP lossy (perfect at 32x32)
				quality = 100 - (compressionLevel * 3);
			}
			else if (encodedFormat == SkiaSharp.SKEncodedImageFormat.Jpeg)
			{
				if (quality < 10) quality = 10;
			}

			using var data = image.Encode(encodedFormat, quality);
			if (data == null) return;

			using var stream = File.Create(outputPath);
			data.SaveTo(stream);
		}
		finally
		{
			pin.Free();
		}
	}

	public void Dispose()
	{
		_cts?.Dispose();
	}
}
