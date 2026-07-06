using Avalonia.Media.Imaging;
using NyxAssetsEditor.Models;
using NyxAssetsEditor.Services;

namespace NyxAssetsEditor.ViewModels
{
    public class SpriteViewModel : ViewModelBase
    {
        private readonly uint _id;
        private readonly SpriteLoader _loader;
        private readonly SpriteRenderer _renderer;
        private WriteableBitmap? _preview;

        public uint Id => _id;

        // The UI Image element binds to this property to draw the sprite
        public WriteableBitmap Preview
        {
            get
            {
                if (_preview == null)
                {
                    byte[] pixels = _loader.LoadSpritePixels(_id);

                    var model = new SpriteModel 
                    { 
                        Id = _id, 
                        Pixels = pixels 
                    };

                    _preview = _renderer.Convert(model);
                }
                return _preview;
            }
        }

        public SpriteViewModel(uint id, SpriteLoader loader, SpriteRenderer renderer)
        {
            _id = id;
            _loader = loader;
            _renderer = renderer;
        }
    }
}