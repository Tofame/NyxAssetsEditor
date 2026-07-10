using Avalonia;
using Avalonia.Controls;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;

namespace NyxAssetsEditor.Views.ArchiveLoaders;

public sealed class BatchApplyField : ContentControl
{
	public static readonly StyledProperty<string> PropertyNameProperty =
		AvaloniaProperty.Register<BatchApplyField, string>(nameof(PropertyName), string.Empty);
	public static readonly StyledProperty<bool> IsAppliedProperty =
		AvaloniaProperty.Register<BatchApplyField, bool>(nameof(IsApplied));
	public static readonly StyledProperty<bool> ShowApplyProperty =
		AvaloniaProperty.Register<BatchApplyField, bool>(nameof(ShowApply));
	public static readonly StyledProperty<bool> IsFieldEnabledProperty =
		AvaloniaProperty.Register<BatchApplyField, bool>(nameof(IsFieldEnabled), true);

	public string PropertyName { get => GetValue(PropertyNameProperty); set => SetValue(PropertyNameProperty, value); }
	public bool IsApplied { get => GetValue(IsAppliedProperty); set => SetValue(IsAppliedProperty, value); }
	public bool ShowApply { get => GetValue(ShowApplyProperty); private set => SetValue(ShowApplyProperty, value); }
	public bool IsFieldEnabled { get => GetValue(IsFieldEnabledProperty); private set => SetValue(IsFieldEnabledProperty, value); }

	public BatchApplyField()
	{
		DataContextChanged += (_, _) => Refresh();
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (change.Property == IsAppliedProperty)
		{
			if (DataContext is FloatingThingEditorViewModel vm && vm.IsBatchEditor)
				vm.SetBatchOverride(PropertyName, IsApplied);
			Refresh();
		}
	}

	private void Refresh()
	{
		ShowApply = DataContext is FloatingThingEditorViewModel { IsBatchEditor: true };
		IsFieldEnabled = !ShowApply || IsApplied;
	}
}
