using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Platform.Storage;

namespace NyxAssetsEditor.ViewModels.Common;

/// <summary>
/// Canonical file extensions, glob patterns, and Avalonia file-picker filters.
/// Prefer these over ad-hoc "*.png" / ".obd" literals so formats stay easy to extend.
/// </summary>
public static class SupportedFileFormats
{
	// --- Extensions (leading dot) ---
	public const string ExtPng = ".png";
	public const string ExtJpg = ".jpg";
	public const string ExtJpeg = ".jpeg";
	public const string ExtBmp = ".bmp";
	public const string ExtGif = ".gif";
	public const string ExtWebp = ".webp";

	public const string ExtSpr = ".spr";
	public const string ExtAssets = ".assets";
	public const string ExtDat = ".dat";
	public const string ExtJson = ".json";
	public const string ExtObd = ".obd";
	public const string ExtXml = ".xml";

	// --- Format keys used by export dialogs (no dot) ---
	public const string FormatPng = "png";
	public const string FormatJpg = "jpg";
	public const string FormatJpeg = "jpeg";
	public const string FormatBmp = "bmp";
	public const string FormatGif = "gif";
	public const string FormatObd = "obd";
	public const string FormatJson = "json";
	public const string FormatNyxThing = "nyx-thing";

	// --- Display names ---
	public const string NameImageFiles = "Image Files";
	public const string NamePngImage = "PNG Image";
	public const string NameJpegImage = "JPEG Image";
	public const string NameBmpImage = "BMP Image";
	public const string NameGifImage = "GIF Animation";
	public const string NameSpriteArchive = "Nyx Sprite Archive";
	public const string NameAssetArchive = "Nyx Asset Archive";
	public const string NameDatArchive = "Nyx Dat Archive";
	public const string NameThingsJson = "Nyx Things JSON";
	public const string NameThingObd = "Object Builder OBD";
	public const string NameAllSupportedArchives = "All Supported Archives";
	public const string NameAllSupported = "All Supported";
	public const string NameXmlFiles = "XML Files";

	/// <summary>Raster formats accepted for sprite/thing image import (Skia-decodable).</summary>
	public static readonly string[] ImageExtensions =
	[
		ExtPng, ExtJpg, ExtJpeg, ExtBmp, ExtWebp
	];

	public static readonly string[] ImagePatterns = ToPatterns(ImageExtensions);

	public static readonly string[] SpriteArchiveExtensions = [ExtSpr, ExtAssets];
	public static readonly string[] SpriteArchivePatterns = ToPatterns(SpriteArchiveExtensions);

	public static readonly string[] ThingsArchiveExtensions = [ExtDat, ExtJson];
	public static readonly string[] ThingsArchivePatterns = ToPatterns(ThingsArchiveExtensions);

	public static readonly string[] ThingExchangeExtensions = [ExtJson, ExtObd];
	public static readonly string[] ThingExchangePatterns = ToPatterns(ThingExchangeExtensions);

	public static readonly string[] ThingExchangeAndImageExtensions = [ExtJson, ExtObd, ExtPng, ExtJpg, ExtJpeg, ExtBmp, ExtWebp];
	public static readonly string[] ThingExchangeAndImagePatterns = ToPatterns(ThingExchangeAndImageExtensions);

	public static string ToPattern(string extension)
	{
		if (string.IsNullOrWhiteSpace(extension))
			return "*";
		return extension[0] == '*' ? extension : "*" + EnsureDot(extension);
	}

	public static string[] ToPatterns(params string[] extensions)
	{
		var patterns = new string[extensions.Length];
		for (var i = 0; i < extensions.Length; i++)
			patterns[i] = ToPattern(extensions[i]);
		return patterns;
	}

	public static string EnsureDot(string extension)
	{
		if (string.IsNullOrWhiteSpace(extension))
			return string.Empty;
		return extension[0] == '.' ? extension : "." + extension;
	}

	public static bool HasExtension(string? path, string extension) =>
		!string.IsNullOrWhiteSpace(path)
		&& path.EndsWith(EnsureDot(extension), StringComparison.OrdinalIgnoreCase);

	public static bool IsSupportedImagePath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return false;
		var ext = Path.GetExtension(path);
		foreach (var supported in ImageExtensions)
		{
			if (string.Equals(ext, supported, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	public static bool IsThingExchangePath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return false;
		var ext = Path.GetExtension(path);
		foreach (var supported in ThingExchangeExtensions)
		{
			if (string.Equals(ext, supported, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	/// <summary>Maps export format keys (png/jpg/bmp/…) to a canonical file extension.</summary>
	public static string NormalizeImageExportExtension(string? formatOrExtension)
	{
		var value = (formatOrExtension ?? FormatPng).Trim().ToLowerInvariant();
		if (value.Length > 0 && value[0] == '.')
			value = value[1..];

		return value switch
		{
			FormatJpg or FormatJpeg => ExtJpg,
			FormatBmp => ExtBmp,
			FormatGif => ExtGif,
			_ => ExtPng,
		};
	}

	public static bool IsObdFormat(string? format) =>
		string.Equals(format, FormatObd, StringComparison.OrdinalIgnoreCase)
		|| HasExtension(format, ExtObd);

	public static bool IsJsonThingFormat(string? format) =>
		string.Equals(format, FormatJson, StringComparison.OrdinalIgnoreCase)
		|| string.Equals(format, FormatNyxThing, StringComparison.OrdinalIgnoreCase)
		|| HasExtension(format, ExtJson);
}

/// <summary>Ready-made Avalonia <see cref="FilePickerFileType"/> filters backed by <see cref="SupportedFileFormats"/>.</summary>
public static class FilePickerFilters
{
	public static FilePickerFileType ImageFiles { get; } = new(SupportedFileFormats.NameImageFiles)
	{
		Patterns = SupportedFileFormats.ImagePatterns
	};

	public static FilePickerFileType PngImage { get; } = Single(SupportedFileFormats.NamePngImage, SupportedFileFormats.ExtPng);
	public static FilePickerFileType JpegImage { get; } = new(SupportedFileFormats.NameJpegImage)
	{
		Patterns = SupportedFileFormats.ToPatterns(SupportedFileFormats.ExtJpg, SupportedFileFormats.ExtJpeg)
	};
	public static FilePickerFileType BmpImage { get; } = Single(SupportedFileFormats.NameBmpImage, SupportedFileFormats.ExtBmp);
	public static FilePickerFileType GifImage { get; } = Single(SupportedFileFormats.NameGifImage, SupportedFileFormats.ExtGif);

	public static FilePickerFileType Spr { get; } = Single(SupportedFileFormats.NameSpriteArchive, SupportedFileFormats.ExtSpr);
	public static FilePickerFileType Assets { get; } = Single(SupportedFileFormats.NameAssetArchive, SupportedFileFormats.ExtAssets);
	public static FilePickerFileType Dat { get; } = Single(SupportedFileFormats.NameDatArchive, SupportedFileFormats.ExtDat);
	public static FilePickerFileType ThingsJson { get; } = Single(SupportedFileFormats.NameThingsJson, SupportedFileFormats.ExtJson);
	public static FilePickerFileType ThingObd { get; } = Single(SupportedFileFormats.NameThingObd, SupportedFileFormats.ExtObd);
	public static FilePickerFileType Xml { get; } = Single(SupportedFileFormats.NameXmlFiles, SupportedFileFormats.ExtXml);

	public static IReadOnlyList<FilePickerFileType> OpenImages { get; } = [ImageFiles];

	public static IReadOnlyList<FilePickerFileType> OpenSpriteArchives { get; } =
	[
		new FilePickerFileType(SupportedFileFormats.NameAllSupportedArchives)
		{
			Patterns = SupportedFileFormats.SpriteArchivePatterns
		},
		Spr,
		Assets
	];

	public static IReadOnlyList<FilePickerFileType> OpenThingsArchives { get; } =
	[
		new FilePickerFileType(SupportedFileFormats.NameAllSupportedArchives)
		{
			Patterns = SupportedFileFormats.ThingsArchivePatterns
		},
		Dat,
		ThingsJson
	];

	public static IReadOnlyList<FilePickerFileType> OpenThingExchange { get; } =
	[
		new FilePickerFileType(SupportedFileFormats.NameAllSupported)
		{
			Patterns = SupportedFileFormats.ThingExchangePatterns
		},
		ThingsJson,
		ThingObd
	];

	public static IReadOnlyList<FilePickerFileType> OpenThingExchangeAndImages { get; } =
	[
		new FilePickerFileType(SupportedFileFormats.NameAllSupported)
		{
			Patterns = SupportedFileFormats.ThingExchangeAndImagePatterns
		},
		ThingsJson,
		ThingObd,
		PngImage,
		ImageFiles
	];

	public static FilePickerFileType Single(string name, string extension) =>
		new(name) { Patterns = [SupportedFileFormats.ToPattern(extension)] };

	public static IReadOnlyList<FilePickerFileType> Only(FilePickerFileType type) => [type];

	public static IReadOnlyList<FilePickerFileType> ForArchiveExtension(string extension) =>
		SupportedFileFormats.EnsureDot(extension).ToLowerInvariant() switch
		{
			SupportedFileFormats.ExtSpr => Only(Spr),
			SupportedFileFormats.ExtAssets => Only(Assets),
			SupportedFileFormats.ExtDat => Only(Dat),
			SupportedFileFormats.ExtJson => Only(ThingsJson),
			SupportedFileFormats.ExtObd => Only(ThingObd),
			_ => Only(Single(extension.TrimStart('.').ToUpperInvariant() + " File", extension))
		};

	public static IReadOnlyList<FilePickerFileType> ForImageExport(string? format)
	{
		var ext = SupportedFileFormats.NormalizeImageExportExtension(format);
		return ext switch
		{
			SupportedFileFormats.ExtJpg => Only(JpegImage),
			SupportedFileFormats.ExtBmp => Only(BmpImage),
			SupportedFileFormats.ExtGif => Only(GifImage),
			_ => Only(PngImage),
		};
	}

	public static string ImageExportTitle(string subject, string? format)
	{
		var ext = SupportedFileFormats.NormalizeImageExportExtension(format);
		var kind = ext switch
		{
			SupportedFileFormats.ExtJpg => "JPEG",
			SupportedFileFormats.ExtBmp => "BMP",
			SupportedFileFormats.ExtGif => "GIF",
			_ => "PNG",
		};
		return $"Export {subject} as {kind}";
	}
}
