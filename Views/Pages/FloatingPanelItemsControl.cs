using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace NyxAssetsEditor.Views.Pages;

/// <summary>
/// Owns activation and visual stacking for the floating-panel containers it generates.
/// </summary>
public sealed class FloatingPanelItemsControl : ItemsControl
{
	protected override Type StyleKeyOverride => typeof(ItemsControl);

	public FloatingPanelItemsControl()
	{
		AddHandler(
			InputElement.PointerPressedEvent,
			OnPointerPressed,
			RoutingStrategies.Tunnel,
			handledEventsToo: true);

		ContainerPrepared += OnContainerPrepared;
	}

	public void BringToFront(object item)
	{
		if (ContainerFromItem(item) is Control container)
			BringContainerToFront(container);
	}

	private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
	{
		BringContainerToFront(e.Container);
	}

	private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (e.Source is not Visual source || ItemsPanelRoot is not Panel itemsPanel)
			return;

		var container = FindDirectChild(source, itemsPanel);
		if (container != null)
			BringContainerToFront(container);
	}

	private void BringContainerToFront(Control container)
	{
		if (ItemsPanelRoot is not Panel itemsPanel || container.GetVisualParent() != itemsPanel)
			return;

		var orderedChildren = itemsPanel.Children
			.Select((child, index) => (child, index))
			.OrderBy(entry => entry.child.ZIndex)
			.ThenBy(entry => entry.index)
			.ToArray();
		if (orderedChildren.Length == 0 || ReferenceEquals(orderedChildren[^1].child, container))
			return;

		var highestZIndex = orderedChildren[^1].child.ZIndex;
		if (highestZIndex == int.MaxValue)
		{
			NormalizeZIndexes(orderedChildren);
			highestZIndex = orderedChildren.Length - 1;
		}

		container.ZIndex = highestZIndex + 1;

		// Avalonia can retain the previous composition order for descendants until
		// each one is dirtied. Refresh the activated subtree so the panel appears
		// at its new level as a single visual update.
		InvalidateSubtree(container);
	}

	private static void InvalidateSubtree(Visual root)
	{
		root.InvalidateVisual();
		foreach (var descendant in root.GetVisualDescendants())
			descendant.InvalidateVisual();
	}

	private static Control? FindDirectChild(Visual source, Panel itemsPanel)
	{
		Visual? current = source;
		while (current != null && current.GetVisualParent() != itemsPanel)
			current = current.GetVisualParent();

		return current as Control;
	}

	private static void NormalizeZIndexes((Control child, int index)[] orderedChildren)
	{
		for (var index = 0; index < orderedChildren.Length; index++)
			orderedChildren[index].child.ZIndex = index;
	}
}
