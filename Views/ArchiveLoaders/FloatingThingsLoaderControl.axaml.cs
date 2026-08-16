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

		// Auto-scroll fields
		private bool _autoScrollActive;
		private Point _autoScrollAnchor;
		private DispatcherTimer? _autoScrollTimer;
		private double _autoScrollDeltaY;
		private DateTime _lastPageChangeTime = DateTime.MinValue;
		private int _lastPage = 1;

		public FloatingThingsLoaderControl()
		{
			InitializeComponent();
			
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

			ThingGridListBox.PointerWheelChanged += OnListBoxPointerWheelChanged;
			ThingListListBox.PointerWheelChanged += OnListBoxPointerWheelChanged;

			ThingGridListBox.PointerPressed += OnListBoxPointerPressed;
			ThingListListBox.PointerPressed += OnListBoxPointerPressed;

			PointerMoved += OnGlobalPointerMoved;
			PointerReleased += OnGlobalPointerReleased;
			PointerPressed += OnGlobalPointerPressed;

			DataContextChanged += (_, _) =>
			{
				if (_viewModel != null)
				{
					_viewModel.RequestThingFileDialog -= OnThingFileDialogRequested;
					_viewModel.ScrollToItemRequested -= OnScrollToItemRequested;
					_viewModel.RequestShowInfo -= OnShowInfoRequested;
					_viewModel.RequestShowWarning -= OnShowWarningRequested;
					_viewModel.PropertyChanged -= OnViewModelPropertyChanged;
				}

				_viewModel = DataContext as FloatingThingsLoaderViewModel;
				if (_viewModel != null)
				{
					_viewModel.RequestThingFileDialog += OnThingFileDialogRequested;
					_viewModel.ScrollToItemRequested += OnScrollToItemRequested;
					_viewModel.RequestShowInfo += OnShowInfoRequested;
					_viewModel.RequestShowWarning += OnShowWarningRequested;
					_viewModel.PropertyChanged += OnViewModelPropertyChanged;
					_lastPage = _viewModel.CurrentPage;
				}
			};
		}

		private async void OnShowInfoRequested(object? sender, string message)
		{
			var window = TopLevel.GetTopLevel(this) as Window ?? this.VisualRoot as Window;
			if (window == null) return;
			await new InfoDialog("Things Archive Info", message).ShowDialog(window);
		}

		private async void OnShowWarningRequested(object? sender, (string Title, string Message, string? InfoMessage, string? SnippetCode) e)
		{
			var window = TopLevel.GetTopLevel(this) as Window ?? this.VisualRoot as Window;
			if (window == null) return;
			await new WarningDialog(e.Title, e.Message, e.InfoMessage, e.SnippetCode).ShowDialog(window);
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

		private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (_viewModel == null) return;

			if (e.PropertyName == nameof(FloatingThingsLoaderViewModel.CurrentPage))
			{
				int newPage = _viewModel.CurrentPage;
				bool isBackwards = newPage < _lastPage;
				_lastPage = newPage;

				var listBox = _viewModel.IsGridView ? ThingGridListBox : ThingListListBox;
				if (listBox != null)
				{
					Dispatcher.UIThread.Post(() =>
					{
						var scrollViewer = listBox.FindDescendantOfType<ScrollViewer>();
						if (scrollViewer != null)
						{
							if (isBackwards)
							{
								scrollViewer.Offset = new Vector(scrollViewer.Offset.X, scrollViewer.Extent.Height);
							}
							else
							{
								scrollViewer.Offset = new Vector(scrollViewer.Offset.X, 0);
							}
						}
					}, DispatcherPriority.Loaded);
				}
			}
		}

		private void OnListBoxPointerWheelChanged(object? sender, PointerWheelEventArgs e)
		{
			if (_viewModel == null || sender is not ListBox listBox) return;
			var scrollViewer = listBox.FindDescendantOfType<ScrollViewer>();
			if (scrollViewer == null) return;

			if (e.Delta.Y > 0) // Scrolling up
			{
				if (scrollViewer.Offset.Y <= 0.01)
				{
					if (_viewModel.HasPreviousPage)
					{
						_viewModel.CurrentPage--;
						e.Handled = true;
					}
				}
			}
			else if (e.Delta.Y < 0) // Scrolling down
			{
				double maxScroll = scrollViewer.Extent.Height - scrollViewer.Viewport.Height;
				if (scrollViewer.Offset.Y >= maxScroll - 0.01)
				{
					if (_viewModel.HasNextPage)
					{
						_viewModel.CurrentPage++;
						e.Handled = true;
					}
				}
			}
		}

		private void OnListBoxPointerPressed(object? sender, PointerPressedEventArgs e)
		{
			if (sender is not ListBox listBox) return;

			var prop = e.GetCurrentPoint(listBox).Properties;
			if (prop.IsMiddleButtonPressed)
			{
				e.Handled = true;
				if (_autoScrollActive)
				{
					StopAutoScroll();
				}
				else
				{
					StartAutoScroll(listBox, e);
				}
			}
			else if (_autoScrollActive)
			{
				StopAutoScroll();
				e.Handled = true;
			}
		}

		private void StartAutoScroll(ListBox listBox, PointerPressedEventArgs e)
		{
			_autoScrollActive = true;
			var mainGrid = this.FindControl<Canvas>("AutoScrollCanvas")?.Parent as Control;
			if (mainGrid == null) return;

			_autoScrollAnchor = e.GetPosition(mainGrid);
			
			var indicator = this.FindControl<Image>("AutoScrollIndicator");
			if (indicator != null)
			{
				Canvas.SetLeft(indicator, _autoScrollAnchor.X - 16);
				Canvas.SetTop(indicator, _autoScrollAnchor.Y - 16);
				indicator.IsVisible = true;
			}

			_autoScrollDeltaY = 0;
			e.Pointer.Capture(listBox);

			_autoScrollTimer?.Stop();
			_autoScrollTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(16)
			};
			_autoScrollTimer.Tick += (s, ev) =>
			{
				if (!_autoScrollActive || _viewModel == null)
				{
					_autoScrollTimer?.Stop();
					return;
				}

				var sv = listBox.FindDescendantOfType<ScrollViewer>();
				if (sv == null) return;

				if (Math.Abs(_autoScrollDeltaY) > 10)
				{
					double speed = (_autoScrollDeltaY - Math.Sign(_autoScrollDeltaY) * 10) * 0.15;
					double newY = sv.Offset.Y + speed;
					sv.Offset = new Vector(sv.Offset.X, newY);

					if (speed < 0 && sv.Offset.Y <= 0.01)
					{
						if ((DateTime.Now - _lastPageChangeTime).TotalMilliseconds > 800)
						{
							if (_viewModel.HasPreviousPage)
							{
								_viewModel.CurrentPage--;
								_lastPageChangeTime = DateTime.Now;
							}
						}
					}
					else if (speed > 0)
					{
						double maxScroll = sv.Extent.Height - sv.Viewport.Height;
						if (sv.Offset.Y >= maxScroll - 0.01)
						{
							if ((DateTime.Now - _lastPageChangeTime).TotalMilliseconds > 800)
							{
								if (_viewModel.HasNextPage)
								{
									_viewModel.CurrentPage++;
									_lastPageChangeTime = DateTime.Now;
								}
							}
						}
					}
				}
			};
			_autoScrollTimer.Start();
		}

		private void StopAutoScroll()
		{
			if (!_autoScrollActive) return;
			_autoScrollActive = false;
			_autoScrollTimer?.Stop();
			_autoScrollTimer = null;

			var indicator = this.FindControl<Image>("AutoScrollIndicator");
			if (indicator != null)
			{
				indicator.IsVisible = false;
			}

			var listBox = _viewModel?.IsGridView == true ? ThingGridListBox : ThingListListBox;
			if (listBox != null)
			{
				var topLevel = TopLevel.GetTopLevel(this);
				if (topLevel != null)
				{
				}
			}
		}

		private void OnGlobalPointerMoved(object? sender, PointerEventArgs e)
		{
			if (!_autoScrollActive) return;

			var mainGrid = this.FindControl<Canvas>("AutoScrollCanvas")?.Parent as Control;
			if (mainGrid != null)
			{
				var currentPos = e.GetPosition(mainGrid);
				_autoScrollDeltaY = currentPos.Y - _autoScrollAnchor.Y;
			}
		}

		private void OnGlobalPointerReleased(object? sender, PointerReleasedEventArgs e)
		{
			if (!_autoScrollActive) return;

			if (e.InitialPressMouseButton == MouseButton.Middle)
			{
				var mainGrid = this.FindControl<Canvas>("AutoScrollCanvas")?.Parent as Control;
				if (mainGrid != null)
				{
					var currentPos = e.GetPosition(mainGrid);
					double dist = Math.Abs(currentPos.Y - _autoScrollAnchor.Y);
					if (dist > 15)
					{
						StopAutoScroll();
						e.Handled = true;
					}
				}
			}
		}

		private void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e)
		{
			if (_autoScrollActive)
			{
				StopAutoScroll();
				e.Handled = true;
			}
		}
	}
}
