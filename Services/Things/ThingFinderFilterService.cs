using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NyxAssets.Things;

namespace NyxAssetsEditor.Services.Things;

public enum ThingFinderFieldSource
{
	Thing,
	Pattern,
	ExtraProperty,
}

public enum ThingFinderOperator
{
	IsSet,
	IsNotSet,
	Equals,
	Exists,
	Missing,
}

public enum ThingFinderValueKind
{
	Boolean,
	Number,
	Text,
	Enum,
}

public sealed record ThingFinderNumericMetadata(
	decimal Minimum,
	decimal Maximum,
	decimal Increment,
	decimal DefaultValue,
	bool AllowsDecimal = false);

public sealed record ThingFinderFieldDescriptor(
	string Key,
	string DisplayName,
	ThingFinderFieldSource Source,
	ThingFinderValueKind ValueKind,
	ThingFinderNumericMetadata? Numeric = null)
{
	public override string ToString() => DisplayName;
}

public sealed class ThingFinderCriterion
{
	public ThingFinderFieldSource Source { get; init; }
	public string FieldName { get; init; } = string.Empty;
	public ThingFinderOperator Operator { get; init; }
	public string Value { get; init; } = string.Empty;
}

public static class ThingFinderFilterService
{
	private static readonly IReadOnlyDictionary<string, PropertyInfo> ThingProperties = typeof(ThingType)
		.GetProperties(BindingFlags.Instance | BindingFlags.Public)
		.Where(property => property.CanRead)
		.ToDictionary(property => property.Name, StringComparer.Ordinal);

	private static readonly IReadOnlyDictionary<string, PropertyInfo> PatternProperties = typeof(ThingFrameGroup)
		.GetProperties(BindingFlags.Instance | BindingFlags.Public)
		.Where(property => property.CanRead)
		.ToDictionary(property => property.Name, StringComparer.Ordinal);

	private static readonly (string Name, string DisplayName)[] SupportedPatternFields =
	{
		(nameof(ThingFrameGroup.Width), "Width"),
		(nameof(ThingFrameGroup.Height), "Height"),
		(nameof(ThingFrameGroup.ExactSize), "Crop Size"),
		(nameof(ThingFrameGroup.Layers), "Layers"),
		(nameof(ThingFrameGroup.PatternX), "Pattern X"),
		(nameof(ThingFrameGroup.PatternY), "Pattern Y"),
		(nameof(ThingFrameGroup.PatternZ), "Pattern Z"),
		(nameof(ThingFrameGroup.Frames), "Animations"),
	};

	private static readonly IReadOnlyDictionary<string, ThingFinderNumericMetadata> NumericOverrides =
		new Dictionary<string, ThingFinderNumericMetadata>(StringComparer.Ordinal)
		{
			[nameof(ThingType.GroundSpeed)] = Integer(0, 65535),
			[nameof(ThingType.LightColor)] = Integer(0, 215),
			[nameof(ThingType.LightLevel)] = Integer(0, 255),
			[nameof(ThingType.MiniMapColor)] = Integer(0, 215),
			[nameof(ThingType.OffsetX)] = Integer(-1024, 1024),
			[nameof(ThingType.OffsetY)] = Integer(-1024, 1024),
			[nameof(ThingType.Elevation)] = Integer(0, 255),
			[nameof(ThingType.MarketTradeAs)] = Integer(0, 65535),
			[nameof(ThingType.MarketShowAs)] = Integer(0, 65535),
			[nameof(ThingType.MarketRestrictProfession)] = Integer(0, 65535),
			[nameof(ThingType.MarketRestrictLevel)] = Integer(0, 65535),
			[nameof(ThingType.MaxTextLength)] = Integer(0, 65535),
			[nameof(ThingType.ClothSlot)] = Integer(0, 65535),
			[nameof(ThingFrameGroup.Width)] = Integer(1, 32, 1),
			[nameof(ThingFrameGroup.Height)] = Integer(1, 32, 1),
			[nameof(ThingFrameGroup.ExactSize)] = Integer(1, 64, 1),
			[nameof(ThingFrameGroup.Layers)] = Integer(1, 16, 1),
			[nameof(ThingFrameGroup.PatternX)] = Integer(1, 32, 1),
			[nameof(ThingFrameGroup.PatternY)] = Integer(1, 32, 1),
			[nameof(ThingFrameGroup.PatternZ)] = Integer(1, 16, 1),
			[nameof(ThingFrameGroup.Frames)] = Integer(1, 60, 1),
		};

	public static IReadOnlyList<ThingFinderFieldDescriptor> GetFieldDescriptors()
	{
		var descriptors = new List<ThingFinderFieldDescriptor>();

		descriptors.AddRange(ThingProperties.Values
			.Where(property => property.Name is not nameof(ThingType.Id)
				and not nameof(ThingType.Kind)
				and not nameof(ThingType.FrameGroups)
				and not nameof(ThingType.ExtraProperties))
			.Where(property => IsScalar(property.PropertyType))
			.OrderBy(property => Humanize(property.Name), StringComparer.OrdinalIgnoreCase)
			.Select(property => new ThingFinderFieldDescriptor(
				property.Name,
				Humanize(property.Name),
				ThingFinderFieldSource.Thing,
				GetValueKind(property.PropertyType),
				GetNumericMetadata(property.Name, property.PropertyType))));

		descriptors.AddRange(SupportedPatternFields.Select(field => new ThingFinderFieldDescriptor(
			field.Name,
			$"Pattern: {field.DisplayName}",
			ThingFinderFieldSource.Pattern,
			ThingFinderValueKind.Number,
			NumericOverrides[field.Name])));

		return descriptors;
	}

	public static ThingFinderCriterion? CreateCriterion(
		ThingFinderFieldDescriptor descriptor,
		bool isActive,
		bool booleanValue,
		string? value)
	{
		if (!isActive) return null;
		if (descriptor.ValueKind == ThingFinderValueKind.Boolean)
		{
			return new ThingFinderCriterion
			{
				Source = descriptor.Source,
				FieldName = descriptor.Key,
				Operator = descriptor.Source == ThingFinderFieldSource.ExtraProperty
					? booleanValue ? ThingFinderOperator.Exists : ThingFinderOperator.Missing
					: booleanValue ? ThingFinderOperator.IsSet : ThingFinderOperator.IsNotSet,
			};
		}

		var trimmedValue = value?.Trim() ?? string.Empty;
		if (trimmedValue.Length == 0) return null;
		return new ThingFinderCriterion
		{
			Source = descriptor.Source,
			FieldName = descriptor.Key,
			Operator = ThingFinderOperator.Equals,
			Value = trimmedValue,
		};
	}

	public static IReadOnlyList<ThingType> Filter(
		IEnumerable<ThingType> things,
		ThingKind kind,
		IReadOnlyCollection<ThingFinderCriterion> criteria,
		int frameGroupIndex)
	{
		var candidates = things.Where(thing => thing.Kind == kind);
		if (criteria.Count == 0)
			return candidates.OrderBy(thing => thing.Id).ToList();

		return candidates
			.Where(thing => criteria.All(criterion => Matches(thing, criterion, frameGroupIndex)))
			.OrderBy(thing => thing.Id)
			.ToList();
	}

	public static bool Matches(
		ThingType thing,
		ThingFinderCriterion criterion,
		int frameGroupIndex)
	{
		object? actual;
		Type actualType;

		switch (criterion.Source)
		{
			case ThingFinderFieldSource.ExtraProperty:
			{
				string? extraValue = null;
				var exists = !string.IsNullOrWhiteSpace(criterion.FieldName)
					&& thing.ExtraProperties.TryGetValue(criterion.FieldName, out extraValue);
				if (criterion.Operator == ThingFinderOperator.Exists) return exists;
				if (criterion.Operator == ThingFinderOperator.Missing) return !exists;
				if (!exists) return false;
				actual = extraValue;
				actualType = typeof(string);
				break;
			}
			case ThingFinderFieldSource.Pattern:
			{
				if (frameGroupIndex < 0 || frameGroupIndex >= thing.FrameGroups.Count
					|| !PatternProperties.TryGetValue(criterion.FieldName, out var patternProperty))
					return false;
				actual = patternProperty.GetValue(thing.FrameGroups[frameGroupIndex]);
				actualType = patternProperty.PropertyType;
				break;
			}
			default:
			{
				if (!ThingProperties.TryGetValue(criterion.FieldName, out var thingProperty))
					return false;
				actual = thingProperty.GetValue(thing);
				actualType = thingProperty.PropertyType;
				break;
			}
		}

		return Compare(actual, actualType, criterion);
	}

	public static string GetValueText(
		ThingType thing,
		ThingFinderCriterion criterion,
		int frameGroupIndex)
	{
		if (criterion.Source == ThingFinderFieldSource.ExtraProperty)
			return thing.ExtraProperties.TryGetValue(criterion.FieldName, out var value)
				? $"{criterion.FieldName}={value}"
				: $"{criterion.FieldName}=<missing>";

		if (criterion.Source == ThingFinderFieldSource.Pattern)
		{
			if (frameGroupIndex < 0 || frameGroupIndex >= thing.FrameGroups.Count
				|| !PatternProperties.TryGetValue(criterion.FieldName, out var property))
				return $"Group {frameGroupIndex}: <missing>";
			return $"Group {frameGroupIndex} {Humanize(criterion.FieldName)}={Format(property.GetValue(thing.FrameGroups[frameGroupIndex]))}";
		}

		return ThingProperties.TryGetValue(criterion.FieldName, out var thingProperty)
			? $"{Humanize(criterion.FieldName)}={Format(thingProperty.GetValue(thing))}"
			: criterion.FieldName;
	}

	private static bool Compare(object? actual, Type declaredType, ThingFinderCriterion criterion)
	{
		var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
		if (type == typeof(bool))
		{
			if (actual is not bool boolean) return false;
			return criterion.Operator switch
			{
				ThingFinderOperator.IsSet => boolean,
				ThingFinderOperator.IsNotSet => !boolean,
				_ => false,
			};
		}

		if (type.IsEnum)
		{
			if (actual == null || !Enum.TryParse(type, criterion.Value, true, out var expected)) return false;
			return criterion.Operator == ThingFinderOperator.Equals && actual.Equals(expected);
		}

		if (IsNumeric(type))
		{
			if (actual == null || !TryDecimal(actual, out var number)
				|| !decimal.TryParse(criterion.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var expected))
				return false;
			return criterion.Operator == ThingFinderOperator.Equals && number == expected;
		}

		var text = Format(actual);
		var value = criterion.Value ?? string.Empty;
		return criterion.Operator == ThingFinderOperator.Equals
			&& text.Equals(value, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsScalar(Type type)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		return type == typeof(string) || type == typeof(bool) || type.IsEnum || IsNumeric(type);
	}

	private static ThingFinderValueKind GetValueKind(Type type)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		if (type == typeof(bool)) return ThingFinderValueKind.Boolean;
		if (type.IsEnum) return ThingFinderValueKind.Enum;
		return IsNumeric(type) ? ThingFinderValueKind.Number : ThingFinderValueKind.Text;
	}

	private static ThingFinderNumericMetadata? GetNumericMetadata(string name, Type type)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		if (!IsNumeric(type)) return null;
		if (NumericOverrides.TryGetValue(name, out var metadata)) return metadata;

		return Type.GetTypeCode(type) switch
		{
			TypeCode.Byte => Integer(byte.MinValue, byte.MaxValue),
			TypeCode.SByte => Integer(sbyte.MinValue, sbyte.MaxValue),
			TypeCode.UInt16 => Integer(ushort.MinValue, ushort.MaxValue),
			TypeCode.Int16 => Integer(short.MinValue, short.MaxValue),
			TypeCode.UInt32 or TypeCode.UInt64 => Integer(0, 65535),
			TypeCode.Int32 or TypeCode.Int64 => Integer(-65535, 65535),
			TypeCode.Single or TypeCode.Double or TypeCode.Decimal => new ThingFinderNumericMetadata(
				-1000000, 1000000, 0.1m, 0, AllowsDecimal: true),
			_ => null,
		};
	}

	private static ThingFinderNumericMetadata Integer(decimal minimum, decimal maximum, decimal defaultValue = 0) =>
		new(minimum, maximum, 1, defaultValue);

	private static bool IsNumeric(Type type) => Type.GetTypeCode(type) is
		TypeCode.Byte or TypeCode.SByte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64
		or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

	private static bool TryDecimal(object value, out decimal result)
	{
		try
		{
			result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
			return true;
		}
		catch
		{
			result = 0;
			return false;
		}
	}

	private static string Format(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

	private static string Humanize(string value)
	{
		if (string.IsNullOrEmpty(value)) return value;
		var chars = new List<char>(value.Length + 8) { value[0] };
		for (var i = 1; i < value.Length; i++)
		{
			if (char.IsUpper(value[i]) && !char.IsUpper(value[i - 1])) chars.Add(' ');
			chars.Add(value[i]);
		}
		return new string(chars.ToArray());
	}
}
