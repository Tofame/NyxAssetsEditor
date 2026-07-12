using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;

namespace NyxAssetsEditor.Views.Pages
{
	public partial class HexColorPicker : UserControl
	{
		public static readonly StyledProperty<string> HexColorProperty =
			AvaloniaProperty.Register<HexColorPicker, string>(
				nameof(HexColor),
				"#FFFFFF",
				defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

		private readonly ColorPicker _colorBtn;
		private readonly TextBlock _hexText;
		private bool _isUpdating = false;

		public string HexColor
		{
			get => GetValue(HexColorProperty);
			set => SetValue(HexColorProperty, value);
		}

		public HexColorPicker()
		{
			InitializeComponent();

			_colorBtn = this.FindControl<ColorPicker>("ColorBtn") ?? throw new Exception("ColorBtn not found");
			_hexText = this.FindControl<TextBlock>("HexText") ?? throw new Exception("HexText not found");

			_colorBtn.ColorChanged += OnColorButtonChanged;
		}

		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
		{
			base.OnPropertyChanged(change);

			if (change.Property == HexColorProperty)
			{
				if (_isUpdating)
					return;

				_isUpdating = true;
				try
				{
					string hex = change.GetNewValue<string>();
					if (!string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out Color color))
					{
						_colorBtn.Color = color;
						_hexText.Text = hex.ToUpperInvariant();
					}
				}
				finally
				{
					_isUpdating = false;
				}
			}
		}

		private void OnColorButtonChanged(object? sender, ColorChangedEventArgs e)
		{
			if (_isUpdating)
				return;

			_isUpdating = true;
			try
			{
				Color color = e.NewColor;
				string hex = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
				HexColor = hex;
				_hexText.Text = hex;
			}
			finally
			{
				_isUpdating = false;
			}
		}
	}
}
