using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NyxAssetsEditor.Views.Shell
{
	public partial class ConfirmOpenDialog : Window
	{
		public bool Result { get; private set; } = false;

		public ConfirmOpenDialog()
		{
			InitializeComponent();
		}

		private void OnContinueClick(object? sender, RoutedEventArgs e)
		{
			Result = true;
			Close();
		}

		private void OnCancelClick(object? sender, RoutedEventArgs e)
		{
			Result = false;
			Close();
		}
	}
}
