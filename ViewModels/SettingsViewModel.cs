using System;

namespace NyxAssetsEditor.ViewModels
{
	public class SettingsViewModel : ViewModelBase
	{
		public string Title => "Application Settings";
		public string Description => "This is the dynamically loaded Settings View. Configure your editor options here.";

		public static int DefaultPageSize { get; private set; } = 100;
		public static bool UseTransparentPixels { get; set; } = true;
		public static bool UseExtendedSpriteIds { get; set; } = true;
		public static event Action<int>? DefaultPageSizeChanged;

		private int _selectedPageIndex = 1; // Index 1 maps to 100

		public int SelectedPageSizeIndex
		{
			get => _selectedPageIndex;
			set
			{
				if (_selectedPageIndex != value)
				{
					_selectedPageIndex = value;
					OnPropertyChanged(nameof(SelectedPageSizeIndex));

					int newSize = value switch
					{
						0 => 50,
						1 => 100,
						2 => 200,
						_ => 100
					};
					DefaultPageSize = newSize;
					DefaultPageSizeChanged?.Invoke(newSize);
				}
			}
		}
	}
}
