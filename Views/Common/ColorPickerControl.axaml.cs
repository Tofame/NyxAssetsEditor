using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;

namespace NyxAssetsEditor.Views.Common
{
	public partial class ColorPickerControl : UserControl
	{
		public static readonly StyledProperty<Color> SelectedColorProperty =
			AvaloniaProperty.Register<ColorPickerControl, Color>(
				nameof(SelectedColor),
				Colors.White,
				defaultBindingMode: BindingMode.TwoWay);

		public Color SelectedColor
		{
			get => GetValue(SelectedColorProperty);
			set => SetValue(SelectedColorProperty, value);
		}

		public static readonly DirectProperty<ColorPickerControl, byte> ColorRProperty =
			AvaloniaProperty.RegisterDirect<ColorPickerControl, byte>(
				nameof(ColorR),
				o => o.ColorR,
				(o, v) => o.ColorR = v);

		public static readonly DirectProperty<ColorPickerControl, byte> ColorGProperty =
			AvaloniaProperty.RegisterDirect<ColorPickerControl, byte>(
				nameof(ColorG),
				o => o.ColorG,
				(o, v) => o.ColorG = v);

		public static readonly DirectProperty<ColorPickerControl, byte> ColorBProperty =
			AvaloniaProperty.RegisterDirect<ColorPickerControl, byte>(
				nameof(ColorB),
				o => o.ColorB,
				(o, v) => o.ColorB = v);

		private byte _colorR = 255;
		private byte _colorG = 255;
		private byte _colorB = 255;
		private bool _isSyncing = false;

		public byte ColorR
		{
			get => _colorR;
			set
			{
				SetAndRaise(ColorRProperty, ref _colorR, value);
				UpdateColorFromRgb();
			}
		}

		public byte ColorG
		{
			get => _colorG;
			set
			{
				SetAndRaise(ColorGProperty, ref _colorG, value);
				UpdateColorFromRgb();
			}
		}

		public byte ColorB
		{
			get => _colorB;
			set
			{
				SetAndRaise(ColorBProperty, ref _colorB, value);
				UpdateColorFromRgb();
			}
		}

		public ColorPickerControl()
		{
			InitializeComponent();
			SelectedColorProperty.Changed.AddClassHandler<ColorPickerControl>((x, e) => x.OnSelectedColorChanged(e));
			UpdateRgbFromColor(SelectedColor);
		}

		private void OnSelectedColorChanged(AvaloniaPropertyChangedEventArgs e)
		{
			if (e.NewValue is Color color)
			{
				UpdateRgbFromColor(color);
			}
		}

		private void UpdateRgbFromColor(Color color)
		{
			if (_isSyncing)
				return;
			_isSyncing = true;
			try
			{
				ColorR = color.R;
				ColorG = color.G;
				ColorB = color.B;
			}
			finally
			{
				_isSyncing = false;
			}
		}

		private void UpdateColorFromRgb()
		{
			if (_isSyncing)
				return;
			_isSyncing = true;
			try
			{
				SelectedColor = Color.FromRgb(ColorR, ColorG, ColorB);
			}
			finally
			{
				_isSyncing = false;
			}
		}
	}
}
