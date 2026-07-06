using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NyxAssetsEditor.Views.Shell
{
	public enum ExitDialogResult
	{
		Yes,
		No,
		Cancel
	}

	public partial class ExitConfirmDialog : Window
	{
		public ExitDialogResult Result { get; private set; } = ExitDialogResult.Cancel;

		public ExitConfirmDialog(string summary)
		{
			InitializeComponent();
			SummaryText.Text = summary;
		}

		private void OnYesClick(object? sender, RoutedEventArgs e)
		{
			Result = ExitDialogResult.Yes;
			Close();
		}

		private void OnNoClick(object? sender, RoutedEventArgs e)
		{
			Result = ExitDialogResult.No;
			Close();
		}

		private void OnCancelClick(object? sender, RoutedEventArgs e)
		{
			Result = ExitDialogResult.Cancel;
			Close();
		}
	}
}
