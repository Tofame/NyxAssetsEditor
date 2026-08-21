using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
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
	private decimal? _fromId = 100;
	private decimal? _toId = 100;
	private bool _keepDiscardedSprites = true;
	private string _statusText = "Select two different archive pairs and an ID range.";
	private bool _hasError;
	private bool _syncingRange;
	private bool _fromIdInvalid;
	private bool _toIdInvalid;
	private int _fromIdShakeNonce;
	private int _toIdShakeNonce;
	private DispatcherTimer? _fromIdInvalidTimer;
	private DispatcherTimer? _toIdInvalidTimer;

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
			{
				ClampRangeToTarget();
				NotifyInputsChanged();
			}
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
			ClampRangeToTarget();
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
			{
				ClampRangeToTarget();
				NotifyInputsChanged();
			}
		}
	}

	public decimal? FromId
	{
		get => _fromId;
		set => SetDraftId(ref _fromId, value, nameof(FromId), raisingFrom: true);
	}

	public decimal? ToId
	{
		get => _toId;
		set => SetDraftId(ref _toId, value, nameof(ToId), raisingFrom: false);
	}

	public decimal TargetMinId { get; private set; } = 1;
	public decimal TargetMaxId { get; private set; } = 1;

	public bool FromIdInvalid
	{
		get => _fromIdInvalid;
		private set => SetProperty(ref _fromIdInvalid, value);
	}

	public bool ToIdInvalid
	{
		get => _toIdInvalid;
		private set => SetProperty(ref _toIdInvalid, value);
	}

	public int FromIdShakeNonce => _fromIdShakeNonce;
	public int ToIdShakeNonce => _toIdShakeNonce;

	public bool KeepDiscardedSprites
	{
		get => _keepDiscardedSprites;
		set => SetProperty(ref _keepDiscardedSprites, value);
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

	public bool CanReplace
	{
		get
		{
			if (SelectedSourcePair == null || SelectedTargetPair == null)
				return false;
			if (ReferenceEquals(SelectedSourcePair.Pair, SelectedTargetPair.Pair))
				return false;
			return TryGetValidRange(out _, out _);
		}
	}

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
		if (SelectedSourcePair == null || SelectedTargetPair == null || !TryGetValidRange(out var fromId, out var toId))
			return;

		var request = new AssetReplacementRequest(
			SelectedSourcePair.Pair,
			SelectedTargetPair.Pair,
			SelectedMode,
			IsThingsMode ? SelectedThingKind : null,
			fromId,
			toId,
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
			if (IsSpritesMode && KeepDiscardedSprites && batch.DiscardedSpritePixels.Count > 0)
			{
				try
				{
					var folder = SpriteReplacementBackup.Write(batch.DiscardedSpritePixels, DateTime.Now);
					StatusText += $"{Environment.NewLine}Saved {batch.DiscardedSpritePixels.Count} discarded sprite(s) to {folder}.";
				}
				catch (Exception ex)
				{
					StatusText += $"{Environment.NewLine}Replacement succeeded, but discarded sprites could not be saved: {ex.Message}";
				}
			}
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

	public void CommitIdField(bool fromField)
	{
		if (!TryGetTargetBounds(out var min, out var max))
			return;

		if (fromField)
		{
			if (IsIdInBounds(_fromId, min, max, out _))
				return;
			FlashInvalid(true);
			_fromId = ClampDraft(_fromId, min, max);
			OnPropertyChanged(nameof(FromId));
		}
		else
		{
			if (IsIdInBounds(_toId, min, max, out _))
				return;
			FlashInvalid(false);
			_toId = ClampDraft(_toId, min, max);
			OnPropertyChanged(nameof(ToId));
		}

		if (TryGetValidRange(out var from, out var to) && from > to)
		{
			_syncingRange = true;
			try
			{
				if (fromField)
				{
					_toId = from;
					OnPropertyChanged(nameof(ToId));
				}
				else
				{
					_fromId = to;
					OnPropertyChanged(nameof(FromId));
				}
			}
			finally
			{
				_syncingRange = false;
			}
		}

		NotifyInputsChanged();
	}

	private void SetDraftId(ref decimal? field, decimal? value, string propertyName, bool raisingFrom)
	{
		if (_syncingRange)
		{
			SetProperty(ref field, value, propertyName);
			NotifyInputsChanged();
			return;
		}

		if (value != null && (value < 0 || value != decimal.Truncate(value.Value) || value > uint.MaxValue))
		{
			FlashInvalid(raisingFrom);
			OnPropertyChanged(propertyName);
			NotifyInputsChanged();
			return;
		}

		SetProperty(ref field, value, propertyName);
		if (TryGetValidRange(out var from, out var to) && from > to)
		{
			_syncingRange = true;
			try
			{
				if (raisingFrom)
				{
					_toId = from;
					OnPropertyChanged(nameof(ToId));
				}
				else
				{
					_fromId = to;
					OnPropertyChanged(nameof(FromId));
				}
			}
			finally
			{
				_syncingRange = false;
			}
		}

		NotifyInputsChanged();
	}

	private bool TryGetValidRange(out uint from, out uint to)
	{
		from = 0;
		to = 0;
		if (!TryGetTargetBounds(out var min, out var max))
			return false;
		return IsIdInBounds(_fromId, min, max, out from) && IsIdInBounds(_toId, min, max, out to) && to >= from;
	}

	private static bool IsIdInBounds(decimal? value, uint min, uint max, out uint id)
	{
		id = 0;
		if (value == null || value < 0 || value != decimal.Truncate(value.Value) || value > uint.MaxValue)
			return false;
		id = (uint)value.Value;
		return id >= min && id <= max;
	}

	private static decimal ClampDraft(decimal? value, uint min, uint max)
	{
		if (value == null || value < 0)
			return min;
		var id = value > uint.MaxValue ? uint.MaxValue : (uint)decimal.Truncate(value.Value);
		if (id < min)
			return min;
		if (id > max)
			return max;
		return id;
	}

	private void ClampRangeToTarget()
	{
		RefreshTargetBounds();
		if (!TryGetTargetBounds(out var min, out var max))
			return;
		_syncingRange = true;
		try
		{
			var from = (uint)ClampDraft(_fromId, min, max);
			var to = (uint)ClampDraft(_toId, min, max);
			if (from > to)
				to = from;
			SetProperty(ref _fromId, from, nameof(FromId));
			SetProperty(ref _toId, to, nameof(ToId));
		}
		finally
		{
			_syncingRange = false;
		}
	}

	private void RefreshTargetBounds()
	{
		if (TryGetTargetBounds(out var min, out var max))
		{
			TargetMinId = min;
			TargetMaxId = max;
		}
		else
		{
			TargetMinId = 1;
			TargetMaxId = uint.MaxValue;
		}
		OnPropertyChanged(nameof(TargetMinId));
		OnPropertyChanged(nameof(TargetMaxId));
	}

	private bool TryGetTargetBounds(out uint min, out uint max)
	{
		min = 1;
		max = uint.MaxValue;
		var target = SelectedTargetPair?.Pair;
		if (target == null)
			return false;

		if (IsSpritesMode)
		{
			if (!target.SpritePanel.IsArchiveLoaded)
				return false;
			var count = target.SpritePanel.Loader.SpriteCount;
			min = 1;
			max = count == 0 ? 1u : count;
			return true;
		}

		var catalog = target.ThingsPanel.Catalog;
		if (catalog == null)
			return false;

		var things = target.ThingsPanel.EnumerateThings(SelectedThingKind);
		if (things.Count > 0)
		{
			min = things.Min(thing => thing.Id);
			max = things.Max(thing => thing.Id);
			return true;
		}

		min = SelectedThingKind switch
		{
			ThingKind.Item => ThingCatalog.FirstItemId,
			ThingKind.Outfit => ThingCatalog.FirstOutfitId,
			ThingKind.Effect => ThingCatalog.FirstEffectId,
			ThingKind.Missile => ThingCatalog.FirstMissileId,
			_ => 1u,
		};
		max = min;
		return true;
	}

	private void FlashInvalid(bool fromField)
	{
		if (fromField)
		{
			FromIdInvalid = true;
			_fromIdShakeNonce++;
			OnPropertyChanged(nameof(FromIdShakeNonce));
			RestartInvalidTimer(ref _fromIdInvalidTimer, () => FromIdInvalid = false);
		}
		else
		{
			ToIdInvalid = true;
			_toIdShakeNonce++;
			OnPropertyChanged(nameof(ToIdShakeNonce));
			RestartInvalidTimer(ref _toIdInvalidTimer, () => ToIdInvalid = false);
		}
	}

	private static void RestartInvalidTimer(ref DispatcherTimer? timer, Action clear)
	{
		timer?.Stop();
		timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
		var captured = timer;
		timer.Tick += (_, _) =>
		{
			captured.Stop();
			clear();
		};
		timer.Start();
	}

	private void NotifyInputsChanged()
	{
		RefreshTargetBounds();
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
