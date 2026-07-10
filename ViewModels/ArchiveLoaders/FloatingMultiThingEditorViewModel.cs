using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyxAssets.Things;
using NyxAssets.Things.Frames;
using NyxAssetsEditor.Services.Exchange;
using NyxAssetsEditor.ViewModels.Core;

namespace NyxAssetsEditor.ViewModels.ArchiveLoaders;

public partial class FloatingMultiThingEditorViewModel : PanelViewModelBase, IDisposable
{
	private readonly List<ThingType> _working = new();
	private readonly Dictionary<uint, FloatingThingEditorViewModel> _individualEditors = new();
	private BatchThingEntryViewModel? _selectedEntry;
	private FloatingThingEditorViewModel? _activeIndividualEditor;
	private ThingType _allBaseline;
	private bool _showClosePrompt;

	public FloatingThingsLoaderViewModel SourcePanel { get; }
	public ObservableCollection<BatchThingEntryViewModel> Entries { get; } = new();
	public FloatingThingEditorViewModel AllEditor { get; }
	public FloatingThingEditorViewModel? ActiveEditor => SelectedEntry == null ? AllEditor : ActiveIndividualEditor;
	public FloatingThingEditorViewModel? ActiveIndividualEditor
	{
		get => _activeIndividualEditor;
		private set => SetProperty(ref _activeIndividualEditor, value);
	}
	public string Title => $"Edit {Entries.Count} Things";
	public bool ShowClosePrompt { get => _showClosePrompt; set => SetProperty(ref _showClosePrompt, value); }

	public BatchThingEntryViewModel? SelectedEntry
	{
		get => _selectedEntry;
		set
		{
			if (!SetProperty(ref _selectedEntry, value)) return;
			LoadIndividualEditor(value);
			OnPropertyChanged(nameof(ActiveEditor));
		}
	}

	public FloatingMultiThingEditorViewModel(FloatingThingsLoaderViewModel source, IEnumerable<ThingType> things)
	{
		SourcePanel = source;
		PanelWidth = 780;
		ContentHeight = 650;
		DockState = "Floating";
		IsDefaultPosition = true;

		foreach (var thing in things.OrderBy(t => t.Id))
		{
			_working.Add(ThingCloner.Clone(thing, thing.Id));
			Entries.Add(new BatchThingEntryViewModel(this, thing.Id));
		}

		var blank = ThingCloner.Clone(_working[0], _working[0].Id);
		ResetScalarValues(blank);
		foreach (var group in blank.FrameGroups)
			Array.Clear(group.SpriteIds, 0, group.SpriteIds.Length);

		_allBaseline = ThingCloner.Clone(blank, blank.Id);
		AllEditor = new FloatingThingEditorViewModel(source, blank, useDetachedThing: true)
		{
			IsEmbedded = true,
			ContentHeight = 560,
			BatchSaveRequested = SaveAllTemplate,
			BatchCancelRequested = ResetAllEditor,
		};
		AllEditor.BatchHost = this;
	}

	private static void ResetScalarValues(ThingType thing)
	{
		foreach (var property in typeof(ThingType).GetProperties(BindingFlags.Instance | BindingFlags.Public)
			.Where(p => p.CanWrite && p.Name is not "Id" and not "Kind" and not "FrameGroups" and not "ExtraProperties" && IsEditableType(p.PropertyType)))
		{
			var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
			property.SetValue(thing, type == typeof(string) ? string.Empty : Activator.CreateInstance(type));
		}
		thing.ExtraProperties.Clear();
	}

	private static bool IsEditableType(Type type)
	{
		var valueType = Nullable.GetUnderlyingType(type) ?? type;
		return valueType == typeof(string) || valueType == typeof(bool) || valueType.IsEnum
			|| valueType == typeof(byte) || valueType == typeof(sbyte)
			|| valueType == typeof(short) || valueType == typeof(ushort)
			|| valueType == typeof(int) || valueType == typeof(uint)
			|| valueType == typeof(long) || valueType == typeof(ulong)
			|| valueType == typeof(float) || valueType == typeof(double) || valueType == typeof(decimal);
	}

	private void SaveAllTemplate(ThingType edited)
	{
		ApplyTemplateDifferences(_allBaseline, edited);
		SourcePanel.ApplyThingEdits(_working.Select(t => ThingCloner.Clone(t, t.Id)));
		_allBaseline = ThingCloner.Clone(edited, edited.Id);

		foreach (var entry in Entries)
			entry.NotifyPreviewChanged();
		foreach (var pair in _individualEditors)
		{
			if (pair.Value.IsDirty) continue;
			var current = SourcePanel.GetThingType(pair.Key);
			if (current != null) pair.Value.LoadThing(current);
		}

		AllEditor.IsDirty = false;
	}

	private void ApplyTemplateDifferences(ThingType baseline, ThingType edited)
	{
		foreach (var property in typeof(ThingType).GetProperties(BindingFlags.Instance | BindingFlags.Public)
			.Where(p => p.CanRead && p.CanWrite && p.Name is not "Id" and not "Kind" and not "FrameGroups" and not "ExtraProperties" && IsEditableType(p.PropertyType)))
		{
			var before = property.GetValue(baseline);
			var after = property.GetValue(edited);
			if (Equals(before, after) && !AllEditor.BatchTouchedProperties.Contains(property.Name)) continue;
			foreach (var target in _working) property.SetValue(target, after);
		}

		var sharedGroups = Math.Min(baseline.FrameGroups.Count, edited.FrameGroups.Count);
		for (var index = 0; index < sharedGroups; index++)
			ApplyFrameGroupDifferences(index, baseline.FrameGroups[index], edited.FrameGroups[index]);

		if (!baseline.ExtraProperties.OrderBy(x => x.Key).SequenceEqual(edited.ExtraProperties.OrderBy(x => x.Key)))
		{
			foreach (var target in _working)
			{
				target.ExtraProperties.Clear();
				foreach (var pair in edited.ExtraProperties) target.ExtraProperties[pair.Key] = pair.Value;
			}
		}
	}

	private void ApplyFrameGroupDifferences(int index, ThingFrameGroup beforeGroup, ThingFrameGroup afterGroup)
	{
		var targetGroups = _working.Where(t => t.FrameGroups.Count > index).Select(t => t.FrameGroups[index]).ToList();
		foreach (var property in beforeGroup.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
			.Where(p => p.CanRead && p.CanWrite && IsEditableType(p.PropertyType)))
		{
			var before = property.GetValue(beforeGroup);
			var after = property.GetValue(afterGroup);
			if (Equals(before, after)) continue;
			foreach (var targetGroup in targetGroups)
			{
				property.SetValue(targetGroup, after);
				ThingFrameGroupEditor.EnsureSpriteCapacity(targetGroup);
			}
		}

		if (!beforeGroup.SpriteIds.SequenceEqual(afterGroup.SpriteIds))
		{
			foreach (var targetGroup in targetGroups)
			{
				ThingFrameGroupEditor.EnsureSpriteCapacity(targetGroup);
				Array.Copy(afterGroup.SpriteIds, targetGroup.SpriteIds, Math.Min(targetGroup.SpriteIds.Length, afterGroup.SpriteIds.Length));
			}
		}

		var timingsChanged = beforeGroup.FrameTimings == null != (afterGroup.FrameTimings == null)
			|| (beforeGroup.FrameTimings != null && afterGroup.FrameTimings != null
				&& !beforeGroup.FrameTimings.Select(t => (t.MinimumMilliseconds, t.MaximumMilliseconds))
					.SequenceEqual(afterGroup.FrameTimings.Select(t => (t.MinimumMilliseconds, t.MaximumMilliseconds))));
		if (!timingsChanged) return;

		foreach (var targetGroup in targetGroups)
		{
			targetGroup.FrameTimings = afterGroup.FrameTimings?
				.Select(t => new AnimationFrameTiming(t.MinimumMilliseconds, t.MaximumMilliseconds)).ToArray();
		}
	}

	private void ResetAllEditor() => AllEditor.LoadThing(ThingCloner.Clone(_allBaseline, _allBaseline.Id));

	private void LoadIndividualEditor(BatchThingEntryViewModel? entry)
	{
		if (entry == null) { ActiveIndividualEditor = null; return; }
		if (!_individualEditors.TryGetValue(entry.Id, out var editor))
		{
			var thing = SourcePanel.GetThingType(entry.Id);
			if (thing == null) return;
			editor = new FloatingThingEditorViewModel(SourcePanel, thing) { IsEmbedded = true, ContentHeight = 560 };
			editor.PropertyChanged += (_, args) =>
			{
				if (args.PropertyName == nameof(FloatingThingEditorViewModel.IsDirty) && !editor.IsDirty)
					RefreshWorkingThingAfterIndividualSave(entry.Id);
			};
			_individualEditors[entry.Id] = editor;
		}
		ActiveIndividualEditor = editor;
	}

	private void RefreshWorkingThingAfterIndividualSave(uint id)
	{
		var latest = SourcePanel.GetThingType(id);
		var index = _working.FindIndex(t => t.Id == id);
		if (latest != null && index >= 0) _working[index] = ThingCloner.Clone(latest, id);
	}

	[RelayCommand]
	private void SelectAll() => SelectedEntry = null;

	[RelayCommand]
	private void RemoveEntry(BatchThingEntryViewModel? entry)
	{
		if (entry == null || Entries.Count <= 1) return;
		var index = Entries.IndexOf(entry);
		_working.RemoveAll(t => t.Id == entry.Id);
		Entries.Remove(entry);
		if (_individualEditors.Remove(entry.Id, out var removedEditor)) removedEditor.ClosePanel();
		SelectedEntry = Entries[Math.Min(index, Entries.Count - 1)];
		AllEditor.BatchTouchedProperties.Clear();
		OnPropertyChanged(nameof(Title));
		foreach (var remaining in Entries) remaining.NotifyCanRemoveChanged();
	}

	[RelayCommand]
	private void RequestEditorClose()
	{
		if (AllEditor.IsDirty || _individualEditors.Values.Any(e => e.IsDirty)) ShowClosePrompt = true;
		else ClosePanel();
	}

	[RelayCommand]
	private void PromptSave()
	{
		ShowClosePrompt = false;
		foreach (var editor in _individualEditors.Values.Where(e => e.IsDirty).ToList()) editor.Save();
		if (AllEditor.IsDirty) AllEditor.Save();
		ClosePanel();
	}

	[RelayCommand]
	private void PromptDontSave()
	{
		ShowClosePrompt = false;
		if (AllEditor.IsDirty) AllEditor.Cancel();
		foreach (var editor in _individualEditors.Values.Where(e => e.IsDirty).ToList()) editor.Cancel();
		ClosePanel();
	}

	[RelayCommand]
	private void PromptCancel() => ShowClosePrompt = false;

	public void Dispose()
	{
		AllEditor.ClosePanel();
		foreach (var editor in _individualEditors.Values) editor.ClosePanel();
		_individualEditors.Clear();
	}
}

public sealed partial class BatchThingEntryViewModel : ObservableObject
{
	private readonly FloatingMultiThingEditorViewModel _owner;
	public uint Id { get; }
	public uint DisplayedId => _owner.SourcePanel.GetDisplayedId(Id);
	public Avalonia.Media.Imaging.WriteableBitmap? PreviewImage => _owner.SourcePanel.PagedThings.FirstOrDefault(x => x.Id == Id)?.PreviewImage;
	public bool CanRemove => _owner.Entries.Count > 1;

	public BatchThingEntryViewModel(FloatingMultiThingEditorViewModel owner, uint id) { _owner = owner; Id = id; }
	public void NotifyCanRemoveChanged() => OnPropertyChanged(nameof(CanRemove));
	public void NotifyPreviewChanged() => OnPropertyChanged(nameof(PreviewImage));
	[RelayCommand] private void Remove() => _owner.RemoveEntryCommand.Execute(this);
}
