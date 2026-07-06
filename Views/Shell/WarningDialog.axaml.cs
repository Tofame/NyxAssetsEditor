using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NyxAssetsEditor.Views.Shell
{
	public partial class WarningDialog : Window
	{
		public WarningDialog() : this("Action Required", string.Empty)
		{
		}

		public WarningDialog(string title, string message)
		{
			InitializeComponent();
			TitleText.Text = title;
			MessageText.Text = message;
			Title = title;
		}

		private void OnOkClick(object? sender, RoutedEventArgs e)
		{
			Close();
		}
	}
}
