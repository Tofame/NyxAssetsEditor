using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyxAssets.Things;
using NyxAssetsEditor.Services.Things;
using NyxAssetsEditor.ViewModels.Core;

namespace NyxAssetsEditor.ViewModels.ArchiveLoaders;

public abstract partial class CustomFlagViewModelBase : ViewModelBase
{
	protected readonly FloatingThingEditorViewModel Editor;
	protected readonly CustomFlagDefinition Definition;

	public string Name => Definition.Name;
	public string Label => Definition.Label;
	public string? Description => Definition.Description;
	public string FlagType => Definition.Type;
	public bool IsLocked => Definition.Locked;

	public int UsageCount => Editor.GetFlagUsageCount(Name);
	public bool CanDelete => !IsLocked && UsageCount <= 1;

	public bool IsLowUsage => UsageCount >= 1 && UsageCount <= 10;
	public string LowUsageWarning => IsLowUsage ? $"Flag may be redundant, it is not used in a lot of items (x{UsageCount})" : string.Empty;
	public string LabelColor => IsLowUsage ? "#FFD54F" : "#AAA";

	public string DeleteTooltip
	{
		get
		{
			string usageStr = UsageCount == 1 ? "Used in 1 item" : $"Used in {UsageCount} items";
			string warn = IsLowUsage ? $" — {LowUsageWarning}" : string.Empty;
			if (IsLocked) return $"Flag is locked in TOML schema ({usageStr}, unremovable){warn}";
			if (UsageCount > 1) return $"{usageStr} (remove disabled){warn}";
			return $"Remove flag from this item{warn}";
		}
	}

	public string LockTooltip
	{
		get
		{
			if (!IsLocked) return string.Empty;
			string usageStr = UsageCount == 1 ? "Used in 1 item" : $"Used in {UsageCount} items";
			string warn = IsLowUsage ? $" — {LowUsageWarning}" : string.Empty;
			return $"Locked in TOML schema ({usageStr}){warn}";
		}
	}

	[RelayCommand]
	public void RemoveFlag()
	{
		if (!CanDelete) return;
		Editor.RemoveSchemaFlag(Name);
	}

	protected CustomFlagViewModelBase(CustomFlagDefinition definition, FloatingThingEditorViewModel editor)
	{
		Definition = definition;
		Editor = editor;
	}

	private static readonly Dictionary<string, System.Reflection.PropertyInfo> PropertyMap =
		typeof(ThingType).GetProperties()
			.ToDictionary(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..], p => p, StringComparer.OrdinalIgnoreCase);

	protected string? GetRawValue()
	{
		if (Editor.Thing.ExtraProperties.TryGetValue(Name, out var extraVal))
			return extraVal;

		if (PropertyMap.TryGetValue(Name, out var prop))
		{
			var val = prop.GetValue(Editor.Thing);
			if (val == null) return null;
			if (val is bool b) return b ? "true" : "false";
			return val.ToString();
		}

		return null;
	}

	protected void SetRawValue(string? value)
	{
		Editor.Thing.ExtraProperties.Remove(Name);

		if (PropertyMap.TryGetValue(Name, out var prop) && prop.CanWrite)
		{
			if (prop.PropertyType == typeof(bool))
			{
				bool b = value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
				prop.SetValue(Editor.Thing, b);
			}
			else if (prop.PropertyType == typeof(uint) && uint.TryParse(value, out var u))
			{
				prop.SetValue(Editor.Thing, u);
			}
			else if (prop.PropertyType == typeof(int) && int.TryParse(value, out var i))
			{
				prop.SetValue(Editor.Thing, i);
			}
			else if (prop.PropertyType == typeof(string))
			{
				prop.SetValue(Editor.Thing, value);
			}
		}

		if (value != null)
			Editor.Thing.ExtraProperties[Name] = value;

		Editor.RequestApplyToCatalog();
	}

	public abstract void Refresh();
}

public partial class BoolFlagViewModel : CustomFlagViewModelBase
{
	public BoolFlagViewModel(CustomFlagDefinition definition, FloatingThingEditorViewModel editor)
		: base(definition, editor) { }

	public bool IsChecked
	{
		get
		{
			var raw = GetRawValue();
			if (raw != null) return raw.Equals("true", StringComparison.OrdinalIgnoreCase);
			return Definition.Default?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
		}
		set
		{
			if (value)
				SetRawValue("true");
			else
				Editor.Thing.ExtraProperties.Remove(Name);
			OnPropertyChanged();
			Editor.RequestApplyToCatalog();
		}
	}

	public override void Refresh() => OnPropertyChanged(nameof(IsChecked));
}

public partial class IntFlagViewModel : CustomFlagViewModelBase
{
	public decimal MinValue { get; }
	public decimal MaxValue { get; }

	public IntFlagViewModel(CustomFlagDefinition definition, FloatingThingEditorViewModel editor)
		: base(definition, editor)
	{
		MinValue = definition.Min ?? 0;
		MaxValue = definition.Max ?? int.MaxValue;
	}

	public decimal? Value
	{
		get
		{
			var raw = GetRawValue();
			if (raw != null && int.TryParse(raw, out var v)) return Math.Clamp(v, (int)MinValue, (int)MaxValue);
			if (Definition.Default != null && int.TryParse(Definition.Default, out var d)) return Math.Clamp(d, (int)MinValue, (int)MaxValue);
			return MinValue;
		}
		set
		{
			int clamped = Math.Clamp((int)(value ?? MinValue), (int)MinValue, (int)MaxValue);
			SetRawValue(clamped.ToString());
			OnPropertyChanged();
		}
	}

	public override void Refresh() => OnPropertyChanged(nameof(Value));
}

public partial class StringFlagViewModel : CustomFlagViewModelBase
{
	public StringFlagViewModel(CustomFlagDefinition definition, FloatingThingEditorViewModel editor)
		: base(definition, editor) { }

	public string Value
	{
		get => GetRawValue() ?? Definition.Default ?? string.Empty;
		set
		{
			if (string.IsNullOrEmpty(value))
				SetRawValue(null);
			else
				SetRawValue(value);
			OnPropertyChanged();
		}
	}

	public override void Refresh() => OnPropertyChanged(nameof(Value));
}

public partial class EnumFlagViewModel : CustomFlagViewModelBase
{
	public List<string> Options { get; }
	public bool IsRadio { get; }
	public bool IsDropdown => !IsRadio;

	public const string NoneOption = "(None)";

	public EnumFlagViewModel(CustomFlagDefinition definition, FloatingThingEditorViewModel editor)
		: base(definition, editor)
	{
		var list = new List<string> { NoneOption };
		if (definition.Options != null)
		{
			foreach (var opt in definition.Options)
			{
				if (!string.Equals(opt, NoneOption, StringComparison.OrdinalIgnoreCase))
					list.Add(opt);
			}
		}
		Options = list;
		IsRadio = string.Equals(definition.GroupType, "radio", StringComparison.OrdinalIgnoreCase);
	}

	public string? SelectedOption
	{
		get
		{
			var raw = GetRawValue();
			if (raw != null)
			{
				var match = Options.FirstOrDefault(o => string.Equals(o, raw, StringComparison.OrdinalIgnoreCase));
				if (match != null) return match;
			}
			if (Definition.Default != null)
			{
				var matchDef = Options.FirstOrDefault(o => string.Equals(o, Definition.Default, StringComparison.OrdinalIgnoreCase));
				if (matchDef != null) return matchDef;
			}
			return NoneOption;
		}
		set
		{
			if (value == null || string.Equals(value, NoneOption, StringComparison.OrdinalIgnoreCase))
				SetRawValue(null);
			else
				SetRawValue(value);

			OnPropertyChanged();
			OnPropertyChanged(nameof(SelectedIndex));
		}
	}

	public int SelectedIndex
	{
		get
		{
			var sel = SelectedOption;
			if (sel == null) return -1;
			return Options.FindIndex(o => string.Equals(o, sel, StringComparison.OrdinalIgnoreCase));
		}
		set
		{
			if (value >= 0 && value < Options.Count)
				SelectedOption = Options[value];
		}
	}

	public override void Refresh()
	{
		OnPropertyChanged(nameof(SelectedOption));
		OnPropertyChanged(nameof(SelectedIndex));
	}
}

public class FlagGroupViewModel : ViewModelBase
{
	public string GroupKey { get; }
	public string Label { get; }
	public int Order { get; }
	public ObservableCollection<CustomFlagViewModelBase> Flags { get; } = new();

	public FlagGroupViewModel(string key, string label, int order)
	{
		GroupKey = key;
		Label = label;
		Order = order;
	}
}

public partial class AdHocFlagViewModel : ViewModelBase
{
	private readonly string _name;
	private readonly FloatingThingEditorViewModel _editor;

	public string Name => _name;

	private int _typeIndex;
	public int TypeIndex
	{
		get => _typeIndex;
		set
		{
			if (SetProperty(ref _typeIndex, value))
			{
				OnPropertyChanged(nameof(IsBool));
				OnPropertyChanged(nameof(IsInt));
				OnPropertyChanged(nameof(IsString));
			}
		}
	}

	public bool IsBool => TypeIndex == 0;
	public bool IsInt => TypeIndex == 1;
	public bool IsString => TypeIndex == 2;

	public bool BoolValue
	{
		get => _editor.Thing.ExtraProperties.TryGetValue(_name, out var val) && val.Equals("true", StringComparison.OrdinalIgnoreCase);
		set
		{
			if (value) _editor.Thing.ExtraProperties[_name] = "true";
			else _editor.Thing.ExtraProperties.Remove(_name);
			OnPropertyChanged();
			_editor.RequestApplyToCatalog();
		}
	}

	public decimal? IntValue
	{
		get
		{
			if (_editor.Thing.ExtraProperties.TryGetValue(_name, out var val) && int.TryParse(val, out var v)) return v;
			return 0;
		}
		set
		{
			_editor.Thing.ExtraProperties[_name] = ((int)(value ?? 0)).ToString();
			OnPropertyChanged();
			_editor.RequestApplyToCatalog();
		}
	}

	public string StringValue
	{
		get => _editor.Thing.ExtraProperties.TryGetValue(_name, out var val) ? val : string.Empty;
		set
		{
			if (string.IsNullOrEmpty(value)) _editor.Thing.ExtraProperties.Remove(_name);
			else _editor.Thing.ExtraProperties[_name] = value;
			OnPropertyChanged();
			_editor.RequestApplyToCatalog();
		}
	}

	public AdHocFlagViewModel(string name, FloatingThingEditorViewModel editor, int initialType = 0)
	{
		_name = name;
		_editor = editor;
		_typeIndex = initialType;

		if (editor.Thing.ExtraProperties.TryGetValue(name, out var val))
		{
			if (val.Equals("true", StringComparison.OrdinalIgnoreCase) || val.Equals("false", StringComparison.OrdinalIgnoreCase))
				_typeIndex = 0;
			else if (int.TryParse(val, out _))
				_typeIndex = 1;
			else
				_typeIndex = 2;
		}
	}

	[RelayCommand]
	private void Remove()
	{
		_editor.RemoveAdHocFlag(_name);
	}
}
