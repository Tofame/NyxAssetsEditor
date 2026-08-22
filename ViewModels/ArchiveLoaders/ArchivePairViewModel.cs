using NyxAssetsEditor.ViewModels.Pages;
using NyxAssetsEditor.ViewModels.Common;

namespace NyxAssetsEditor.ViewModels.ArchiveLoaders;

public class ArchivePairViewModel
{
	private readonly ArchivePairPathPresentation _presentation;

	public LinkedArchivePair Pair { get; }
	public string SpritePath => Pair.SpritePanel.FilePath;
	public string ThingsPath => Pair.ThingsPanel.FilePath;
	public string DisplayName => _presentation.DisplayName;
	public string DetailsText => _presentation.DetailsText;
	public string ToolTipText => _presentation.ToolTipText;

	public ArchivePairViewModel(LinkedArchivePair pair)
	{
		Pair = pair;
		_presentation = ArchivePairPathPresentation.Create(pair.SpritePanel.FilePath, pair.ThingsPanel.FilePath);
	}
}
