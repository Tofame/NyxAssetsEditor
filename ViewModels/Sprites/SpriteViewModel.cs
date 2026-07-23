using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using NyxAssetsEditor.Models;
using NyxAssetsEditor.Services.Archive;
using NyxAssetsEditor.Services.ImportExport;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Shell;

namespace NyxAssetsEditor.ViewModels.Sprites
{
	public partial class SpriteViewModel : ViewModelBase
	{
		private readonly FloatingSpriteLoaderViewModel _panel;
		private readonly SpriteLoader _loader;
		private readonly SpriteRenderer _renderer;
		private WriteableBitmap? _preview;
		private bool _isSelected;

		public uint Id { get; }

		public bool IsSelected
		{
			get => _isSelected;
			set => SetProperty(ref _isSelected, value);
		}

		public WriteableBitmap Preview
		{
			get
			{
				_preview ??= _renderer.Convert(GetPixels());
				return _preview;
			}
		}

		public bool CanModify => Id != 0;

		public bool CanPaste => CanModify;

		public SpriteViewModel(uint id, FloatingSpriteLoaderViewModel panel, SpriteLoader loader, SpriteRenderer renderer)
		{
			Id = id;
			_panel = panel;
			_loader = loader;
			_renderer = renderer;
		}

		public byte[] GetPixels() => _loader.LoadSpritePixels(Id);

		public void InvalidatePreview()
		{
			_preview = null;
			OnPropertyChanged(nameof(Preview));
		}

		public void NotifyPasteAvailabilityChanged() => OnPropertyChanged(nameof(CanPaste));

		[RelayCommand]
		private async System.Threading.Tasks.Task Copy()
		{
			var selected = _panel.GetSelectedSprites();
			if (selected.Count > 1 && selected.Any(s => s.Id == Id))
				await _panel.CopySpritesAsync(selected);
			else
				await _panel.CopySpriteAsync(this);
		}

		[RelayCommand(CanExecute = nameof(CanPaste))]
		private async System.Threading.Tasks.Task Paste() => await _panel.PasteSpriteAsync(this);

		[RelayCommand(CanExecute = nameof(CanModify))]
		private void Replace()
		{
			var selected = _panel.GetSelectedSprites();
			if (selected.Count > 1 && selected.Any(s => s.Id == Id))
				_panel.RequestImportSprites(selected);
			else
				_panel.RequestReplaceSprite(this);
		}

		[RelayCommand]
		private void ExportPng() => ExportWithSelection("png");

		[RelayCommand]
		private void ExportJpeg() => ExportWithSelection("jpg");

		[RelayCommand]
		private void ExportBmp() => ExportWithSelection("bmp");

		[RelayCommand(CanExecute = nameof(CanModify))]
		private void Remove()
		{
			var selected = _panel.GetSelectedSprites();
			if (selected.Count > 1 && selected.Any(s => s.Id == Id))
				_panel.RemoveSprites(selected);
			else
				_panel.RemoveSprites(new[] { this });
		}

		[RelayCommand]
		private void EditInPaint()
		{
			if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
				&& desktop.MainWindow?.DataContext is MainWindowViewModel mainVM)
			{
				mainVM.EditSprite(this, _panel);
			}
		}

		private void ExportWithSelection(string format)
		{
			var selected = _panel.GetSelectedSprites();
			if (selected.Count > 1 && selected.Any(s => s.Id == Id))
				_panel.RequestExportSprites(selected, format);
			else
				_panel.RequestExportSprite(this, format);
		}
	}
}
