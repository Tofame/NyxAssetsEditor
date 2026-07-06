using System;
using System.Linq;
using NyxAssets.Things;

namespace NyxAssetsEditor.Services.Exchange;

public static class ThingCloner
{
    public static ThingType Clone(ThingType source, uint newId)
    {
        var clone = new ThingType
        {
            Id = newId,
            Kind = source.Kind,
            IsGround = source.IsGround,
            GroundSpeed = source.GroundSpeed,
            IsGroundBorder = source.IsGroundBorder,
            IsOnBottom = source.IsOnBottom,
            IsOnTop = source.IsOnTop,
            IsContainer = source.IsContainer,
            Stackable = source.Stackable,
            ForceUse = source.ForceUse,
            MultiUse = source.MultiUse,
            HasCharges = source.HasCharges,
            Writable = source.Writable,
            WritableOnce = source.WritableOnce,
            MaxTextLength = source.MaxTextLength,
            IsFluidContainer = source.IsFluidContainer,
            IsFluid = source.IsFluid,
            IsUnpassable = source.IsUnpassable,
            IsUnmoveable = source.IsUnmoveable,
            BlockMissile = source.BlockMissile,
            BlockPathfind = source.BlockPathfind,
            NoMoveAnimation = source.NoMoveAnimation,
            Pickupable = source.Pickupable,
            Hangable = source.Hangable,
            IsVertical = source.IsVertical,
            IsHorizontal = source.IsHorizontal,
            Rotatable = source.Rotatable,
            HasLight = source.HasLight,
            LightLevel = source.LightLevel,
            LightColor = source.LightColor,
            DontHide = source.DontHide,
            IsTranslucent = source.IsTranslucent,
            FloorChange = source.FloorChange,
            HasOffset = source.HasOffset,
            OffsetX = source.OffsetX,
            OffsetY = source.OffsetY,
            HasElevation = source.HasElevation,
            Elevation = source.Elevation,
            IsLyingObject = source.IsLyingObject,
            AnimateAlways = source.AnimateAlways,
            MiniMap = source.MiniMap,
            MiniMapColor = source.MiniMapColor,
            IsLensHelp = source.IsLensHelp,
            LensHelp = source.LensHelp,
            IsFullGround = source.IsFullGround,
            IgnoreLook = source.IgnoreLook,
            Cloth = source.Cloth,
            ClothSlot = source.ClothSlot,
            IsMarketItem = source.IsMarketItem,
            MarketName = source.MarketName,
            MarketCategory = source.MarketCategory,
            MarketTradeAs = source.MarketTradeAs,
            MarketShowAs = source.MarketShowAs,
            MarketRestrictProfession = source.MarketRestrictProfession,
            MarketRestrictLevel = source.MarketRestrictLevel,
            HasDefaultAction = source.HasDefaultAction,
            DefaultAction = source.DefaultAction,
            Wrappable = source.Wrappable,
            Unwrappable = source.Unwrappable,
            BottomEffect = source.BottomEffect,
            DontCenterOutfit = source.DontCenterOutfit,
            Usable = source.Usable,
        };

        foreach (var group in source.FrameGroups)
            clone.FrameGroups.Add(CloneFrameGroup(group));

        foreach (var pair in source.ExtraProperties)
            clone.ExtraProperties[pair.Key] = pair.Value;

        return clone;
    }

    private static ThingFrameGroup CloneFrameGroup(ThingFrameGroup source)
    {
        var clone = new ThingFrameGroup
        {
            GroupTypeId = source.GroupTypeId,
            Width = source.Width,
            Height = source.Height,
            ExactSize = source.ExactSize,
            Layers = source.Layers,
            PatternX = source.PatternX,
            PatternY = source.PatternY,
            PatternZ = source.PatternZ,
            Frames = source.Frames,
            IsAnimation = source.IsAnimation,
            AnimationMode = source.AnimationMode,
            LoopCount = source.LoopCount,
            StartFrame = source.StartFrame,
            SpriteIds = (uint[])source.SpriteIds.Clone(),
        };

        if (source.FrameTimings != null)
        {
            clone.FrameTimings = source.FrameTimings
                .Select(t => new AnimationFrameTiming(t.MinimumMilliseconds, t.MaximumMilliseconds))
                .ToArray();
        }

        return clone;
    }
}
