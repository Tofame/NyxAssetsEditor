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
		private FloatingThingsLoaderViewModel? _viewModel;

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
					FileTypeFilter = new[]
					{
						new FilePickerFileType("All Supported Archives") { Patterns = new[] { "*.dat", "*.json" } },
						new FilePickerFileType("Nyx Dat Archive") { Patterns = new[] { "*.dat" } },
						new FilePickerFileType("Nyx Things JSON") { Patterns = new[] { "*.json" } }
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
