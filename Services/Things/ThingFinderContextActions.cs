using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NyxAssets.Things;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;

namespace NyxAssetsEditor.Services.Things;

public sealed record ThingFinderContextAction(
	string Label,
	Func<Task> ExecuteAsync,
	string? ConfirmationTitle = null,
	string? ConfirmationMessage = null,
	string ConfirmationButtonText = "Confirm");

public interface IThingFinderContextActionProvider
{
	IEnumerable<ThingFinderContextAction> GetThingFinderContextActions(
		FloatingThingsLoaderViewModel source,
		ThingType thing);
}
