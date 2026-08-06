using Avalonia.Controls;
using NyxAssetsEditor.Views.ArchiveLoaders;

namespace NyxAssetsEditor.Views.ArchiveLoaders;

public partial class FloatingReplacerControl : UserControl
{
	public FloatingReplacerControl()
	{
		InitializeComponent();
		var titleBar = this.FindControl<Border>("TitleBar");
		if (titleBar == null) return;

		var interaction = new FloatingPanelInteraction(this, titleBar, null, minWidth: 500, minHeight: 360);
		Register(interaction, "ResizeLeft", 4);
		Register(interaction, "ResizeRight", 1);
		Register(interaction, "ResizeBottom", 2);
		Register(interaction, "ResizeCorner", 3);
		Register(interaction, "ResizeBottomLeft", 5);
		Register(interaction, "ResizeTop", 6);
		Register(interaction, "ResizeTopRight", 7);
		Register(interaction, "ResizeTopLeft", 8);
	}

	private void Register(FloatingPanelInteraction interaction, string name, int direction)
	{
		var handle = this.FindControl<Border>(name);
		if (handle != null)
			interaction.RegisterResizeHandle(handle, direction);
	}
}
