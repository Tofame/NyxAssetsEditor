using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;

namespace NyxAssetsEditor.Views.ArchiveLoaders;

public partial class FloatingMultiThingEditorControl : UserControl
{
	public FloatingMultiThingEditorControl()
	{
		InitializeComponent();
		var titleBar = this.FindControl<Border>("TitleBar");
		if (titleBar != null)
			_ = new FloatingPanelInteraction(this, titleBar, minWidth: 560, minHeight: 350);
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		if (DataContext is not FloatingMultiThingEditorViewModel vm || !vm.IsDefaultPosition)
			return;

		var canvas = GetParentCanvas();
		if (canvas == null)
			return;

		void CenterPanel()
		{
			if (canvas.Bounds.Width <= 0 || canvas.Bounds.Height <= 0)
				return;
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
}
