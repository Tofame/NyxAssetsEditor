using Avalonia.Input;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;

namespace NyxAssetsEditor.Services.DragDrop;

/// <summary>Drag-and-drop payload for sprites dragged from viewer panels.</summary>
public static class SpriteDragContext
{
	public static DataFormat<string> MarkerFormat { get; } =
		DataFormat.CreateStringApplicationFormat("nyxassets-editor.sprite");

	public static DataFormat<FloatingSpriteLoaderViewModel> SourcePanelFormat { get; } =
		DataFormat.CreateInProcessFormat<FloatingSpriteLoaderViewModel>("nyxassets-editor.sprite-source");

	public static DataTransfer CreateDrag(FloatingSpriteLoaderViewModel sourcePanel, uint spriteId)
	{
		var data = new DataTransfer();
		data.Add(DataTransferItem.Create(MarkerFormat, spriteId.ToString()));
		data.Add(DataTransferItem.Create(SourcePanelFormat, sourcePanel));
		return data;
	}

	public static bool CanAccept(DragEventArgs e) => TryRead(e, out _, out _);

	public static bool TryRead(DragEventArgs e, out FloatingSpriteLoaderViewModel? sourcePanel, out uint spriteId)
	{
		sourcePanel = null;
		spriteId = 0;

		if (!e.DataTransfer.Contains(MarkerFormat))
			return false;

		var text = e.DataTransfer.TryGetValue(MarkerFormat);
		if (!uint.TryParse(text, out spriteId) || spriteId < 1)
			return false;

		sourcePanel = e.DataTransfer.TryGetValue(SourcePanelFormat);
		return sourcePanel is { IsArchiveLoaded: true };
	}
}
