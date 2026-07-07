using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NyxAssetsEditor.Services.Archive;
using NyxAssetsEditor.Services.Exchange;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;
using NyxAssetsEditor.ViewModels.Pages;
using NyxAssetsEditor.Views.Pages;
using NyxAssetsEditor.ViewModels.Things;
using NyxAssets.Things;
using NyxAssets.Things.Exchange;
using NyxAssets.Utils;

namespace NyxAssetsEditor.Views.ArchiveLoaders
{
	public partial class FloatingThingsLoaderControl : UserControl
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
		private IPointer? _activePointer;
		private static IPointer? _sharedActivePointer;
		private const double DragThreshold = 8.0;
		private FloatingThingsLoaderViewModel? _viewModel;

		public FloatingThingsLoaderControl()
		{
			InitializeComponent();
			
			var titleBar = this.FindControl<Border>("TitleBar");
			if (titleBar != null)
			{
				titleBar.PointerPressed += OnTitleBarPointerPressed;
				titleBar.PointerMoved += OnTitleBarPointerMoved;
				titleBar.PointerReleased += OnTitleBarPointerReleased;
			}

			DataContextChanged += (_, _) =>
			{
				if (_viewModel != null)
				{
					_viewModel.RequestThingFileDialog -= OnThingFileDialogRequested;
					_viewModel.ScrollToItemRequested -= OnScrollToItemRequested;
				}

				_viewModel = DataContext as FloatingThingsLoaderViewModel;
				if (_viewModel != null)
				{
					_viewModel.RequestThingFileDialog += OnThingFileDialogRequested;
					_viewModel.ScrollToItemRequested += OnScrollToItemRequested;
				}
			};
		}

		private void OnScrollToItemRequested(object item)
		{
			var listBox = _viewModel?.IsGridView == true ? ThingGridListBox : ThingListListBox;
			if (listBox == null || !listBox.IsVisible)
				return;

			Dispatcher.UIThread.Post(() => listBox.ScrollIntoView(item), DispatcherPriority.Loaded);
		}

		private async void OnThingPointerPressed(object? sender, PointerPressedEventArgs e)
		{
			if (sender is not Control control || control.DataContext is not ThingItemViewModel thing)
				return;

			if (DataContext is FloatingThingsLoaderViewModel vm)
			{
				if (e.GetCurrentPoint(control).Properties.IsRightButtonPressed)
				{
					e.Handled = true;
					return;
				}

				if (e.ClickCount >= 2)
				{
					await vm.OpenThingEditor(thing);
					e.Handled = true;
					return;
				}

				var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
				var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
				await vm.RequestSelectThing(thing, shift, ctrl);

				if (e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
					e.Handled = true;
			}

			if (e.GetCurrentPoint(control).Properties.IsRightButtonPressed)
				e.Handled = true;
		}

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);

			if (DataContext is FloatingThingsLoaderViewModel vm)
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

			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is FloatingThingsLoaderViewModel vm)
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
			if (!_isDragging || DataContext is not FloatingThingsLoaderViewModel vm) return;

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

			// Highlight the drop target the cursor is currently over (hittest)
			if (parentVm != null && assetsView != null)
			{
				var cursorInView = e.GetPosition(assetsView);
				parentVm.DragOverZone = GetDropZoneFromPoint(assetsView, cursorInView);
			}

			e.Handled = true;
		}

		private void OnTitleBarPointerReleased(object? sender, PointerReleasedEventArgs e)
		{
			if (!_isDragging || DataContext is not FloatingThingsLoaderViewModel vm) return;

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
				var topLeft = border.TranslatePoint(new Point(0, 0), assetsView);
				if (topLeft == null) continue;
				var rect = new Rect(topLeft.Value, border.Bounds.Size);
				if (rect.Contains(cursorInView)) return zone;
			}
			return null;
		}

		public async void OnEmptyStateClick(object? sender, PointerPressedEventArgs e)
		{
			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is FloatingThingsLoaderViewModel vm)
			{
				var topLevel = TopLevel.GetTopLevel(this);
				if (topLevel == null) return;

				var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
				{
					Title = "Open Nyx Things or Dat Archive",
					AllowMultiple = false,
					FileTypeFilter = new[]
					{
						new FilePickerFileType("Nyx Dat Archive") { Patterns = new[] { "*.dat" } },
						new FilePickerFileType("Nyx Things Archive") { Patterns = new[] { "*.things" } },
						new FilePickerFileType("All Supported Archives") { Patterns = new[] { "*.dat", "*.things" } }
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
			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is FloatingThingsLoaderViewModel vm && vm.IsFloating)
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
			if (_isResizing && DataContext is FloatingThingsLoaderViewModel vm)
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

		private async void OnThingFileDialogRequested(object? sender, ThingFileRequestEventArgs e)
		{
			if (DataContext is not FloatingThingsLoaderViewModel vm)
				return;

			var topLevel = TopLevel.GetTopLevel(this);
			if (topLevel == null)
				return;

			var format = e.Format.ToLowerInvariant();
			switch (format)
			{
				case "import":
					await HandleThingImport(vm, topLevel, replace: false, e.Things);
					break;
				case "replace":
					await HandleThingImport(vm, topLevel, replace: true, e.Things);
					break;
				case "nyx-thing":
				case "obd":
					await HandleThingPortableExport(vm, topLevel, e.Things, format);
					break;
				default:
					await HandleThingSpritesheetExport(vm, topLevel, e, format);
					break;
			}
		}

		private static readonly FilePickerFileType[] ThingExchangeFileTypes =
		{
			new FilePickerFileType("Nyx Thing JSON") { Patterns = new[] { "*.json" } },
			new FilePickerFileType("Object Builder OBD") { Patterns = new[] { "*.obd" } },
			new FilePickerFileType("All Supported") { Patterns = new[] { "*.json", "*.obd" } },
		};

		private static async Task HandleThingImport(
			FloatingThingsLoaderViewModel vm,
			TopLevel topLevel,
			bool replace,
			IReadOnlyList<ThingItemViewModel> targets)
		{
			if (vm.Catalog == null)
				return;

			if (replace && targets.Count == 0)
				return;

			var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
			{
				Title = replace ? "Replace Thing from File" : "Import Thing from File",
				AllowMultiple = false,
				FileTypeFilter = ThingExchangeFileTypes,
			});

			if (files == null || files.Count == 0)
				return;

			try
			{
				var path = files[0].Path.LocalPath;
				var document = ThingExchangeHelper.LoadFromPath(path, vm.GetWriteOptions());

				if (replace)
				{
					foreach (var target in targets)
						vm.ApplyImportedDocument(document, target.Id, replaceExisting: true);
				}
				else
				{
					var assignId = ThingExchangeHelper.GetNextAppendId(vm.Catalog, document.Thing.Kind);
					vm.ApplyImportedDocument(document, assignId, replaceExisting: false);
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to import thing: {ex.Message}");
			}
		}

		private static async Task HandleThingPortableExport(
			FloatingThingsLoaderViewModel vm,
			TopLevel topLevel,
			IReadOnlyList<ThingItemViewModel> things,
			string format)
		{
			if (things.Count == 0)
				return;

			var loader = vm.GetActiveSpriteLoader();
			if (loader == null)
			{
				System.Diagnostics.Debug.WriteLine("[ThingsLoader] Portable export requires a loaded sprite archive.");
				return;
			}

			var isObd = format == "obd";
			var extension = isObd ? ".obd" : ".json";
			var options = vm.GetWriteOptions();

			if (things.Count == 1)
			{
				var thingVm = things[0];
				var thingType = vm.GetThingType(thingVm.Id);
				if (thingType == null)
					return;

				var saveFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
				{
					Title = isObd ? "Export Thing as Object Builder OBD" : "Export Thing as nyx-thing JSON",
					DefaultExtension = extension,
					SuggestedFileName = $"thing_{thingVm.DisplayedId}{extension}",
					FileTypeChoices = isObd
						? new[] { new FilePickerFileType("Object Builder OBD") { Patterns = new[] { "*.obd" } } }
						: new[] { new FilePickerFileType("Nyx Thing JSON") { Patterns = new[] { "*.json" } } },
				});

				if (saveFile == null)
					return;

				try
				{
					var document = ThingExchangeHelper.CreatePortableDocument(thingType, loader, options);
					if (isObd)
						ThingExchangeHelper.WriteObd(saveFile.Path.LocalPath, document, options);
					else
						ThingExchangeHelper.WriteNyxThingJson(saveFile.Path.LocalPath, document);
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Failed to export thing: {ex.Message}");
				}

				return;
			}

			var folder = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
			{
				Title = isObd
					? $"Export {things.Count} Things as OBD"
					: $"Export {things.Count} Things as nyx-thing JSON",
				AllowMultiple = false,
			});

			if (folder == null || folder.Count == 0)
				return;

			var folderPath = folder[0].Path.LocalPath;
			try
			{
				foreach (var thingVm in things)
				{
					var thingType = vm.GetThingType(thingVm.Id);
					if (thingType == null)
						continue;

					var document = ThingExchangeHelper.CreatePortableDocument(thingType, loader, options);
					var outputPath = Path.Combine(folderPath, $"thing_{thingVm.DisplayedId}{extension}");
					if (isObd)
						ThingExchangeHelper.WriteObd(outputPath, document, options);
					else
						ThingExchangeHelper.WriteNyxThingJson(outputPath, document);
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to export things: {ex.Message}");
			}
		}

		private static async Task HandleThingSpritesheetExport(
			FloatingThingsLoaderViewModel vm,
			TopLevel topLevel,
			ThingFileRequestEventArgs e,
			string format)
		{
			var loader = vm.GetActiveSpriteLoader();
			if (loader == null)
			{
				System.Diagnostics.Debug.WriteLine("[ThingsLoader] Export requires a loaded sprite archive.");
				return;
			}

			var extension = format is "jpg" or "jpeg" ? ".jpg" : format == "bmp" ? ".bmp" : ".png";

			if (e.Things.Count == 1 && e.Thing != null)
			{
				var thingVm = e.Thing;
				var thingType = vm.GetThingType(thingVm.Id);
				if (thingType == null)
					return;

				var (fileTypeChoices, title) = format switch
				{
					"jpg" or "jpeg" => (new[] { new FilePickerFileType("JPEG Image") { Patterns = new[] { "*.jpg", "*.jpeg" } } }, "Export Thing Spritesheet as JPEG"),
					"bmp" => (new[] { new FilePickerFileType("BMP Image") { Patterns = new[] { "*.bmp" } } }, "Export Thing Spritesheet as BMP"),
					_ => (new[] { new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } } }, "Export Thing Spritesheet as PNG"),
				};

				var saveFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
				{
					Title = title,
					DefaultExtension = extension,
					SuggestedFileName = $"thing_{thingVm.DisplayedId}{extension}",
					FileTypeChoices = fileTypeChoices,
				});

				if (saveFile == null)
					return;

				try
				{
					WriteThingSpritesheetExport(loader, thingType, saveFile.Path.LocalPath, format);
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Failed to export thing spritesheet: {ex.Message}");
				}

				return;
			}

			var folder = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
			{
				Title = $"Export {e.Things.Count} Thing Spritesheets as {extension.ToUpperInvariant().TrimStart('.')}",
				AllowMultiple = false,
			});

			if (folder == null || folder.Count == 0)
				return;

			var folderPath = folder[0].Path.LocalPath;
			try
			{
				using var spriteSource = new SpriteLoaderSpriteSource(loader);
				foreach (var thingVm in e.Things)
				{
					var thingType = vm.GetThingType(thingVm.Id);
					if (thingType == null)
						continue;

					var outputPath = Path.Combine(folderPath, $"thing_{thingVm.DisplayedId}{extension}");
					WriteThingSpritesheetExport(spriteSource, thingType, outputPath, format);
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to export thing spritesheets: {ex.Message}");
			}
		}

		private static void WriteThingSpritesheetExport(SpriteLoader loader, NyxAssets.Things.ThingType thing, string outputPath, string format)
		{
			using var spriteSource = new SpriteLoaderSpriteSource(loader);
			WriteThingSpritesheetExport(spriteSource, thing, outputPath, format);
		}

		private static void WriteThingSpritesheetExport(SpriteLoaderSpriteSource spriteSource, NyxAssets.Things.ThingType thing, string outputPath, string format)
		{
			var ok = format switch
			{
				"jpg" or "jpeg" => ThingSpriteSheetExporter.TryWriteThingSpriteSheetJpeg(spriteSource, thing, outputPath),
				"bmp" => ThingSpriteSheetExporter.TryWriteThingSpriteSheetBmp(spriteSource, thing, outputPath),
				_ => ThingSpriteSheetExporter.TryWriteThingSpriteSheetPng(spriteSource, thing, outputPath),
			};

			if (!ok)
				throw new InvalidOperationException($"ThingSpriteSheetExporter could not write spritesheet for thing {thing.Id}.");
		}
	}
}
