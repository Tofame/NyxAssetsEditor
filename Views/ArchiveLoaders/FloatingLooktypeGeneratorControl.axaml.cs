using Avalonia.Controls;
using Avalonia.Input.Platform;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;

namespace NyxAssetsEditor.Views.ArchiveLoaders;

public partial class FloatingLooktypeGeneratorControl : UserControl
{
	public FloatingLooktypeGeneratorControl()
	{
		InitializeComponent();
		var titleBar = this.FindControl<Border>("TitleBar");
		if (titleBar == null) return;
		var interaction = new FloatingPanelInteraction(this, titleBar, minWidth: 760, minHeight: 420);
		Register(interaction, "ResizeLeft", 4); Register(interaction, "ResizeRight", 1);
		Register(interaction, "ResizeBottom", 2); Register(interaction, "ResizeCorner", 3);
	}

	private void Register(FloatingPanelInteraction interaction, string name, int direction)
	{
		var border = this.FindControl<Border>(name); if (border != null) interaction.RegisterResizeHandle(border, direction);
	}

	private async void CopyAppearanceText(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if (DataContext is not FloatingLooktypeGeneratorViewModel viewModel) return;
		var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
		if (clipboard != null) await clipboard.SetTextAsync(viewModel.AppearanceText);
	}
}
