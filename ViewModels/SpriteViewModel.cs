using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using NyxAssetsEditor.Models;
using NyxAssetsEditor.Services;

namespace NyxAssetsEditor.ViewModels
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

		public bool CanPaste => SpriteClipboard.HasData;

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
		private void Copy()
		{
			var selected = _panel.GetSelectedSprites();
			if (selected.Count > 1 && selected.Any(s => s.Id == Id))
				_panel.CopySprites(selected);
			else
				_panel.CopySprite(this);
		}

		[RelayCommand(CanExecute = nameof(CanPaste))]
		private void Paste() => _panel.PasteSprite(this);

		[RelayCommand]
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

		[RelayCommand]
		private void Remove()
		{
			var selected = _panel.GetSelectedSprites();
			if (selected.Count > 1 && selected.Any(s => s.Id == Id))
				_panel.RemoveSprites(selected);
			else
				_panel.RemoveSprites(new[] { this });
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
