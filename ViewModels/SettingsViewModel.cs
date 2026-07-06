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
		public static uint ThingIdOffset { get; set; } = 0;
		public static uint ClientVersion { get; set; } = 1098;
		public static event Action<int>? DefaultPageSizeChanged;
		public static event Action<uint>? ThingIdOffsetChanged;
		public static event Action<uint>? ClientVersionChanged;

		public int SelectedThingIdOffset
		{
			get => (int)ThingIdOffset;
			set
			{
				uint uValue = value < 0 ? 0 : (uint)value;
				if (ThingIdOffset != uValue)
				{
					ThingIdOffset = uValue;
					OnPropertyChanged(nameof(SelectedThingIdOffset));
					ThingIdOffsetChanged?.Invoke(uValue);
				}
			}
		}

		private int _selectedVersionIndex = 0; // Index 0 maps to 10.98

		public int SelectedVersionIndex
		{
			get => _selectedVersionIndex;
			set
			{
				if (_selectedVersionIndex != value)
				{
					_selectedVersionIndex = value;
					OnPropertyChanged(nameof(SelectedVersionIndex));

					ClientVersion = value switch
					{
						0 => 1098,
						1 => 860,
						2 => 760,
						_ => 1098
					};
					ClientVersionChanged?.Invoke(ClientVersion);
				}
			}
		}

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
