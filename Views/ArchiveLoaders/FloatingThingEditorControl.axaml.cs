using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using NyxAssetsEditor.Services.DragDrop;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;

namespace NyxAssetsEditor.Views.ArchiveLoaders;

public partial class FloatingThingEditorControl : UserControl
{
	private static readonly IBrush AppearanceDropBorderDefault = new SolidColorBrush(Color.Parse("#444444"));
	private static readonly IBrush AppearanceDropBorderActive = new SolidColorBrush(Color.Parse("#3A7BD5"));

	private FloatingThingEditorViewModel? _vm;
	private bool _pushingPatternValues;
	private bool _patternSpinnersHooked;

	public FloatingThingEditorControl()
	{
		InitializeComponent();
		_pushingPatternValues = true;

		DataContextChanged += OnDataContextChanged;
		Loaded += OnLoaded;

		var titleBar = this.FindControl<Border>("TitleBar");
		if (titleBar == null)
			return;

		var interaction = new FloatingPanelInteraction(this, titleBar, minWidth: 400, minHeight: 200);
		RegisterResizeHandle(interaction, "ResizeLeft", 4);
		RegisterResizeHandle(interaction, "ResizeRight", 1);
		RegisterResizeHandle(interaction, "ResizeBottom", 2);
		RegisterResizeHandle(interaction, "ResizeCorner", 3);
		RegisterResizeHandle(interaction, "ResizeBottomLeft", 5);
	}

	private void OnDataContextChanged(object? sender, EventArgs e)
	{
		DetachViewModel();
		if (DataContext is FloatingThingEditorViewModel vm)
			AttachViewModel(vm);
	}

	private void OnLoaded(object? sender, RoutedEventArgs e)
	{
		_pushingPatternValues = true;
		EnsurePatternSpinnersHooked();
		if (DataContext is FloatingThingEditorViewModel vm)
		{
			vm.RefreshPatternBindings();
			Dispatcher.UIThread.Post(() => PushPatternValuesToControls(vm));
		}
	}

	private void OnAppearanceDragEnter(object? sender, DragEventArgs e)
	{
		if (!SpriteDragContext.CanAccept(e))
			return;

		if (sender is Border border)
		{
			border.BorderBrush = AppearanceDropBorderActive;
			border.BorderThickness = new Thickness(2);
		}

		if (DataContext is FloatingThingEditorViewModel vm)
		{
			var pos = e.GetPosition(AppearanceImageControl);
			vm.UpdateAppearanceDragHover(pos.X, pos.Y);
		}
	}

	private void OnAppearanceDragLeave(object? sender, DragEventArgs e)
	{
		ResetAppearanceDropBorder();
		if (DataContext is FloatingThingEditorViewModel vm)
			vm.ClearAppearanceDragHover();
	}

	private void OnAppearanceDragOver(object? sender, DragEventArgs e)
	{
		var canAccept = SpriteDragContext.CanAccept(e);
		e.DragEffects = canAccept ? DragDropEffects.Copy : DragDropEffects.None;

		if (!canAccept || DataContext is not FloatingThingEditorViewModel vm)
			return;

		var pos = e.GetPosition(AppearanceImageControl);
		vm.UpdateAppearanceDragHover(pos.X, pos.Y);
	}

	private void OnAppearanceDrop(object? sender, DragEventArgs e)
	{
		ResetAppearanceDropBorder();

		if (DataContext is not FloatingThingEditorViewModel vm)
			return;

		if (!SpriteDragContext.TryRead(e, out var sourcePanel, out var spriteId) || sourcePanel == null)
		{
			vm.ClearAppearanceDragHover();
			return;
		}

		var pos = e.GetPosition(AppearanceImageControl);
		vm.HandleSpriteDrop(sourcePanel, spriteId, pos.X, pos.Y);
		e.DragEffects = DragDropEffects.Copy;
		e.Handled = true;
	}

	private void ResetAppearanceDropBorder()
	{
		AppearanceDropTarget.BorderBrush = AppearanceDropBorderDefault;
		AppearanceDropTarget.BorderThickness = new Thickness(1);
	}

	private void AttachViewModel(FloatingThingEditorViewModel vm)
	{
		_pushingPatternValues = true;
		_vm = vm;
		_vm.PropertyChanged += OnViewModelPropertyChanged;
		EnsurePatternSpinnersHooked();
	}

	private void DetachViewModel()
	{
		if (_vm == null)
			return;

		_vm.PropertyChanged -= OnViewModelPropertyChanged;
		_vm = null;
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (_vm == null || string.IsNullOrEmpty(e.PropertyName))
			return;

		switch (e.PropertyName)
		{
			case nameof(FloatingThingEditorViewModel.TileWidth):
			case nameof(FloatingThingEditorViewModel.TileHeight):
			case nameof(FloatingThingEditorViewModel.CropSize):
			case nameof(FloatingThingEditorViewModel.LayerCount):
			case nameof(FloatingThingEditorViewModel.PatternXCount):
			case nameof(FloatingThingEditorViewModel.PatternYCount):
			case nameof(FloatingThingEditorViewModel.PatternZCount):
			case nameof(FloatingThingEditorViewModel.FrameCount):
				_pushingPatternValues = true;
				var capture = _vm;
				Dispatcher.UIThread.Post(() => { if (capture != null) PushPatternValuesToControls(capture); });
				break;
		}
	}

	private void EnsurePatternSpinnersHooked()
	{
		if (_patternSpinnersHooked)
			return;

		HookSpinner(PatternTileWidthNud, v => { if (_vm != null) _vm.TileWidth = v; });
		HookSpinner(PatternTileHeightNud, v => { if (_vm != null) _vm.TileHeight = v; });
		HookSpinner(PatternCropSizeNud, v => { if (_vm != null) _vm.CropSize = v; });
		HookSpinner(PatternLayerCountNud, v => { if (_vm != null) _vm.LayerCount = v; });
		HookSpinner(PatternXCountNud, v => { if (_vm != null) _vm.PatternXCount = v; });
		HookSpinner(PatternYCountNud, v => { if (_vm != null) _vm.PatternYCount = v; });
		HookSpinner(PatternZCountNud, v => { if (_vm != null) _vm.PatternZCount = v; });
		HookSpinner(PatternFrameCountNud, v => { if (_vm != null) _vm.FrameCount = v; });
		_patternSpinnersHooked = true;
	}

	private void HookSpinner(NumericUpDown spinner, Action<int> apply)
	{
		spinner.ValueChanged += (_, e) =>
		{
			if (_pushingPatternValues)
				return;

			apply((int)(e.NewValue ?? 1));
		};
	}

	private void PushPatternValuesToControls(FloatingThingEditorViewModel vm)
	{
		_pushingPatternValues = true;
		try
		{
			SetNud(PatternTileWidthNud, vm.TileWidth);
			SetNud(PatternTileHeightNud, vm.TileHeight);
			SetNud(PatternCropSizeNud, vm.CropSize);
			SetNud(PatternLayerCountNud, vm.LayerCount);
			SetNud(PatternXCountNud, vm.PatternXCount);
			SetNud(PatternYCountNud, vm.PatternYCount);
			SetNud(PatternZCountNud, vm.PatternZCount);
			SetNud(PatternFrameCountNud, vm.FrameCount);
		}
		finally
		{
			_pushingPatternValues = false;
		}
	}

	private static void SetNud(NumericUpDown nud, int value)
	{
		if (nud.Value == value)
			nud.Value = null;
		nud.Value = value;
	}

	private void RegisterResizeHandle(FloatingPanelInteraction interaction, string name, int direction)
	{
		var handle = this.FindControl<Border>(name);
		if (handle != null)
			interaction.RegisterResizeHandle(handle, direction);
	}
}
