using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using System;
using System.IO;
using System.Collections.Generic;
using NyxAssets.Utils;
using NyxAssetsEditor.Services.DragDrop;
using NyxAssetsEditor.Services.Archive;
using NyxAssetsEditor.Services.ImportExport;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;
using NyxAssetsEditor.ViewModels.Sprites;
using NyxAssetsEditor.ViewModels.Pages;
using NyxAssetsEditor.Views.Pages;

namespace NyxAssetsEditor.Views.ArchiveLoaders
{
	public partial class FloatingSpriteLoaderControl : UserControl
	{
		private bool _isDragging;
		private Point _clickPosition;
		private Point _dragStartPosition;
		private bool _dragThresholdMet;
		private bool _isResizing;
		private int _resizeDirection; // 1 = Right, 2 = Bottom, 3 = Corner, 4 = Left
		private Point _initialPointerPosition;
		private double _initialWidth;
		private double _initialHeight;
		private double _initialPositionX;
		private FloatingSpriteLoaderViewModel? _viewModel;
		private IPointer? _activePointer;
		private static IPointer? _sharedActivePointer;
		private const double DragThreshold = 8.0;
		private SpriteViewModel? _dragSprite;
		private Point _spriteDragStart;
		private bool _spriteDragStarted;
		private PointerPressedEventArgs? _spriteDragPressEvent;

		public FloatingSpriteLoaderControl()
		{
			InitializeComponent();

			PointerMoved += OnSpriteDragPointerMoved;
			PointerReleased += OnSpriteDragPointerReleased;
			
			var titleBar = this.FindControl<Border>("TitleBar");
			if (titleBar != null)
			{
				titleBar.PointerPressed += OnTitleBarPointerPressed;
				titleBar.PointerMoved += OnTitleBarPointerMoved;
				titleBar.PointerReleased += OnTitleBarPointerReleased;
			}

			this.DataContextChanged += (s, e) =>
			{
				if (_viewModel != null)
				{
					_viewModel.RequestSaveAs -= OnSaveAsRequested;
					_viewModel.RequestSpriteFileDialog -= OnSpriteFileDialogRequested;
					_viewModel.ScrollToItemRequested -= OnScrollToItemRequested;
				}
				_viewModel = DataContext as FloatingSpriteLoaderViewModel;
				if (_viewModel != null)
				{
					_viewModel.RequestSaveAs += OnSaveAsRequested;
					_viewModel.RequestSpriteFileDialog += OnSpriteFileDialogRequested;
					_viewModel.ScrollToItemRequested += OnScrollToItemRequested;
				}
			};
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

				if (vm.IsDraggingVM && _sharedActivePointer != null)
				{
					_isDragging = true;
					_activePointer = _sharedActivePointer;
					_clickPosition = new Point(vm.DragClickX, vm.DragClickY);

					var titleBar = this.FindControl<Border>("TitleBar");
					if (titleBar != null)
					{
						_activePointer.Capture(titleBar);
					}
				}
			}
		}

		private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
		{
			if (e.Source is Visual visual)
			{
				var current = visual;
				while (current != null && current != this)
				{
					if (current is Button || current is ComboBox || current is TextBox)
					{
						return;
					}
					current = current.GetVisualParent();
				}
			}

			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is FloatingSpriteLoaderViewModel vm)
			{
				_isDragging = true;
				_dragThresholdMet = false;
				_activePointer = e.Pointer;
				_sharedActivePointer = e.Pointer;
				_clickPosition = e.GetPosition(this);
				_dragStartPosition = e.GetPosition(this);

				vm.IsDraggingVM = true;
				vm.DragClickX = _clickPosition.X;
				vm.DragClickY = _clickPosition.Y;

				e.Pointer.Capture(sender as IInputElement);
				e.Handled = true;
			}
		}

		private void OnTitleBarPointerMoved(object? sender, PointerEventArgs e)
		{
			if (!_isDragging || DataContext is not FloatingSpriteLoaderViewModel vm) return;

			var assetsView = this.FindAncestorOfType<AssetsView>();
			var parentVm = assetsView?.DataContext as AssetsViewModel;

			// Check drag threshold before committing any undock
			if (!_dragThresholdMet)
			{
				var curPos = e.GetPosition(this);
				var dx = curPos.X - _dragStartPosition.X;
				var dy = curPos.Y - _dragStartPosition.Y;
				if (Math.Sqrt(dx * dx + dy * dy) < DragThreshold) return;
				_dragThresholdMet = true;
				if (parentVm != null) parentVm.IsDraggingPanel = true;
			}

			// If still docked, undock now (threshold already met)
			if (!vm.IsFloating)
			{
				vm.DockState = "Floating";
				if (assetsView != null)
				{
					var pos = e.GetPosition(assetsView);
					vm.PositionX = pos.X - _clickPosition.X;
					vm.PositionY = pos.Y - _clickPosition.Y;
				}
				e.Handled = true;
				return;
			}

			// Move freely on canvas
			Visual? canvasVisual = GetParentCanvas();
			if (canvasVisual != null)
			{
				var currentPosition = e.GetPosition(canvasVisual);
				vm.PositionX = currentPosition.X - _clickPosition.X;
				vm.PositionY = currentPosition.Y - _clickPosition.Y;
			}

			// Highlight the drop target the cursor is currently over
			if (parentVm != null && assetsView != null)
			{
				var cursorInView = e.GetPosition(assetsView);
				parentVm.DragOverZone = GetDropZoneFromPoint(assetsView, cursorInView);
			}

			e.Handled = true;
		}

		private void OnTitleBarPointerReleased(object? sender, PointerReleasedEventArgs e)
		{
			if (!_isDragging || DataContext is not FloatingSpriteLoaderViewModel vm) return;

			_isDragging = false;
			_dragThresholdMet = false;
			_activePointer = null;
			_sharedActivePointer = null;
			vm.IsDraggingVM = false;
			e.Pointer.Capture(null);
			e.Handled = true;

			var assetsView = this.FindAncestorOfType<AssetsView>();
			if (assetsView != null && assetsView.DataContext is AssetsViewModel parentVm)
			{
				// Dock only if released over an explicit drop target
				var cursorInView = e.GetPosition(assetsView);
				var hitZone = GetDropZoneFromPoint(assetsView, cursorInView);
				if (hitZone != null)
				{
					vm.DockState = hitZone;
				}
				parentVm.DragOverZone = null;
				parentVm.IsDraggingPanel = false;
				parentVm.TriggerSaveAppState();
			}
		}

		/// <summary>Checks if a point (in AssetsView coords) is within any named drop-target border.</summary>
		private static string? GetDropZoneFromPoint(AssetsView assetsView, Point cursorInView)
		{
			var targets = new (string name, string zone)[]
			{
				("LeftDropTarget",   "Left"),
				("CenterDropTarget", "Center"),
				("RightDropTarget",  "Right"),
			};
			foreach (var (name, zone) in targets)
			{
				var border = assetsView.FindControl<Border>(name);
				if (border == null || !border.IsVisible) continue;
				// Translate the border's top-left corner into assetsView coordinate space
				var topLeft = border.TranslatePoint(new Point(0, 0), assetsView);
				if (topLeft == null) continue;
				var rect = new Rect(topLeft.Value, border.Bounds.Size);
				if (rect.Contains(cursorInView)) return zone;
			}
			return null;
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
					FileTypeFilter = new[]
					{
						new FilePickerFileType("All Supported Archives") { Patterns = new[] { "*.spr", "*.assets" } },
						new FilePickerFileType("Nyx Sprite Archive") { Patterns = new[] { "*.spr" } },
						new FilePickerFileType("Nyx Asset Archive") { Patterns = new[] { "*.assets" } }
					}
				});

				if (files != null && files.Count > 0)
				{
					var filePath = files[0].Path.LocalPath;
					await vm.LoadArchiveAsync(filePath);
				}
				
				e.Handled = true;
			}
		}

		private void OnResizeLeftPressed(object? sender, PointerPressedEventArgs e)
		{
			StartResizing(sender, e, 4);
		}

		private void OnResizeRightPressed(object? sender, PointerPressedEventArgs e)
		{
			StartResizing(sender, e, 1);
		}

		private void OnResizeBottomPressed(object? sender, PointerPressedEventArgs e)
		{
			StartResizing(sender, e, 2);
		}

		private void OnResizeCornerPressed(object? sender, PointerPressedEventArgs e)
		{
			StartResizing(sender, e, 3);
		}

		private void OnResizeBottomLeftPressed(object? sender, PointerPressedEventArgs e)
		{
			StartResizing(sender, e, 5);
		}

		private void StartResizing(object? sender, PointerPressedEventArgs e, int direction)
		{
			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is FloatingSpriteLoaderViewModel vm && vm.IsFloating)
			{
				_isResizing = true;
				_resizeDirection = direction;
				
				var canvasVisual = GetParentCanvas();
				if (canvasVisual != null)
				{
					_initialPointerPosition = e.GetPosition(canvasVisual);
					_initialWidth = vm.PanelWidth;
					_initialHeight = vm.ContentHeight;
					_initialPositionX = vm.PositionX;
					e.Pointer.Capture(sender as IInputElement);
					e.Handled = true;
				}
			}
		}

		private void OnResizeLeftMoved(object? sender, PointerEventArgs e)
		{
			PerformResizing(e);
		}

		private void OnResizeRightMoved(object? sender, PointerEventArgs e)
		{
			PerformResizing(e);
		}

		private void OnResizeBottomMoved(object? sender, PointerEventArgs e)
		{
			PerformResizing(e);
		}

		private void OnResizeCornerMoved(object? sender, PointerEventArgs e)
		{
			PerformResizing(e);
		}

		private void OnResizeBottomLeftMoved(object? sender, PointerEventArgs e)
		{
			PerformResizing(e);
		}

		private void PerformResizing(PointerEventArgs e)
		{
			if (_isResizing && DataContext is FloatingSpriteLoaderViewModel vm)
			{
				var canvasVisual = GetParentCanvas();
				if (canvasVisual != null)
				{
					var currentPos = e.GetPosition(canvasVisual);
					var dx = currentPos.X - _initialPointerPosition.X;
					var dy = currentPos.Y - _initialPointerPosition.Y;

					if (_resizeDirection == 1 || _resizeDirection == 3)
					{
						vm.PanelWidth = Math.Max(340, _initialWidth + dx);
					}
					
					if (_resizeDirection == 4 || _resizeDirection == 5)
					{
						double newWidth = Math.Max(340, _initialWidth - dx);
						double widthDiff = newWidth - _initialWidth;
						vm.PanelWidth = newWidth;
						vm.PositionX = _initialPositionX - widthDiff;
					}
					
					if (_resizeDirection == 2 || _resizeDirection == 3 || _resizeDirection == 5)
					{
						vm.ContentHeight = Math.Max(150, _initialHeight + dy);
					}
					e.Handled = true;
				}
			}
		}

		private void OnResizeReleased(object? sender, PointerReleasedEventArgs e)
		{
			if (_isResizing)
			{
				_isResizing = false;
				e.Pointer.Capture(null);
				e.Handled = true;

				var assetsView = this.FindAncestorOfType<AssetsView>();
				if (assetsView != null && assetsView.DataContext is AssetsViewModel parentVm)
				{
					parentVm.TriggerSaveAppState();
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
						FileTypeChoices = new[]
						{
							new FilePickerFileType("All Supported Archives") { Patterns = new[] { "*.spr", "*.assets" } },
							new FilePickerFileType("Nyx Sprite Archive") { Patterns = new[] { "*.spr" } },
							new FilePickerFileType("Nyx Asset Archive") { Patterns = new[] { "*.assets" } }
						}
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
			if (topLevel == null)
				return;

			if (string.IsNullOrEmpty(e.Format))
			{
				var title = e.Sprites.Count == 1
					? $"Import Sprite #{e.Sprite.Id}"
					: $"Import Image to {e.Sprites.Count} Sprites";

				var file = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
				{
					Title = title,
					AllowMultiple = false,
					FileTypeFilter = new[]
					{
						new FilePickerFileType("Image Files") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp", "*.tga" } }
					}
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
			var extension = format is "jpg" or "jpeg" ? ".jpg" : format == "bmp" ? ".bmp" : ".png";

			if (e.Sprites.Count == 1)
			{
				var (fileTypeChoices, title) = format switch
				{
					"jpg" or "jpeg" => (new[] { new FilePickerFileType("JPEG Image") { Patterns = new[] { "*.jpg", "*.jpeg" } } }, "Export Sprite as JPEG"),
					"bmp" => (new[] { new FilePickerFileType("BMP Image") { Patterns = new[] { "*.bmp" } } }, "Export Sprite as BMP"),
					_ => (new[] { new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } } }, "Export Sprite as PNG"),
				};

				var saveFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
				{
					Title = title,
					DefaultExtension = extension,
					SuggestedFileName = $"sprite_{e.Sprite.Id}{extension}",
					FileTypeChoices = fileTypeChoices
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

			var folder = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
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
					using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
					if (image == null) return;

					SkiaSharp.SKEncodedImageFormat encodedFormat = format.ToLowerInvariant() switch
					{
						"jpg" or "jpeg" => SkiaSharp.SKEncodedImageFormat.Jpeg,
						"bmp" => SkiaSharp.SKEncodedImageFormat.Bmp,
						_ => SkiaSharp.SKEncodedImageFormat.Png,
					};

					using var data = image.Encode(encodedFormat, 100);
					if (data == null) return;

					using var stream = File.OpenWrite(outputPath);
					data.SaveTo(stream);
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
	}
}
