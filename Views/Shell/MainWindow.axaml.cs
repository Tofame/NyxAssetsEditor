using System.ComponentModel;
using Avalonia.Controls;
using NyxAssetsEditor.ViewModels.Pages;
using NyxAssetsEditor.ViewModels.Shell;

namespace NyxAssetsEditor.Views.Shell
{
	public partial class MainWindow : Window
	{
		private bool _closeConfirmed;

		public MainWindow()
		{
			InitializeComponent();
			Closing += OnWindowClosing;
		}

		private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
		{
			if (_closeConfirmed)
				return;

			var assetsVm = GetAssetsViewModel();
			if (assetsVm == null || !assetsVm.CanCompile)
				return;

			// Cancel the close so we can show the async dialog
			e.Cancel = true;

			var summary = assetsVm.GetPendingChangesSummary();
			var dialog = new ExitConfirmDialog(summary);
			await dialog.ShowDialog(this);

			switch (dialog.Result)
			{
				case ExitDialogResult.Yes:
					// Run compile synchronously then exit
					try { assetsVm.CompileCommand.Execute(null); }
					catch { /* swallow — don't block exit on compile error */ }
					_closeConfirmed = true;
					Close();
					break;

				case ExitDialogResult.No:
					// Exit without compiling
					_closeConfirmed = true;
					Close();
					break;

				case ExitDialogResult.Cancel:
					// Stay in app — do nothing
					break;
			}
		}

		private AssetsViewModel? GetAssetsViewModel()
		{
			if (DataContext is MainWindowViewModel mwvm)
			{
				// Reach the AssetsViewModel through the current page if it is one,
				// or through any previously cached page.
				if (mwvm.CurrentPage is AssetsViewModel avm)
					return avm;

				// Walk field via reflection to avoid exposing it publicly
				var field = typeof(MainWindowViewModel)
					.GetField("_assetsViewModel",
					          System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
				return field?.GetValue(mwvm) as AssetsViewModel;
			}
			return null;
		}
	}
}