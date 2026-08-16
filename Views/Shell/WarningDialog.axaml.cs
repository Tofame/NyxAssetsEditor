using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace NyxAssetsEditor.Views.Shell
{
	public partial class WarningDialog : Window
	{
		public WarningDialog() : this("Action Required", string.Empty, null, null)
		{
		}

		public WarningDialog(string title, string message) : this(title, message, null, null)
		{
		}

		public WarningDialog(string title, string message, string? infoMessage) : this(title, message, infoMessage, null)
		{
		}

		public WarningDialog(string title, string message, string? infoMessage, string? snippetCode)
		{
			InitializeComponent();
			TitleText.Text = title;
			MessageText.Text = message;
			Title = title;

			if (!string.IsNullOrEmpty(infoMessage))
			{
				InfoBox.IsVisible = true;
				InfoBoxText.Text = infoMessage;

				if (!string.IsNullOrEmpty(snippetCode))
				{
					SnippetPanel.IsVisible = true;
					SnippetTextBox.Text = snippetCode;
				}
				else
				{
					SnippetPanel.IsVisible = false;
				}
			}
			else
			{
				InfoBox.IsVisible = false;
			}
		}

		private async void OnCopySnippetClick(object? sender, RoutedEventArgs e)
		{
			var text = SnippetTextBox.Text;
			if (!string.IsNullOrEmpty(text))
			{
				var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
				if (clipboard != null)
				{
					await clipboard.SetTextAsync(text);
					CopySnippetButton.Content = "Copied!";
				}
			}
		}

		private void OnOkClick(object? sender, RoutedEventArgs e)
		{
			Close();
		}
	}
}
