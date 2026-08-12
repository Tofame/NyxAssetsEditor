using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Threading.Tasks;
using NyxAssetsEditor.Services.Archive;
using NyxAssetsEditor.Services.Exchange;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;
using NyxAssetsEditor.ViewModels.Common;
using NyxAssetsEditor.ViewModels.Pages;
using NyxAssetsEditor.Views.Pages;
using NyxAssetsEditor.Services.Replacement;
using NyxAssetsEditor.ViewModels.Things;
using NyxAssets.Things;
using NyxAssets.Things.Exchange;
using NyxAssets.Utils;
using NyxAssetsEditor.Views.Shell;

namespace NyxAssetsEditor.Views.ArchiveLoaders
{
	public partial class FloatingThingsLoaderControl : UserControl
	{
		private FloatingThingsLoaderViewModel? _viewModel;
		private bool _isMiddleAutoScrollActive;
		private Point _middleAutoScrollAnchor;
		private double _middleAutoScrollDeltaY;
		private IPointer? _middleAutoScrollPointer;
		private readonly DispatcherTimer _middleAutoScrollTimer;
		private bool _autoScrollPageTransitionPending;
		private Cursor? _previousCursor;
		private const double MiddleAutoScrollDeadZone = 8.0;
		private const double MiddleAutoScrollBaseStep = 1.5;
		private const double MiddleAutoScrollAcceleration = 0.12;
		private const double MiddleAutoScrollMaxStep = 45.0;
		private int _lastKnownPage = 1;

		public FloatingThingsLoaderControl()
		{
			InitializeComponent();
			_middleAutoScrollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, OnMiddleAutoScrollTick);
			PointerMoved += OnAutoScrollPointerMoved;
			PointerReleased += OnAutoScrollPointerReleased;
			
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

			DataContextChanged += (_, _) =>
			{
				if (_viewModel != null)
				{
					_viewModel.RequestThingFileDialog -= OnThingFileDialogRequested;
					_viewModel.ScrollToItemRequested -= OnScrollToItemRequested;
					_viewModel.RequestShowInfo -= OnShowInfoRequested;
					_viewModel.PropertyChanged -= OnViewModelPropertyChanged;
				}

				_viewModel = DataContext as FloatingThingsLoaderViewModel;
				if (_viewModel != null)
				{
					_viewModel.RequestThingFileDialog += OnThingFileDialogRequested;
					_viewModel.ScrollToItemRequested += OnScrollToItemRequested;
					_viewModel.RequestShowInfo += OnShowInfoRequested;
					_viewModel.PropertyChanged += OnViewModelPropertyChanged;
					_lastKnownPage = _viewModel.CurrentPage;
				}
			};
		}

		private async void OnShowInfoRequested(object? sender, string message)
		{
			var window = TopLevel.GetTopLevel(this) as Window ?? this.VisualRoot as Window;
			if (window == null) return;
			await new InfoDialog("Things Archive Info", message).ShowDialog(window);
		}

		private void RegisterResizeHandle(FloatingPanelInteraction interaction, string name, int direction)
		{
			var handle = this.FindControl<Border>(name);
			if (handle != null)
				interaction.RegisterResizeHandle(handle, direction);
		}

		private async void CopyThingId(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			if (sender is not MenuItem { DataContext: ThingItemViewModel thing }) return;
			var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
			if (clipboard != null) await clipboard.SetTextAsync(thing.DisplayedId.ToString());
		}

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			Focus();
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

			if (_isMiddleAutoScrollActive)
			{
				StopMiddleAutoScroll();
				e.Handled = true;
				return;
			}

			if (e.GetCurrentPoint(control).Properties.IsMiddleButtonPressed)
			{
				if (TryStartMiddleAutoScroll(e))
					e.Handled = true;
				return;
			}

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
			}
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
					FileTypeFilter = FilePickerFilters.OpenThingsArchives
				});

				if (files != null && files.Count > 0)
				{
					var filePath = files[0].Path.LocalPath;
					await vm.LoadArchiveAsync(filePath);
				}
				
				e.Handled = true;
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
			var window = topLevel as Window ?? this.VisualRoot as Window;
			if (window == null)
				return;

			var format = e.Format.ToLowerInvariant();
			switch (format)
			{
				case "import":
					await HandleThingImport(vm, window, replace: false, e.Things);
					break;
				case "replace":
					await HandleSingleThingReplacement(vm, window, e.Things);
					break;
				case "export_popup":
					{
						string defaultName = vm.SectionLabel;
						var dialog = new AssetExportDialog(defaultName, showThingsFormats: true);
						await dialog.ShowDialog(window);
						if (dialog.IsConfirmed)
						{
							PerformThingExport(vm, e.Things, dialog.ExportName, dialog.ExportPath, dialog.ExportFormat, dialog.SkipWestDirection);
						}
					}
					break;
				case SupportedFileFormats.FormatNyxThing:
				case SupportedFileFormats.FormatJson:
				case SupportedFileFormats.FormatObd:
					await HandleThingPortableExport(vm, window, e.Things, format);
					break;
				default:
					await HandleThingSpritesheetExport(vm, window, e, format);
					break;
			}
		}

		private static async Task HandleSingleThingReplacement(
			FloatingThingsLoaderViewModel vm,
			Window owner,
			IReadOnlyList<ThingItemViewModel> targets)
		{
			if (targets.Count != 1 || vm.Catalog == null || vm.ParentViewModel == null)
				return;
			var target = targets[0];
			var targetPair = vm.ParentViewModel.GetCompilePairs()
				.FirstOrDefault(pair => ReferenceEquals(pair.ThingsPanel, vm));
			if (targetPair == null)
				return;

			var dialog = new SingleAssetReplaceDialog(
				$"Replace {vm.SectionLabel} #{target.Id}",
				$"Drop a {SupportedFileFormats.ExtJson} or {SupportedFileFormats.ExtObd} file here",
				FilePickerFilters.OpenThingExchange,
				SupportedFileFormats.ThingExchangeExtensions,
				path =>
				{
					try
					{
						var document = ThingExchangeHelper.LoadFromPath(path, vm.GetWriteOptions());
						var batch = AssetReplacementService.PrepareSingleThing(document, targetPair, vm.SelectedSection, target.Id);
						if (!batch.CanApply)
							return batch.Error ?? "The replacement file could not be applied.";
						var result = AssetReplacementService.Apply(batch);
						return result.Succeeded ? null : result.Message;
					}
					catch (Exception ex)
					{
						return $"Failed to read the replacement file: {ex.Message}";
					}
				});
			await dialog.ShowDialog<bool>(owner);
		}

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
				Title = replace ? "Replace Thing from File" : "Import Things from Files",
				AllowMultiple = !replace,
				FileTypeFilter = FilePickerFilters.OpenThingExchange,
			});

			if (files == null || files.Count == 0)
				return;

			try
			{
				if (replace)
				{
					var path = files[0].Path.LocalPath;
					var document = ThingExchangeHelper.LoadFromPath(path, vm.GetWriteOptions());
					foreach (var target in targets)
						vm.ApplyImportedDocument(document, target.Id, replaceExisting: true);
				}
				else
				{
					// Sort files alphanumerically (natural sort) by filename so that order is respected
					var sortedFiles = files.OrderBy(f => 
						System.Text.RegularExpressions.Regex.Replace(f.Name ?? "", @"\d+", m => m.Value.PadLeft(10, '0'))
					).ToList();

					foreach (var file in sortedFiles)
					{
						var path = file.Path.LocalPath;
						var document = ThingExchangeHelper.LoadFromPath(path, vm.GetWriteOptions());
						var assignId = ThingExchangeHelper.GetNextAppendId(vm.Catalog, document.Thing.Kind);
						vm.ApplyImportedDocument(document, assignId, replaceExisting: false);
					}
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

			var isObd = SupportedFileFormats.IsObdFormat(format);
			var extension = isObd ? SupportedFileFormats.ExtObd : SupportedFileFormats.ExtJson;
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
						? FilePickerFilters.Only(FilePickerFilters.ThingObd)
						: FilePickerFilters.Only(FilePickerFilters.ThingsJson),
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

			var extension = SupportedFileFormats.NormalizeImageExportExtension(format);

			if (e.Things.Count == 1 && e.Thing != null)
			{
				var thingVm = e.Thing;
				var thingType = vm.GetThingType(thingVm.Id);
				if (thingType == null)
					return;

				var saveFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
				{
					Title = FilePickerFilters.ImageExportTitle("Thing Spritesheet", format),
					DefaultExtension = extension,
					SuggestedFileName = $"thing_{thingVm.DisplayedId}{extension}",
					FileTypeChoices = FilePickerFilters.ForImageExport(format),
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

		private static void WriteThingSpritesheetExport(SpriteLoader loader, NyxAssets.Things.ThingType thing, string outputPath, string format, bool skipWest = false)
		{
			using var spriteSource = new SpriteLoaderSpriteSource(loader);
			WriteThingSpritesheetExport(spriteSource, thing, outputPath, format, skipWest);
		}

		private static void WriteThingSpritesheetExport(SpriteLoaderSpriteSource spriteSource, NyxAssets.Things.ThingType thing, string outputPath, string format, bool skipWest = false)
		{
			var ok = format switch
			{
				"jpg" or "jpeg" => NyxAssetsEditor.Services.ImportExport.ThingSpriteSheetExporterCustom.TryWriteThingSpriteSheetJpeg(spriteSource, thing, outputPath, skipWest: skipWest),
				"bmp" => NyxAssetsEditor.Services.ImportExport.ThingSpriteSheetExporterCustom.TryWriteThingSpriteSheetBmp(spriteSource, thing, outputPath, skipWest: skipWest),
				_ => NyxAssetsEditor.Services.ImportExport.ThingSpriteSheetExporterCustom.TryWriteThingSpriteSheetPng(spriteSource, thing, outputPath, skipWest: skipWest),
			};

			if (!ok)
				throw new InvalidOperationException($"ThingSpriteSheetExporter could not write spritesheet for thing {thing.Id}.");
		}

		private static void PerformThingExport(
			FloatingThingsLoaderViewModel vm,
			IReadOnlyList<ThingItemViewModel> things,
			string name,
			string folderPath,
			string format,
			bool skipWest = false)
		{
			if (things.Count == 0)
				return;

			var loader = vm.GetActiveSpriteLoader();
			if (loader == null)
			{
				System.Diagnostics.Debug.WriteLine("[ThingsLoader] Export requires a loaded sprite archive.");
				return;
			}

			var options = vm.GetWriteOptions();
			var formatLower = format.ToLowerInvariant();
			var isObd = SupportedFileFormats.IsObdFormat(formatLower);
			var isJson = SupportedFileFormats.IsJsonThingFormat(formatLower);
			var extension = isObd
				? SupportedFileFormats.ExtObd
				: isJson
					? SupportedFileFormats.ExtJson
					: SupportedFileFormats.NormalizeImageExportExtension(formatLower);

			try
			{
				if (!Directory.Exists(folderPath))
				{
					Directory.CreateDirectory(folderPath);
				}

				if (things.Count == 1)
				{
					var thingVm = things[0];
					var thingType = vm.GetThingType(thingVm.Id);
					if (thingType == null)
						return;

					var outputPath = Path.Combine(folderPath, $"{name}_{thingVm.DisplayedId}{extension}");
					if (isObd)
					{
						var document = ThingExchangeHelper.CreatePortableDocument(thingType, loader, options);
						ThingExchangeHelper.WriteObd(outputPath, document, options);
					}
					else if (isJson)
					{
						var document = ThingExchangeHelper.CreatePortableDocument(thingType, loader, options);
						ThingExchangeHelper.WriteNyxThingJson(outputPath, document);
					}
					else
					{
						WriteThingSpritesheetExport(loader, thingType, outputPath, formatLower, skipWest);
					}
				}
				else
				{
					using var spriteSource = new SpriteLoaderSpriteSource(loader);
					foreach (var thingVm in things)
					{
						var thingType = vm.GetThingType(thingVm.Id);
						if (thingType == null)
							continue;

						var outputPath = Path.Combine(folderPath, $"{name}_{thingVm.DisplayedId}{extension}");
						if (isObd)
						{
							var document = ThingExchangeHelper.CreatePortableDocument(thingType, loader, options);
							ThingExchangeHelper.WriteObd(outputPath, document, options);
						}
						else if (isJson)
						{
							var document = ThingExchangeHelper.CreatePortableDocument(thingType, loader, options);
							ThingExchangeHelper.WriteNyxThingJson(outputPath, document);
						}
						else
						{
							WriteThingSpritesheetExport(spriteSource, thingType, outputPath, formatLower, skipWest);
						}
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to export things: {ex.Message}");
			}
		}

		private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName != nameof(FloatingThingsLoaderViewModel.CurrentPage) || _viewModel == null)
				return;

			var currentPage = _viewModel.CurrentPage;
			if (currentPage == _lastKnownPage)
				return;

			var scrollToBottom = currentPage < _lastKnownPage;
			_lastKnownPage = currentPage;
			_autoScrollPageTransitionPending = false;
			ScrollCurrentListToPageEdge(scrollToBottom);
		}

		private void OnViewerPointerPressed(object? sender, PointerPressedEventArgs e)
		{
			if (_isMiddleAutoScrollActive)
			{
				StopMiddleAutoScroll();
				e.Handled = true;
				return;
			}

			if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed && TryStartMiddleAutoScroll(e))
				e.Handled = true;
		}

		private void OnViewerPointerWheelChanged(object? sender, PointerWheelEventArgs e)
		{
			if (_viewModel == null)
				return;

			if (sender is not ListBox listBox || !TryGetScrollViewer(listBox, out var scrollViewer))
				return;

			if (e.Delta.Y > 0 && IsAtTop(scrollViewer) && _viewModel.HasPreviousPage)
			{
				_viewModel.PreviousPageCommand.Execute(null);
				e.Handled = true;
				return;
			}

			if (e.Delta.Y < 0 && IsAtBottom(scrollViewer) && _viewModel.HasNextPage)
			{
				_viewModel.NextPageCommand.Execute(null);
				e.Handled = true;
			}
		}

		private void OnAutoScrollPointerMoved(object? sender, PointerEventArgs e)
		{
			if (!_isMiddleAutoScrollActive || _viewModel == null)
				return;

			_middleAutoScrollDeltaY = e.GetPosition(this).Y - _middleAutoScrollAnchor.Y;
			e.Handled = true;
		}

		private void OnAutoScrollPointerReleased(object? sender, PointerReleasedEventArgs e)
		{
			if (_isMiddleAutoScrollActive)
				e.Handled = true;
		}

		private bool TryStartMiddleAutoScroll(PointerPressedEventArgs e)
		{
			var listBox = GetActiveListBox();
			if (!TryGetScrollViewer(listBox, out _))
				return false;

			_isMiddleAutoScrollActive = true;
			_middleAutoScrollAnchor = e.GetPosition(this);
			_middleAutoScrollDeltaY = 0;
			_autoScrollPageTransitionPending = false;
			_middleAutoScrollPointer = e.Pointer;
			_middleAutoScrollPointer.Capture(this);
			_previousCursor = Cursor;
			Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
			_middleAutoScrollTimer.Start();
			return true;
		}

		private void OnMiddleAutoScrollTick(object? sender, EventArgs e)
		{
			if (!_isMiddleAutoScrollActive || _viewModel == null)
				return;

			var listBox = GetActiveListBox();
			if (!TryGetScrollViewer(listBox, out var scrollViewer))
				return;

			var absDelta = Math.Abs(_middleAutoScrollDeltaY);
			if (absDelta < MiddleAutoScrollDeadZone)
				return;

			var direction = Math.Sign(_middleAutoScrollDeltaY);
			var step = MiddleAutoScrollBaseStep + ((absDelta - MiddleAutoScrollDeadZone) * MiddleAutoScrollAcceleration);
			step = Math.Min(step, MiddleAutoScrollMaxStep) * direction;

			var targetY = scrollViewer.Offset.Y + step;
			var maxY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
			var clampedY = Math.Clamp(targetY, 0, maxY);
			scrollViewer.Offset = new Vector(scrollViewer.Offset.X, clampedY);

			if (_autoScrollPageTransitionPending)
				return;

			if (direction < 0 && clampedY <= 0 && _viewModel.HasPreviousPage)
			{
				_autoScrollPageTransitionPending = true;
				_viewModel.PreviousPageCommand.Execute(null);
			}
			else if (direction > 0 && clampedY >= maxY && _viewModel.HasNextPage)
			{
				_autoScrollPageTransitionPending = true;
				_viewModel.NextPageCommand.Execute(null);
			}
		}

		private void StopMiddleAutoScroll()
		{
			if (!_isMiddleAutoScrollActive)
				return;

			_isMiddleAutoScrollActive = false;
			_middleAutoScrollDeltaY = 0;
			_autoScrollPageTransitionPending = false;
			_middleAutoScrollTimer.Stop();
			_middleAutoScrollPointer?.Capture(null);
			_middleAutoScrollPointer = null;
			Cursor = _previousCursor;
			_previousCursor = null;
		}

		private ListBox? GetActiveListBox()
		{
			if (_viewModel?.ShowGridViewContent == true)
				return ThingGridListBox;
			if (_viewModel?.ShowListViewContent == true)
				return ThingListListBox;
			return ThingGridListBox.IsVisible ? ThingGridListBox : ThingListListBox;
		}

		private static bool TryGetScrollViewer(ListBox? listBox, out ScrollViewer scrollViewer)
		{
			scrollViewer = listBox?.FindDescendantOfType<ScrollViewer>()!;
			return scrollViewer != null;
		}

		private static bool IsAtTop(ScrollViewer scrollViewer) => scrollViewer.Offset.Y <= 0.5;

		private static bool IsAtBottom(ScrollViewer scrollViewer)
		{
			var maxY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
			return scrollViewer.Offset.Y >= maxY - 0.5;
		}

		private void ScrollCurrentListToPageEdge(bool toBottom)
		{
			var listBox = GetActiveListBox();
			if (!TryGetScrollViewer(listBox, out var scrollViewer) || scrollViewer == null)
				return;

			Dispatcher.UIThread.Post(() =>
			{
				var maxY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
				var y = toBottom ? maxY : 0;
				scrollViewer.Offset = new Vector(scrollViewer.Offset.X, y);
			}, DispatcherPriority.Loaded);
		}
	}
}
