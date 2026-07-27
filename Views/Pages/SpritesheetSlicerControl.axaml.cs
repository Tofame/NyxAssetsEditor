using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NyxAssetsEditor.Services.ImportExport;
using NyxAssetsEditor.ViewModels.Pages;
using NyxAssetsEditor.Views.ArchiveLoaders;

namespace NyxAssetsEditor.Views.Pages;

public partial class SpritesheetSlicerControl : UserControl
{
	private SpritesheetSlicerViewModel ViewModel => (SpritesheetSlicerViewModel)DataContext!;

	public SpritesheetSlicerControl()
	{
		InitializeComponent();
		RegisterZoomWheelHandler();
		DataContextChanged += (_, _) => (DataContext as SpritesheetSlicerViewModel)?.RefreshTargets();

		var titleBar = this.FindControl<Border>("TitleBar");
		if (titleBar == null) return;
		var interaction = new FloatingPanelInteraction(this, titleBar, minWidth: 1000, minHeight: 620);
		RegisterResizeHandle(interaction, "ResizeLeft", 4);
		RegisterResizeHandle(interaction, "ResizeRight", 1);
		RegisterResizeHandle(interaction, "ResizeBottom", 2);
		RegisterResizeHandle(interaction, "ResizeCorner", 3);
		RegisterResizeHandle(interaction, "ResizeBottomLeft", 5);
		RegisterResizeHandle(interaction, "ResizeTop", 6);
		RegisterResizeHandle(interaction, "ResizeTopRight", 7);
		RegisterResizeHandle(interaction, "ResizeTopLeft", 8);
	}

	private void RegisterZoomWheelHandler() => AddHandler(
		InputElement.PointerWheelChangedEvent,
		OnPointerWheelChanged,
		RoutingStrategies.Tunnel,
		handledEventsToo: true);

	private void RegisterResizeHandle(FloatingPanelInteraction interaction, string name, int direction)
	{
		var border = this.FindControl<Border>(name);
		if (border != null) interaction.RegisterResizeHandle(border, direction);
	}

	private async void OnOpenClick(object? sender, RoutedEventArgs e)
	{
		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;
		IStorageFolder? start = null;
		if (Directory.Exists(ViewModel.LastOpenDirectory))
			start = await topLevel.StorageProvider.TryGetFolderFromPathAsync(ViewModel.LastOpenDirectory);
		var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Open spritesheet",
			AllowMultiple = false,
			SuggestedStartLocation = start,
			FileTypeFilter = new[]
			{
				new FilePickerFileType("Image files") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp", "*.tga" } }
			}
		});
		if (files.Count > 0) ViewModel.LoadImage(files[0].Path.LocalPath);
	}

	private async void OnExportClick(object? sender, RoutedEventArgs e)
	{
		try
		{
			if (TopLevel.GetTopLevel(this) is not Window owner) return;
			var selectedSet = CroppedSpritesListBox.SelectedItems?
				.OfType<SlicerPreviewViewModel>()
				.ToHashSet() ?? new HashSet<SlicerPreviewViewModel>();
			var selectedSprites = ViewModel.CroppedSprites.Where(selectedSet.Contains).ToList();
			var dialog = new SlicerExportDialog(ViewModel.LastExportDirectory, selectedSprites.Count > 0);
			await dialog.ShowDialog(owner);
			if (!dialog.IsConfirmed) return;
			ViewModel.ExportCropped(
				dialog.ExportPath,
				dialog.ExportSelectedOnly ? selectedSprites : null);
		}
		catch (Exception ex) { ViewModel.ReportError(ex.Message); }
	}

	private void OnRemoveCroppedClick(object? sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: SlicerPreviewViewModel sprite }) ViewModel.RemoveCroppedCommand.Execute(sprite);
	}

	private void OnDragOver(object? sender, DragEventArgs e)
	{
		var files = e.DataTransfer.TryGetFiles()?.ToList();
		var valid = files is { Count: 1 } && files[0].TryGetLocalPath() is { } path && SpriteImageImporter.IsSupportedImage(path);
		e.DragEffects = valid ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	private void OnDrop(object? sender, DragEventArgs e)
	{
		e.Handled = true;
		var files = e.DataTransfer.TryGetFiles()?.ToList();
		if (files is not { Count: 1 } || files[0].TryGetLocalPath() is not { } path || !SpriteImageImporter.IsSupportedImage(path))
		{
			ViewModel.ReportError("Drop exactly one supported image file.");
			return;
		}
		ViewModel.LoadImage(path);
	}

	private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
	{
		if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
		if (e.Delta.Y > 0) ViewModel.ZoomIn();
		else if (e.Delta.Y < 0) ViewModel.ZoomOut();
		else return;
		e.Handled = true;
	}

	private void OnKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.O)
		{
			OnOpenClick(this, new RoutedEventArgs()); e.Handled = true; return;
		}
		if (e.Key == Key.Enter && e.Source is not TextBox and not NumericUpDown && ViewModel.CropCommand.CanExecute(null))
		{
			ViewModel.CropCommand.Execute(null); e.Handled = true; return;
		}
		if (e.KeyModifiers == KeyModifiers.None && e.Source is not TextBox and not NumericUpDown and not Slider and not ComboBox)
		{
			switch (e.Key)
			{
				case Key.Left: ViewModel.NudgeGrid(-1, 0); e.Handled = true; return;
				case Key.Right: ViewModel.NudgeGrid(1, 0); e.Handled = true; return;
				case Key.Up: ViewModel.NudgeGrid(0, -1); e.Handled = true; return;
				case Key.Down: ViewModel.NudgeGrid(0, 1); e.Handled = true; return;
			}
		}
		if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
		if (e.Key is Key.OemPlus or Key.Add) { ViewModel.ZoomIn(); e.Handled = true; }
		else if (e.Key is Key.OemMinus or Key.Subtract) { ViewModel.ZoomOut(); e.Handled = true; }
	}
}
