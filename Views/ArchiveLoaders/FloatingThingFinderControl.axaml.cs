using System;
using System.Linq;
using Avalonia;
using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;

namespace NyxAssetsEditor.Views.ArchiveLoaders;

public partial class FloatingThingFinderControl : UserControl
{
	public FloatingThingFinderControl()
	{
		InitializeComponent();
		var titleBar = this.FindControl<Border>("TitleBar");
		if (titleBar == null) return;
		var interaction = new FloatingPanelInteraction(this, titleBar, minWidth: 860, minHeight: 430);
		Register(interaction, "ResizeLeft", 4);
		Register(interaction, "ResizeRight", 1);
		Register(interaction, "ResizeBottom", 2);
		Register(interaction, "ResizeCorner", 3);
	}

	private void Register(FloatingPanelInteraction interaction, string name, int direction)
	{
		var border = this.FindControl<Border>(name);
		if (border != null) interaction.RegisterResizeHandle(border, direction);
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		if (DataContext is not FloatingThingFinderViewModel vm || !vm.IsDefaultPosition)
			return;

		var canvas = GetParentCanvas();
		if (canvas == null) return;

		void CenterPanel()
		{
			if (canvas.Bounds.Width <= 0 || canvas.Bounds.Height <= 0) return;
			vm.DockState = "Floating";
			vm.PositionX = Math.Max(0, (canvas.Bounds.Width - vm.PanelWidth) / 2);
			vm.PositionY = Math.Max(0, (canvas.Bounds.Height - vm.ContentHeight) / 2);
			vm.IsDefaultPosition = false;
		}

		if (canvas.Bounds.Width > 0 && canvas.Bounds.Height > 0)
			CenterPanel();
		else
		{
			canvas.SizeChanged += OnCanvasSizeChanged;
			void OnCanvasSizeChanged(object? sender, SizeChangedEventArgs args)
			{
				if (args.NewSize.Width <= 0 || args.NewSize.Height <= 0) return;
				canvas.SizeChanged -= OnCanvasSizeChanged;
				CenterPanel();
			}
		}
	}

	private Canvas? GetParentCanvas()
	{
		Visual? visual = this;
		while (visual != null && visual is not Canvas)
			visual = visual.GetVisualParent();
		return visual as Canvas;
	}

	private void OnResultContextRequested(object? sender, ContextRequestedEventArgs e)
	{
		if (sender is not Control control || control.DataContext is not ThingFinderResultViewModel result || DataContext is not FloatingThingFinderViewModel vm) return;
		var menu = new ContextMenu();
		
		var copy = new MenuItem { Header = "Copy ID" };
		copy.Click += async (_, _) =>
		{
			var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
			if (clipboard != null) await clipboard.SetTextAsync(result.DisplayedId.ToString());
		};
		menu.Items.Add(copy);
		menu.Items.Add(new Separator());

		var export = new MenuItem { Header = "Export..." };
		export.Click += (_, _) =>
		{
			if (vm.SourcePanel != null)
			{
				vm.SourcePanel.RequestExportThing(new ThingItemViewModel(result.Thing.Id, vm.SourcePanel));
			}
		};

		var openNew = new MenuItem { Header = "Open in new window" };
		openNew.Click += async (_, _) =>
		{
			if (vm.SourcePanel != null)
			{
				await vm.Parent.OpenThingEditor(vm.SourcePanel, result.Thing.Id, newWindow: true);
			}
		};

		var edit = new MenuItem { Header = "Edit" };
		edit.Click += async (_, _) =>
		{
			if (vm.SourcePanel != null)
			{
				await vm.Parent.OpenThingEditor(vm.SourcePanel, result.Thing.Id, newWindow: false);
			}
		};

		menu.Items.Add(export);
		menu.Items.Add(new Separator());
		menu.Items.Add(openNew);
		menu.Items.Add(edit);

		var actions = result.GetContextActions();
		if (actions.Count > 0)
		{
			menu.Items.Add(new Separator());
			foreach (var action in actions)
			{
				var item = new MenuItem { Header = action.Label };
				item.Click += async (_, _) => await result.ExecuteContextActionAsync(action);
				menu.Items.Add(item);
			}
		}

		menu.Open(control);
		e.Handled = true;
	}

	private void OnDragOver(object? sender, DragEventArgs e)
	{
		var files = e.DataTransfer.TryGetFiles()?.ToList();
		var valid = files is { Count: 1 } && files[0].TryGetLocalPath() is { } path;
		e.DragEffects = valid ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	private async void OnDrop(object? sender, DragEventArgs e)
	{
		e.Handled = true;
		if (DataContext is not FloatingThingFinderViewModel vm) return;
		var files = e.DataTransfer.TryGetFiles()?.ToList();
		if (files is { Count: 1 } && files[0].TryGetLocalPath() is { } path)
		{
			try
			{
				using var stream = System.IO.File.OpenRead(path);
				var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
				await vm.LoadSpriteFromBitmapAsync(bitmap);
			}
			catch (Exception ex)
			{
				vm.SearchBySpriteStatus = $"Error loading file: {ex.Message}";
			}
		}
	}

	private async void OnResultDoubleTapped(object? sender, TappedEventArgs e)
	{
		if (DataContext is FloatingThingFinderViewModel vm && sender is Control control && control.DataContext is ThingFinderResultViewModel result)
		{
			if (vm.SourcePanel != null)
			{
				await vm.Parent.OpenThingEditor(vm.SourcePanel, result.Thing.Id, newWindow: false);
			}
		}
	}
}
