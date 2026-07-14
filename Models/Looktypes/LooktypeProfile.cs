using System;

namespace NyxAssetsEditor.Models.Looktypes;

public enum LooktypeAppearanceKind
{
	Outfit,
	Item,
}

public enum LooktypeDirection
{
	North = 0,
	East = 1,
	South = 2,
	West = 3,
}

public sealed class LooktypeProfile
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string Name { get; set; } = "New Looktype";
	public LooktypeAppearanceKind AppearanceKind { get; set; }
	public uint LookType { get; set; }
	public uint LookTypeEx { get; set; }
	public byte Head { get; set; }
	public byte Body { get; set; }
	public byte Legs { get; set; }
	public byte Feet { get; set; }
	public byte Addons { get; set; }
	public uint Mount { get; set; }
	public uint Corpse { get; set; }
	public LooktypeDirection Direction { get; set; } = LooktypeDirection.South;
	public bool AnimationEnabled { get; set; }
	public int AnimationPhase { get; set; }
	public int WalkIntervalMs { get; set; }
	public bool AutoRotate { get; set; }
	public int RotationIntervalMs { get; set; } = 1000;
	public bool IncludeNameInExport { get; set; } = true;
	public bool IncludePreviewSettings { get; set; }

	public LooktypeProfile Clone() => new()
	{
		Id = Id,
		Name = Name,
		AppearanceKind = AppearanceKind,
		LookType = LookType,
		LookTypeEx = LookTypeEx,
		Head = Head,
		Body = Body,
		Legs = Legs,
		Feet = Feet,
		Addons = Addons,
		Mount = Mount,
		Corpse = Corpse,
		Direction = Direction,
		AnimationEnabled = AnimationEnabled,
		AnimationPhase = AnimationPhase,
		WalkIntervalMs = WalkIntervalMs,
		AutoRotate = AutoRotate,
		RotationIntervalMs = RotationIntervalMs,
		IncludeNameInExport = IncludeNameInExport,
		IncludePreviewSettings = IncludePreviewSettings,
	};
}

public sealed class LooktypeLibraryDocument
{
	public int Version { get; set; } = 1;
	public System.Collections.Generic.List<LooktypeProfile> Profiles { get; set; } = new();
}
