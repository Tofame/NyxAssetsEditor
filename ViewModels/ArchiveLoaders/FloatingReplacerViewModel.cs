using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using NyxAssets.Things;
using NyxAssetsEditor.Services.Replacement;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Common;
using NyxAssetsEditor.ViewModels.Pages;

namespace NyxAssetsEditor.ViewModels.ArchiveLoaders;

public sealed class ReplacementArchivePairViewModel
{
	private readonly ArchivePairPathPresentation _presentation;

	public ReplacementArchivePairViewModel(LinkedArchivePair pair)
	{
		Pair = pair;
		_presentation = ArchivePairPathPresentation.Create(pair.SpritePanel.FilePath, pair.ThingsPanel.FilePath);
	}

	public LinkedArchivePair Pair { get; }
	public string DisplayName => _presentation.DisplayName;
	public string DetailsText => _presentation.DetailsText;
	public string ToolTipText => _presentation.ToolTipText;
}

public partial class FloatingReplacerViewModel : PanelViewModelBase
{
	public const double DefaultPanelWidth = 580;
	public const double DefaultContentHeight = 540;
	private readonly AssetsViewModel _parent;
	private readonly List<AppliedReplacementTransaction> _undoHistory = new();
	private readonly List<AppliedReplacementTransaction> _redoHistory = new();
	private ReplacementArchivePairViewModel? _selectedSourcePair;
	private ReplacementArchivePairViewModel? _selectedTargetPair;
	private AssetReplacementMode _selectedMode = AssetReplacementMode.Things;
	private ThingKind _selectedThingKind = ThingKind.Item;
	private uint _fromId = 100;
	private uint _toId = 100;
	private string _statusText = "Select two different archive pairs and an ID range.";
	private bool _hasError;

	public FloatingReplacerViewModel(AssetsViewModel parent)
	{
		_parent = parent;
		PanelWidth = DefaultPanelWidth;
		ContentHeight = DefaultContentHeight;
		RefreshArchivePairs();
	}

	public string Title => "Replacer";
	public ObservableCollection<ReplacementArchivePairViewModel> ArchivePairs { get; } = new();
	public Array ThingKinds { get; } = Enum.GetValues<ThingKind>();

	public ReplacementArchivePairViewModel? SelectedSourcePair
	{
		get => _selectedSourcePair;
		set
		{
			if (SetProperty(ref _selectedSourcePair, value))
				NotifyInputsChanged();
		}
	}

	public ReplacementArchivePairViewModel? SelectedTargetPair
	{
		get => _selectedTargetPair;
		set
		{
			if (SetProperty(ref _selectedTargetPair, value))
				NotifyInputsChanged();
		}
	}

	public AssetReplacementMode SelectedMode
	{
		get => _selectedMode;
		private set
		{
			if (!SetProperty(ref _selectedMode, value)) return;
			OnPropertyChanged(nameof(IsThingsMode));
			OnPropertyChanged(nameof(IsSpritesMode));
			NotifyInputsChanged();
		}
	}

	public bool IsThingsMode => SelectedMode == AssetReplacementMode.Things;
	public bool IsSpritesMode => SelectedMode == AssetReplacementMode.Sprites;

	public ThingKind SelectedThingKind
	{
		get => _selectedThingKind;
		set
		{
			if (SetProperty(ref _selectedThingKind, value))
				NotifyInputsChanged();
		}
	}

	public uint FromId
	{
		get => _fromId;
		set
		{
			if (SetProperty(ref _fromId, value))
				NotifyInputsChanged();
		}
	}

	public uint ToId
	{
		get => _toId;
		set
		{
			if (SetProperty(ref _toId, value))
				NotifyInputsChanged();
		}
	}

	public string StatusText
	{
		get => _statusText;
		private set => SetProperty(ref _statusText, value);
	}

	public bool HasError
	{
		get => _hasError;
		private set => SetProperty(ref _hasError, value);
	}

	public bool CanReplace => SelectedSourcePair != null
		&& SelectedTargetPair != null
		&& !ReferenceEquals(SelectedSourcePair.Pair, SelectedTargetPair.Pair)
		&& FromId > 0
		&& ToId >= FromId;

	public void RefreshArchivePairs()
	{
		var sourceSprite = SelectedSourcePair?.Pair.SpritePanel;
		var sourceThings = SelectedSourcePair?.Pair.ThingsPanel;
		var targetSprite = SelectedTargetPair?.Pair.SpritePanel;
		var targetThings = SelectedTargetPair?.Pair.ThingsPanel;

		ArchivePairs.Clear();
		foreach (var pair in _parent.GetCompilePairs())
			ArchivePairs.Add(new ReplacementArchivePairViewModel(pair));

		SelectedSourcePair = ArchivePairs.FirstOrDefault(item =>
			ReferenceEquals(item.Pair.SpritePanel, sourceSprite) && ReferenceEquals(item.Pair.ThingsPanel, sourceThings))
			?? ArchivePairs.FirstOrDefault();
		SelectedTargetPair = ArchivePairs.FirstOrDefault(item =>
			ReferenceEquals(item.Pair.SpritePanel, targetSprite) && ReferenceEquals(item.Pair.ThingsPanel, targetThings))
			?? ArchivePairs.FirstOrDefault(item => !ReferenceEquals(item, SelectedSourcePair));
		NotifyInputsChanged();
	}

	public void ConfigureForThings(FloatingThingsLoaderViewModel target, ThingKind kind, uint fromId, uint toId)
	{
		RefreshArchivePairs();
		SelectedMode = AssetReplacementMode.Things;
		SelectedThingKind = kind;
		FromId = fromId;
		ToId = toId;
		SelectTarget(target, null);
	}

	public void ConfigureForSprites(FloatingSpriteLoaderViewModel target, uint fromId, uint toId)
	{
		RefreshArchivePairs();
		SelectedMode = AssetReplacementMode.Sprites;
		FromId = fromId;
		ToId = toId;
		SelectTarget(null, target);
	}

	private void SelectTarget(FloatingThingsLoaderViewModel? things, FloatingSpriteLoaderViewModel? sprites)
	{
		SelectedTargetPair = ArchivePairs.FirstOrDefault(item =>
			(things == null || ReferenceEquals(item.Pair.ThingsPanel, things))
			&& (sprites == null || ReferenceEquals(item.Pair.SpritePanel, sprites)));
		SelectedSourcePair = ArchivePairs.FirstOrDefault(item =>
			SelectedTargetPair == null || !ReferenceEquals(item.Pair, SelectedTargetPair.Pair));
		NotifyInputsChanged();
	}

	[RelayCommand]
	private void SetMode(string? mode)
	{
		SelectedMode = string.Equals(mode, "Sprites", StringComparison.OrdinalIgnoreCase)
			? AssetReplacementMode.Sprites
			: AssetReplacementMode.Things;
	}

	[RelayCommand(CanExecute = nameof(CanReplace))]
	private void Replace()
	{
		if (SelectedSourcePair == null || SelectedTargetPair == null)
			return;

		var request = new AssetReplacementRequest(
			SelectedSourcePair.Pair,
			SelectedTargetPair.Pair,
			SelectedMode,
			IsThingsMode ? SelectedThingKind : null,
			FromId,
			ToId,
			AddMissingTargetIds: true);
		var batch = AssetReplacementService.Prepare(request);
		if (!batch.CanApply)
		{
			HasError = true;
			StatusText = FormatStatus(batch.Error ?? "There is nothing to replace.", batch.Skipped, batch.Warnings);
			return;
		}

		var result = AssetReplacementService.Apply(batch);
		HasError = !result.Succeeded;
		StatusText = FormatStatus(result.Message, result.Skipped, result.Warnings);
		if (result.Succeeded && result.Transaction != null)
		{
			_undoHistory.Add(result.Transaction);
			_redoHistory.Clear();
			while (_undoHistory.Count > Math.Max(1, SettingsViewModel.UndoLimit))
				_undoHistory.RemoveAt(0);
			NotifyHistoryChanged();
		}
	}

	[RelayCommand(CanExecute = nameof(CanUndo))]
	private void Undo()
	{
		var transaction = _undoHistory[^1];
		if (!transaction.TryUndo(out var error))
		{
			HasError = true;
			StatusText = error ?? "The replacement could not be undone.";
			return;
		}

		_undoHistory.RemoveAt(_undoHistory.Count - 1);
		_redoHistory.Add(transaction);
		HasError = false;
		StatusText = "Undid the last replacement.";
		NotifyHistoryChanged();
	}

	[RelayCommand(CanExecute = nameof(CanRedo))]
	private void Redo()
	{
		var transaction = _redoHistory[^1];
		if (!transaction.TryRedo(out var error))
		{
			HasError = true;
			StatusText = error ?? "The replacement could not be redone.";
			return;
		}

		_redoHistory.RemoveAt(_redoHistory.Count - 1);
		_undoHistory.Add(transaction);
		HasError = false;
		StatusText = "Redid the last replacement.";
		NotifyHistoryChanged();
	}

	private bool CanUndo() => _undoHistory.Count > 0;
	private bool CanRedo() => _redoHistory.Count > 0;

	private void NotifyHistoryChanged()
	{
		UndoCommand.NotifyCanExecuteChanged();
		RedoCommand.NotifyCanExecuteChanged();
	}

	private void NotifyInputsChanged()
	{
		OnPropertyChanged(nameof(CanReplace));
		ReplaceCommand.NotifyCanExecuteChanged();
		HasError = false;
		StatusText = ArchivePairs.Count < 2
			? "Load at least two linked archive pairs to use Replacer."
			: $"Ready to replace IDs {FromId}-{ToId}.";
	}

	private static string FormatStatus(
		string message,
		System.Collections.Generic.IReadOnlyList<ReplacementSkippedId> skipped,
		System.Collections.Generic.IReadOnlyList<string>? warnings = null)
	{
		var lines = new System.Collections.Generic.List<string> { message };
		if (warnings is { Count: > 0 })
		{
			lines.Add("Adjustments and notes:");
			var numbered = new System.Collections.Generic.Dictionary<uint, System.Collections.Generic.List<string>>();
			var general = new System.Collections.Generic.List<string>();
			foreach (var warning in warnings.Distinct())
			{
				var separator = warning.IndexOf(':');
				if (separator > 1 && warning[0] == '#'
					&& uint.TryParse(warning.Substring(1, separator - 1), out var id))
				{
					if (!numbered.TryGetValue(id, out var notes))
					{
						notes = new System.Collections.Generic.List<string>();
						numbered[id] = notes;
					}
					notes.Add(warning.Substring(separator + 1).Trim());
				}
				else
				{
					general.Add(warning);
				}
			}

			lines.AddRange(numbered.Select(item => $"#{item.Key}: {string.Join(" ", item.Value)}"));
			lines.AddRange(general);
		}
		lines.AddRange(skipped.Select(item => $"#{item.Id}: {item.Reason}"));
		return string.Join(Environment.NewLine, lines);
	}
}
