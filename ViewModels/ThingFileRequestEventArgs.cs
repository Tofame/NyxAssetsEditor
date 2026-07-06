using System;
using System.Collections.Generic;
using System.Linq;

namespace NyxAssetsEditor.ViewModels;

	public sealed class ThingFileRequestEventArgs : EventArgs
	{
		public ThingFileRequestEventArgs(IEnumerable<ThingItemViewModel>? things, string format)
		{
			Things = things?.ToList() ?? new List<ThingItemViewModel>();
			Format = format;
		}

		public IReadOnlyList<ThingItemViewModel> Things { get; }
		public ThingItemViewModel? Thing => Things.Count > 0 ? Things[0] : null;
		/// <summary>png, jpg, bmp, nyx-thing, import, replace.</summary>
		public string Format { get; }
	}
