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
	public static readonly StyledProperty<bool> AlwaysShowApplyProperty =
		AvaloniaProperty.Register<BatchApplyField, bool>(nameof(AlwaysShowApply));
	public static readonly StyledProperty<string> ApplyToolTipProperty =
		AvaloniaProperty.Register<BatchApplyField, string>(nameof(ApplyToolTip), "Apply this field to all selected things");

	public string PropertyName { get => GetValue(PropertyNameProperty); set => SetValue(PropertyNameProperty, value); }
	public bool IsApplied { get => GetValue(IsAppliedProperty); set => SetValue(IsAppliedProperty, value); }
	public bool ShowApply { get => GetValue(ShowApplyProperty); private set => SetValue(ShowApplyProperty, value); }
	public bool IsFieldEnabled { get => GetValue(IsFieldEnabledProperty); private set => SetValue(IsFieldEnabledProperty, value); }
	public bool AlwaysShowApply { get => GetValue(AlwaysShowApplyProperty); set => SetValue(AlwaysShowApplyProperty, value); }
	public string ApplyToolTip { get => GetValue(ApplyToolTipProperty); set => SetValue(ApplyToolTipProperty, value); }

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
		else if (change.Property == AlwaysShowApplyProperty)
		{
			Refresh();
		}
	}

	private void Refresh()
	{
		ShowApply = AlwaysShowApply || DataContext is FloatingThingEditorViewModel { IsBatchEditor: true };
		IsFieldEnabled = !ShowApply || IsApplied;
	}
}
