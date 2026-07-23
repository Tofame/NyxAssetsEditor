using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;

namespace NyxAssetsEditor.Views.Pages;

public partial class SpritesOptimizerDialog : Window
{
	public SpritesOptimizerDialog()
	{
		InitializeComponent();
	}

	public SpritesOptimizerDialog(FloatingSpriteLoaderViewModel viewModel)
	{
		InitializeComponent();
		DataContext = new SpritesOptimizerDialogViewModel(viewModel, this);
	}

	private void OnBrowseFolderClick(object? sender, RoutedEventArgs e)
	{
		(DataContext as SpritesOptimizerDialogViewModel)?.OnBrowseFolderClick(sender, e);
	}

	private void OnNextClick(object? sender, RoutedEventArgs e)
	{
		(DataContext as SpritesOptimizerDialogViewModel)?.OnNextClick(sender, e);
	}

	private void OnBackClick(object? sender, RoutedEventArgs e)
	{
		(DataContext as SpritesOptimizerDialogViewModel)?.OnBackClick(sender, e);
	}

	private void OnOptimizeClick(object? sender, RoutedEventArgs e)
	{
		(DataContext as SpritesOptimizerDialogViewModel)?.OnOptimizeClick(sender, e);
	}

	private void OnCancelClick(object? sender, RoutedEventArgs e)
	{
		(DataContext as SpritesOptimizerDialogViewModel)?.OnCancelClick(sender, e);
	}

	private void OnCloseClick(object? sender, RoutedEventArgs e)
	{
		(DataContext as SpritesOptimizerDialogViewModel)?.OnCloseClick(sender, e);
	}
}

public class SpritesOptimizerDialogViewModel : INotifyPropertyChanged
{
	private readonly FloatingSpriteLoaderViewModel _viewModel;
	private readonly Window _window;

	private bool _isLinked;
	private bool _exportRemoved;
	private string _exportPath = string.Empty;
	private int _oldSpriteCount;
	private int _removedCount;
	private int _newSpriteCount;
	private int _unusedCount;

	private enum DialogState
	{
		Config,
		Confirm,
		Completed
	}

	private DialogState _state = DialogState.Config;

	public bool IsLinked
	{
		get => _isLinked;
		set
		{
			if (SetProperty(ref _isLinked, value))
				UpdateStates();
		}
	}

	public bool ExportRemoved
	{
		get => _exportRemoved;
		set
		{
			if (SetProperty(ref _exportRemoved, value))
				UpdateStates();
		}
	}

	public string ExportPath
	{
		get => _exportPath;
		set
		{
			if (SetProperty(ref _exportPath, value))
				UpdateStates();
		}
	}

	public int OldSpriteCount
	{
		get => _oldSpriteCount;
		set
		{
			if (SetProperty(ref _oldSpriteCount, value))
				UpdateStates();
		}
	}

	public int RemovedCount
	{
		get => _removedCount;
		set
		{
			if (SetProperty(ref _removedCount, value))
				UpdateStates();
		}
	}

	public int NewSpriteCount
	{
		get => _newSpriteCount;
		set
		{
			if (SetProperty(ref _newSpriteCount, value))
				UpdateStates();
		}
	}

	public int UnusedCount
	{
		get => _unusedCount;
		set
		{
			if (SetProperty(ref _unusedCount, value))
				UpdateStates();
		}
	}

	public bool ShowConfig => IsLinked && _state == DialogState.Config;
	public bool ShowConfirm => IsLinked && _state == DialogState.Confirm;
	public bool ShowCompleted => IsLinked && _state == DialogState.Completed;

	public bool ShowCloseOnly => !IsLinked || _state == DialogState.Completed;
	public bool ShowConfigButtons => IsLinked && _state == DialogState.Config;
	public bool ShowConfirmButtons => IsLinked && _state == DialogState.Confirm;

	public string ExportMessage => $"Removed sprites successfully exported to: {ExportPath}";

	public SpritesOptimizerDialogViewModel(FloatingSpriteLoaderViewModel viewModel, Window window)
	{
		_viewModel = viewModel;
		_window = window;

		var thingsPanel = _viewModel.GetLinkedThingsPanel();
		IsLinked = thingsPanel != null && thingsPanel.Catalog != null;

		if (IsLinked)
		{
			OldSpriteCount = (int)_viewModel.TotalSprites;
			CalculateUnusedCount();
		}
	}

	private void CalculateUnusedCount()
	{
		var thingsPanel = _viewModel.GetLinkedThingsPanel();
		if (thingsPanel == null || thingsPanel.Catalog == null) return;

		var catalog = thingsPanel.Catalog;
		var usedSprites = new HashSet<uint>();

		foreach (var item in catalog.EnumerateItems())
			foreach (var fg in item.FrameGroups)
				if (fg.SpriteIds != null)
					foreach (var id in fg.SpriteIds)
						if (id != 0) usedSprites.Add(id);

		foreach (var outfit in catalog.EnumerateOutfits())
			foreach (var fg in outfit.FrameGroups)
				if (fg.SpriteIds != null)
					foreach (var id in fg.SpriteIds)
						if (id != 0) usedSprites.Add(id);

		foreach (var effect in catalog.EnumerateEffects())
			foreach (var fg in effect.FrameGroups)
				if (fg.SpriteIds != null)
					foreach (var id in fg.SpriteIds)
						if (id != 0) usedSprites.Add(id);

		foreach (var missile in catalog.EnumerateMissiles())
			foreach (var fg in missile.FrameGroups)
				if (fg.SpriteIds != null)
					foreach (var id in fg.SpriteIds)
						if (id != 0) usedSprites.Add(id);

		int unused = 0;
		for (uint i = 1; i <= _viewModel.TotalSprites; i++)
		{
			if (!usedSprites.Contains(i))
				unused++;
		}

		UnusedCount = unused;
		NewSpriteCount = OldSpriteCount - UnusedCount;
	}

	private void UpdateStates()
	{
		OnPropertyChanged(nameof(ShowConfig));
		OnPropertyChanged(nameof(ShowConfirm));
		OnPropertyChanged(nameof(ShowCompleted));
		OnPropertyChanged(nameof(ShowCloseOnly));
		OnPropertyChanged(nameof(ShowConfigButtons));
		OnPropertyChanged(nameof(ShowConfirmButtons));
		OnPropertyChanged(nameof(ExportMessage));
	}

	public async void OnBrowseFolderClick(object? sender, RoutedEventArgs e)
	{
		var topLevel = TopLevel.GetTopLevel(_window);
		if (topLevel == null) return;

		var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
		{
			Title = "Select Export Folder",
			AllowMultiple = false
		});

		if (folders != null && folders.Count > 0)
		{
			ExportPath = folders[0].Path.LocalPath;
		}
	}

	public void OnNextClick(object? sender, RoutedEventArgs e)
	{
		if (ExportRemoved && string.IsNullOrWhiteSpace(ExportPath))
		{
			return;
		}

		_state = DialogState.Confirm;
		UpdateStates();
	}

	public void OnBackClick(object? sender, RoutedEventArgs e)
	{
		_state = DialogState.Config;
		UpdateStates();
	}

	public void OnOptimizeClick(object? sender, RoutedEventArgs e)
	{
		_viewModel.RunOptimization(ExportRemoved ? ExportPath : null, out int oldC, out int remC, out int newC);
		
		OldSpriteCount = oldC;
		RemovedCount = remC;
		NewSpriteCount = newC;

		_state = DialogState.Completed;
		UpdateStates();
	}

	public void OnCancelClick(object? sender, RoutedEventArgs e)
	{
		_window.Close();
	}

	public void OnCloseClick(object? sender, RoutedEventArgs e)
	{
		_window.Close();
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value)) return false;
		field = value;
		OnPropertyChanged(propertyName);
		return true;
	}
}
