using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using NyxAssets.Utils;
using NyxAssetsEditor.Services.DragDrop;
using NyxAssetsEditor.Services.Archive;
using NyxAssetsEditor.Services.ImportExport;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;
using NyxAssetsEditor.ViewModels.Common;
using NyxAssetsEditor.ViewModels.Sprites;
using NyxAssetsEditor.ViewModels.Pages;
using NyxAssetsEditor.Views.Pages;
using NyxAssetsEditor.Views.Shell;

namespace NyxAssetsEditor.Views.ArchiveLoaders
{
	public partial class FloatingSpriteLoaderControl : UserControl
	{
		private FloatingSpriteLoaderViewModel? _viewModel;
		private SpriteViewModel? _dragSprite;
		private Point _spriteDragStart;
		private bool _spriteDragStarted;
		private PointerPressedEventArgs? _spriteDragPressEvent;
		private const double DragThreshold = 8.0;

		public FloatingSpriteLoaderControl()
		{
			InitializeComponent();

			PointerMoved += OnSpriteDragPointerMoved;
			PointerReleased += OnSpriteDragPointerReleased;
			
			var titleBar = this.FindControl<Border>("TitleBar");
			var bottomBar = this.FindControl<Border>("BottomBar");
			if (titleBar != null)
			{
				var interaction = new FloatingPanelInteraction(this, titleBar, bottomBar, minWidth: 340, minHeight: 150);
				RegisterResizeHandle(interaction, "ResizeLeft", 4);
				RegisterResizeHandle(interaction, "ResizeRight", 1);
				RegisterResizeHandle(interaction, "ResizeBottom", 2);
				RegisterResizeHandle(interaction, "ResizeCorner", 3);
				RegisterResizeHandle(interaction, "ResizeBottomLeft", 5);
				RegisterResizeHandle(interaction, "ResizeTop", 6);
				RegisterResizeHandle(interaction, "ResizeTopRight", 7);
				RegisterResizeHandle(interaction, "ResizeTopLeft", 8);
			}

			this.DataContextChanged += (s, e) =>
			{
				if (_viewModel != null)
				{
					_viewModel.RequestSaveAs -= OnSaveAsRequested;
					_viewModel.RequestSpriteFileDialog -= OnSpriteFileDialogRequested;
					_viewModel.ScrollToItemRequested -= OnScrollToItemRequested;
					_viewModel.RequestSpritesOptimizer -= OnSpritesOptimizerRequested;
					_viewModel.RequestShowInfo -= OnShowInfoRequested;
				}
				_viewModel = DataContext as FloatingSpriteLoaderViewModel;
				if (_viewModel != null)
				{
					_viewModel.RequestSaveAs += OnSaveAsRequested;
					_viewModel.RequestSpriteFileDialog += OnSpriteFileDialogRequested;
					_viewModel.ScrollToItemRequested += OnScrollToItemRequested;
					_viewModel.RequestSpritesOptimizer += OnSpritesOptimizerRequested;
					_viewModel.RequestShowInfo += OnShowInfoRequested;
				}
			};
		}

		private async void OnSpritesOptimizerRequested(object? sender, EventArgs e)
		{
			if (_viewModel == null) return;
			var topLevel = TopLevel.GetTopLevel(this);
			Window? window = topLevel as Window;
			if (window == null)
			{
				window = this.VisualRoot as Window;
			}
			if (window == null) return;

			var dialog = new SpritesOptimizerDialog(_viewModel);
			await dialog.ShowDialog(window);
		}

		private async void OnShowInfoRequested(object? sender, string message)
		{
			var window = TopLevel.GetTopLevel(this) as Window ?? this.VisualRoot as Window;
			if (window == null) return;
			await new InfoDialog("Sprite Archive Info", message).ShowDialog(window);
		}

		private void RegisterResizeHandle(FloatingPanelInteraction interaction, string name, int direction)
		{
			var handle = this.FindControl<Border>(name);
			if (handle != null)
				interaction.RegisterResizeHandle(handle, direction);
		}

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			Focus();
		}

		private void OnScrollToItemRequested(object item)
		{
			var listBox = _viewModel?.IsGridView == true ? SpriteGridListBox : SpriteListListBox;
			if (listBox == null || !listBox.IsVisible)
				return;

			Dispatcher.UIThread.Post(() => listBox.ScrollIntoView(item), DispatcherPriority.Loaded);
		}

		private void OnSpritePointerPressed(object? sender, PointerPressedEventArgs e)
		{
			if (sender is not Control control || control.DataContext is not SpriteViewModel sprite)
				return;

			if (DataContext is FloatingSpriteLoaderViewModel vm)
			{
				var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
				var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
				vm.SelectSprite(sprite, shift, ctrl);
			}

			if (e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
			{
				_dragSprite = sprite;
				_spriteDragStart = e.GetPosition(this);
				_spriteDragStarted = false;
				_spriteDragPressEvent = e;
				e.Pointer.Capture(control);
				e.Handled = true;
			}

			if (e.GetCurrentPoint(control).Properties.IsRightButtonPressed)
				e.Handled = true;
		}

		private async void OnSpriteDragPointerMoved(object? sender, PointerEventArgs e)
		{
			if (_dragSprite == null || _viewModel == null || _spriteDragPressEvent == null)
				return;

			if (_spriteDragStarted)
				return;

			var pos = e.GetPosition(this);
			var dx = pos.X - _spriteDragStart.X;
			var dy = pos.Y - _spriteDragStart.Y;
			if (Math.Sqrt(dx * dx + dy * dy) < DragThreshold)
				return;

			_spriteDragStarted = true;
			e.Pointer.Capture(null);

			var data = SpriteDragContext.CreateDrag(_viewModel, _dragSprite.Id);
			await DragDrop.DoDragDropAsync(_spriteDragPressEvent, data, DragDropEffects.Copy);
			SpriteDragContext.CurrentDrag = null;

			_dragSprite = null;
			_spriteDragPressEvent = null;
			_spriteDragStarted = false;
		}

		private void OnSpriteDragPointerReleased(object? sender, PointerReleasedEventArgs e)
		{
			if (_spriteDragStarted)
				return;

			e.Pointer.Capture(null);
			_dragSprite = null;
			_spriteDragPressEvent = null;
		}

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);

			if (DataContext is FloatingSpriteLoaderViewModel vm)
			{
				if (vm.IsDefaultPosition)
				{
					var canvasVisual = GetParentCanvas();
					if (canvasVisual != null)
					{
						void CenterPanel()
						{
							double canvasWidth = canvasVisual.Bounds.Width;
							double canvasHeight = canvasVisual.Bounds.Height;
							if (canvasWidth > 0 && canvasHeight > 0)
							{
								vm.PositionX = (canvasWidth - vm.PanelWidth) / 2;
								vm.PositionY = (canvasHeight - vm.ContentHeight) / 2;
								vm.IsDefaultPosition = false;
							}
						}

						if (canvasVisual.Bounds.Width > 0 && canvasVisual.Bounds.Height > 0)
						{
							CenterPanel();
						}
						else
						{
							canvasVisual.SizeChanged += OnCanvasSizeChanged;
							void OnCanvasSizeChanged(object? sender, SizeChangedEventArgs args)
							{
								if (args.NewSize.Width > 0 && args.NewSize.Height > 0)
								{
									canvasVisual.SizeChanged -= OnCanvasSizeChanged;
									CenterPanel();
								}
							}
						}
					}
				}
			}
		}

		private Canvas? GetParentCanvas()
		{
			Visual? canvasVisual = this;
			while (canvasVisual != null && canvasVisual is not Canvas)
			{
				canvasVisual = canvasVisual.GetVisualParent();
			}
			return canvasVisual as Canvas;
		}

		public async void OnEmptyStateClick(object? sender, PointerPressedEventArgs e)
		{
			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is FloatingSpriteLoaderViewModel vm)
			{
				var topLevel = TopLevel.GetTopLevel(this);
				if (topLevel == null) return;

				var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
				{
					Title = "Open Nyx Sprite or Asset Archive",
					AllowMultiple = false,
					FileTypeFilter = FilePickerFilters.OpenSpriteArchives
				});

				if (files != null && files.Count > 0)
				{
					var filePath = files[0].Path.LocalPath;
					await vm.LoadArchiveAsync(filePath);
				}
				
				e.Handled = true;
			}
		}

		private async void OnSaveAsRequested(object? sender, EventArgs e)
		{
			if (DataContext is FloatingSpriteLoaderViewModel vm)
			{
				var topLevel = TopLevel.GetTopLevel(this);
				if (topLevel != null)
				{
					var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
					{
						Title = "Save Archive As",
						DefaultExtension = System.IO.Path.GetExtension(vm.FilePath),
						SuggestedFileName = System.IO.Path.GetFileName(vm.FilePath),
						FileTypeChoices = FilePickerFilters.OpenSpriteArchives
					});

					if (file != null)
					{
						try
						{
							System.IO.File.Copy(vm.FilePath, file.Path.LocalPath, true);
							vm.FilePath = file.Path.LocalPath;
						}
						catch (Exception ex)
						{
							System.Diagnostics.Debug.WriteLine($"Failed to save as: {ex.Message}");
						}
					}
				}
			}
		}

		private async void OnSpriteFileDialogRequested(object? sender, SpriteFileRequestEventArgs e)
		{
			if (DataContext is not FloatingSpriteLoaderViewModel vm)
				return;

			var topLevel = TopLevel.GetTopLevel(this);
			var window = topLevel as Window ?? this.VisualRoot as Window;
			if (window == null)
				return;

			if (e.Format == "replace")
			{
				if (e.Sprites.Count != 1)
					return;
				var target = e.Sprite;
				var dialog = new SingleAssetReplaceDialog(
					$"Replace sprite #{target.Id}",
					"Drop an image file here",
					FilePickerFilters.OpenImages,
					SupportedFileFormats.ImageExtensions,
					path =>
					{
						try
						{
							var rgba = SpriteImageImporter.Load32x32Rgba(path);
							vm.ApplyReplacementPixels(new Dictionary<uint, byte[]> { [target.Id] = rgba });
							return null;
						}
						catch (Exception ex)
						{
							return $"Failed to replace the sprite: {ex.Message}";
						}
					});
				await dialog.ShowDialog<bool>(window);
				return;
			}

			if (e.Format == "export_popup")
			{
				string defaultName = "sprite";
				var dialog = new AssetExportDialog(defaultName, showThingsFormats: false);
				await dialog.ShowDialog(window);
				if (dialog.IsConfirmed)
				{
					PerformSpriteExport(vm, e.Sprites, dialog.ExportName, dialog.ExportPath, dialog.ExportFormat);
				}
				return;
			}

			if (e.Format == "append")
			{
				var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
				{
					Title = "Import Images as New Sprites",
					AllowMultiple = true,
					FileTypeFilter = FilePickerFilters.OpenImages
				});

				if (files == null || files.Count == 0)
					return;

				try
				{
					vm.ImportFiles(files.Select(f => f.Path.LocalPath));
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Failed to import sprites: {ex.Message}");
				}

				return;
			}

			if (string.IsNullOrEmpty(e.Format))
			{
				var title = e.Sprites.Count == 1
					? $"Import Sprite #{e.Sprite.Id}"
					: $"Import Image to {e.Sprites.Count} Sprites";

				var file = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
				{
					Title = title,
					AllowMultiple = false,
					FileTypeFilter = FilePickerFilters.OpenImages
				});

				if (file == null || file.Count == 0)
					return;

				try
				{
					var path = file[0].Path.LocalPath;
					var rgba = SpriteImageImporter.Load32x32Rgba(path);
					vm.ReplaceSpritePixels(e.Sprites, rgba);
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Failed to import sprite image: {ex.Message}");
				}

				return;
			}

			var format = e.Format.ToLowerInvariant();
			var extension = SupportedFileFormats.NormalizeImageExportExtension(format);

			if (e.Sprites.Count == 1)
			{
				var saveFile = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
				{
					Title = FilePickerFilters.ImageExportTitle("Sprite", format),
					DefaultExtension = extension,
					SuggestedFileName = $"sprite_{e.Sprite.Id}{extension}",
					FileTypeChoices = FilePickerFilters.ForImageExport(format)
				});

				if (saveFile == null)
					return;

				try
				{
					WriteSpriteExport(e.Sprite.GetPixels(), saveFile.Path.LocalPath, format);
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Failed to export sprite: {ex.Message}");
				}

				return;
			}

			var folder = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
			{
				Title = $"Export {e.Sprites.Count} Sprites as {extension.ToUpperInvariant().TrimStart('.')}",
				AllowMultiple = false
			});

			if (folder == null || folder.Count == 0)
				return;

			var folderPath = folder[0].Path.LocalPath;
			try
			{
				foreach (var sprite in e.Sprites)
				{
					var outputPath = Path.Combine(folderPath, $"sprite_{sprite.Id}{extension}");
					WriteSpriteExport(sprite.GetPixels(), outputPath, format);
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to export sprites: {ex.Message}");
			}
		}

		private static void WriteSpriteExport(byte[] pixels, string outputPath, string format)
		{
			try
			{
				var edge = NyxAssets.Sprites.SpritePixelCodec.SpriteEdgeLength;
				var info = new SkiaSharp.SKImageInfo(edge, edge, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
				using var bitmap = new SkiaSharp.SKBitmap();
				var pin = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
				try
				{
					bitmap.InstallPixels(info, pin.AddrOfPinnedObject(), info.RowBytes);

					SkiaSharp.SKEncodedImageFormat encodedFormat = format.ToLowerInvariant() switch
					{
						"jpg" or "jpeg" => SkiaSharp.SKEncodedImageFormat.Jpeg,
						"bmp" => SkiaSharp.SKEncodedImageFormat.Bmp,
						_ => SkiaSharp.SKEncodedImageFormat.Png,
					};

					if (encodedFormat == SkiaSharp.SKEncodedImageFormat.Bmp)
					{
						using var stream = File.Create(outputPath);
						NyxAssets.Utils.SpriteImageExporter.WriteOpaque24BitBmp(pixels, (int)edge, (int)edge, stream);
					}
					else
					{
						using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
						if (image == null) return;
						using var data = image.Encode(encodedFormat, 100);
						if (data == null) return;
						using var stream = File.Create(outputPath);
						data.SaveTo(stream);
					}
				}
				finally
				{
					pin.Free();
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to write sprite image: {ex.Message}");
			}
		}

		private void OnDragOver(object? sender, DragEventArgs e)
		{
			if (_viewModel != null && _viewModel.IsArchiveLoaded && e.DataTransfer.Contains(DataFormat.File))
			{
				e.DragEffects = DragDropEffects.Copy;
				e.Handled = true;
			}
			else
			{
				e.DragEffects = DragDropEffects.None;
			}
		}

		private void OnDrop(object? sender, DragEventArgs e)
		{
			if (_viewModel == null || !_viewModel.IsArchiveLoaded)
				return;

			var files = e.DataTransfer.TryGetFiles();
			if (files != null)
			{
				var paths = new List<string>();
				foreach (var file in files)
				{
					var path = file.TryGetLocalPath();
					if (path != null && SpriteImageImporter.IsSupportedImage(path))
					{
						paths.Add(path);
					}
				}

				if (paths.Count > 0)
				{
					_viewModel.ImportFiles(paths);
					e.DragEffects = DragDropEffects.Copy;
					e.Handled = true;
				}
			}
		}

		private static void PerformSpriteExport(
			FloatingSpriteLoaderViewModel vm,
			IReadOnlyList<SpriteViewModel> sprites,
			string name,
			string folderPath,
			string format)
		{
			if (sprites.Count == 0)
				return;

			var formatLower = format.ToLowerInvariant();
			var extension = SupportedFileFormats.NormalizeImageExportExtension(formatLower);

			try
			{
				if (!Directory.Exists(folderPath))
				{
					Directory.CreateDirectory(folderPath);
				}

				if (sprites.Count == 1)
				{
					var sprite = sprites[0];
					var outputPath = Path.Combine(folderPath, $"{name}_{sprite.Id}{extension}");
					WriteSpriteExport(sprite.GetPixels(), outputPath, formatLower);
				}
				else
				{
					foreach (var sprite in sprites)
					{
						var outputPath = Path.Combine(folderPath, $"{name}_{sprite.Id}{extension}");
						WriteSpriteExport(sprite.GetPixels(), outputPath, formatLower);
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to export sprites: {ex.Message}");
			}
		}
	}
}
