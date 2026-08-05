using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NyxAssetsEditor.Views.Shell
{
	public partial class InfoDialog : Window
	{
		public InfoDialog() : this("Info", string.Empty)
		{
		}

		public InfoDialog(string title, string message)
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
