using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NyxAssetsEditor.Views.Pages
{
	public partial class ResizeCanvasDialog : Window
	{
		public bool IsConfirmed { get; private set; }
		public int TargetWidth => (int)(WidthInput?.Value ?? 32);
		public int TargetHeight => (int)(HeightInput?.Value ?? 32);

		public ResizeCanvasDialog()
		{
			InitializeComponent();
		}

		public ResizeCanvasDialog(int currentWidth, int currentHeight)
		{
			InitializeComponent();
			if (WidthInput != null) WidthInput.Value = currentWidth;
			if (HeightInput != null) HeightInput.Value = currentHeight;
		}

		private void OnConfirmClick(object? sender, RoutedEventArgs e)
		{
			IsConfirmed = true;
			Close();
		}

		private void OnCancelClick(object? sender, RoutedEventArgs e)
		{
			IsConfirmed = false;
			Close();
		}
	}
}
