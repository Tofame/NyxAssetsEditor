# DAT Protocol Flags Configuration

You can customize the visible flags and their labels in the Thing Editor for each protocol version (V1 to V6).

## Protocol Versions Map

The folders `v1` to `v6` correspond to the following Tibia client protocol version ranges (as defined in Object Builder):

- **v1**: Client versions 7.10 - 7.30 (internal version < 740)
- **v2**: Client versions 7.40 - 7.50 (internal version < 755)
- **v3**: Client versions 7.55 - 7.72 (internal version < 780)
- **v4**: Client versions 7.80 - 8.54 (internal version < 860)
- **v5**: Client versions 8.60 - 9.86 (internal version < 1010)
- **v6**: Client versions 10.10+ (internal version >= 1010, e.g. 10.98)

## Directories

Default files are located in:
`Assets/datProtocols/v{1-6}/flags.toml`

## Overriding Flags

To customize the layout or rename properties without changing the source code:
1. Create a `flags_override.toml` file in the same directory as the default `flags.toml` (e.g. `Assets/datProtocols/v5/flags_override.toml`).
2. Define the active flags and their custom labels under the `[flags]` table.
3. This file is ignored by git (`*_override*`).

## TOML File Format

```toml
[flags]
IsContainer = "Container"
Stackable = "Stackable"
ForceUse = "Force Use"
MultiUse = "Multi Use"
IsFluidContainer = "Fluid Container"
IsFluid = "Fluid"
IsUnpassable = "Unpassable"
IsUnmoveable = "Unmoveable"
BlockMissile = "Block Missile"
BlockPathfind = "Block Pathfinder"
FloorChange = "Floor Change"
NoMoveAnimation = "No Move Animation"
Pickupable = "Pickupable"
Hangable = "Hangable"
IsHorizontal = "Hook East"
IsVertical = "Hook South"
Rotatable = "Rotatable"
DontHide = "Don't Hide"
IsTranslucent = "Translucent"
IsLyingObject = "Lying Object"
IsFullGround = "Full Ground"
IgnoreLook = "Ignore Look"
Usable = "Useable"
Wrappable = "Wrappable"
Unwrappable = "Unwrappable"
BottomEffect = "Top Effect"
AnimateAlways = "Animate Always"
```

## Behavior rules

- **Visibility**: Only keys defined in the active TOML file will be visible in the editor.
- **Labels**: The value assigned to the key defines the ToggleSwitch label in the UI.
- **Backend Properties**: The keys must exactly match the C# property names from the `ThingType` class (listed below).

## Valid Property Keys

| TOML Key | Default Label | Category / Description |
| :--- | :--- | :--- |
| `IsContainer` | "Container" | Left Column Flags |
| `Stackable` | "Stackable" | Left Column Flags (Items) |
| `ForceUse` | "Force Use" | Left Column Flags |
| `MultiUse` | "Multi Use" | Left Column Flags |
| `IsFluidContainer` | "Fluid Container" | Left Column Flags |
| `IsFluid` | "Fluid" | Left Column Flags |
| `IsUnpassable` | "Unpassable" | Left Column Flags |
| `IsUnmoveable` | "Unmoveable" | Left Column Flags |
| `BlockMissile` | "Block Missile" | Left Column Flags |
| `BlockPathfind` | "Block Pathfinder" | Left Column Flags |
| `FloorChange` | "Floor Change" | Left Column Flags |
| `NoMoveAnimation` | "No Move Animation" | Right Column Flags |
| `Pickupable` | "Pickupable" | Right Column Flags |
| `Hangable` | "Hangable" | Right Column Flags |
| `IsHorizontal` | "Hook East" | Right Column Flags |
| `IsVertical` | "Hook South" | Right Column Flags |
| `Rotatable` | "Rotatable" | Right Column Flags |
| `DontHide` | "Don't Hide" | Right Column Flags |
| `IsTranslucent` | "Translucent" | Right Column Flags |
| `IsLyingObject` | "Lying Object" | Right Column Flags |
| `IsFullGround` | "Full Ground" | Right Column Flags |
| `IgnoreLook` | "Ignore Look" | Right Column Flags |
| `Usable` | "Useable" | Right Column Flags |
| `Wrappable` | "Wrappable" | Right Column Flags |
| `Unwrappable` | "Unwrappable" | Right Column Flags |
| `BottomEffect` | "Top Effect" | Properties tab (Effects only) |
| `AnimateAlways` | "Animate Always" | Properties tab (Outfits only) |
