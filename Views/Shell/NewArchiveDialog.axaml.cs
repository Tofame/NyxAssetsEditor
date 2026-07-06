using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NyxAssetsEditor.ViewModels.Common;

namespace NyxAssetsEditor.Views.Shell
{
	public class NewArchiveResult
	{
		public bool IsConfirmed { get; set; }
		public string Format { get; set; } = "dat"; // "dat" or "json"
		public uint ClientVersion { get; set; } = 1098;
		public bool UseExtendedSpriteIds { get; set; }
		public bool UseTransparentPixels { get; set; }
		public bool UseFrameAnimations { get; set; }
		public bool UseFrameGroups { get; set; }
	}

	public partial class NewArchiveDialog : Window
	{
		public NewArchiveResult Result { get; } = new NewArchiveResult();

		public NewArchiveDialog()
		{
			InitializeComponent();

			// Populate Client Versions ComboBox
			VersionComboBox.ItemsSource = ClientVersion.AvailableVersions;
			VersionComboBox.SelectedIndex = 0; // Default to 10.98

			// Bind selection changes to update checkboxes
			FormatComboBox.SelectionChanged += OnSelectionChanged;
			VersionComboBox.SelectionChanged += OnSelectionChanged;

			UpdateDefaults();
		}

		private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
		{
			UpdateDefaults();
		}

		private void UpdateDefaults()
		{
			if (FormatComboBox == null || VersionComboBox == null) return;

			bool isJson = FormatComboBox.SelectedIndex == 1;

			if (isJson)
			{
				ExtendedCheckbox.IsChecked = true;
				TransparentCheckbox.IsChecked = true;
				AnimationsCheckbox.IsChecked = true;
				GroupsCheckbox.IsChecked = true;

				ExtendedCheckbox.IsEnabled = false;
				TransparentCheckbox.IsEnabled = false;
				AnimationsCheckbox.IsEnabled = false;
				GroupsCheckbox.IsEnabled = false;
				VersionComboBox.IsEnabled = false;
			}
			else
			{
				ExtendedCheckbox.IsEnabled = true;
				TransparentCheckbox.IsEnabled = true;
				AnimationsCheckbox.IsEnabled = true;
				GroupsCheckbox.IsEnabled = true;
				VersionComboBox.IsEnabled = true;

				if (VersionComboBox.SelectedItem is ClientVersion selectedVer)
				{
					if (selectedVer.Version == 1098)
					{
						ExtendedCheckbox.IsChecked = true;
						TransparentCheckbox.IsChecked = true;
						AnimationsCheckbox.IsChecked = true;
						GroupsCheckbox.IsChecked = true;
					}
					else
					{
						ExtendedCheckbox.IsChecked = false;
						TransparentCheckbox.IsChecked = false;
						AnimationsCheckbox.IsChecked = false;
						GroupsCheckbox.IsChecked = false;
					}
				}
			}
		}

		private void OnConfirmClick(object? sender, RoutedEventArgs e)
		{
			Result.IsConfirmed = true;
			Result.Format = FormatComboBox.SelectedIndex == 0 ? "dat" : "json";
			if (VersionComboBox.SelectedItem is ClientVersion selectedVer)
			{
				Result.ClientVersion = selectedVer.Version;
			}
			Result.UseExtendedSpriteIds = ExtendedCheckbox.IsChecked == true;
			Result.UseTransparentPixels = TransparentCheckbox.IsChecked == true;
			Result.UseFrameAnimations = AnimationsCheckbox.IsChecked == true;
			Result.UseFrameGroups = GroupsCheckbox.IsChecked == true;
			Close();
		}

		private void OnCancelClick(object? sender, RoutedEventArgs e)
		{
			Result.IsConfirmed = false;
			Close();
		}
	}
}
