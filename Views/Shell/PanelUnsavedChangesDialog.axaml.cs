using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NyxAssetsEditor.Views.Shell
{
	public enum PanelUnsavedChangesResult
	{
		Discard,
		Cancel
	}

	public partial class PanelUnsavedChangesDialog : Window
	{
		public PanelUnsavedChangesResult Result { get; private set; } = PanelUnsavedChangesResult.Cancel;

		public PanelUnsavedChangesDialog() : this("Unsaved Changes", string.Empty)
		{
		}

		public PanelUnsavedChangesDialog(string title, string summary)
		{
			InitializeComponent();
			TitleText.Text = title;
			SummaryText.Text = summary;
		}

		private void OnDiscardClick(object? sender, RoutedEventArgs e)
		{
			Result = PanelUnsavedChangesResult.Discard;
			Close();
		}

		private void OnCancelClick(object? sender, RoutedEventArgs e)
		{
			Result = PanelUnsavedChangesResult.Cancel;
			Close();
		}
	}
}
