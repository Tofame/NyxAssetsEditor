using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NyxAssetsEditor.Views.Shell
{
	public enum SpritesCloseResult
	{
		CloseBoth,
		Cancel
	}

	public partial class SpritesCloseDialog : Window
	{
		public SpritesCloseResult Result { get; private set; } = SpritesCloseResult.Cancel;

		public SpritesCloseDialog() : this(string.Empty, false)
		{
		}

		public SpritesCloseDialog(string summary, bool hasUnsavedChanges)
		{
			InitializeComponent();
			SummaryText.Text = summary;

			if (hasUnsavedChanges)
			{
				Title = "Sprites Viewer Locked";
				InstructionText.Text = "This Sprites Viewer cannot be closed because the following connected Things Viewer(s) have unsaved changes. Please compile or cancel them first:";
				QuestionText.Text = string.Empty;
				CloseBothButton.IsVisible = false;
			}
		}

		private void OnCloseBothClick(object? sender, RoutedEventArgs e)
		{
			Result = SpritesCloseResult.CloseBoth;
			Close();
		}

		private void OnCancelClick(object? sender, RoutedEventArgs e)
		{
			Result = SpritesCloseResult.Cancel;
			Close();
		}
	}
}
