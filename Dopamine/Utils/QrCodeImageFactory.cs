using QRCoder;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dopamine.Utils
{
    public static class QrCodeImageFactory
    {
        public static ImageSource Create(string content)
        {
            using (var generator = new QRCodeGenerator())
            using (QRCodeData data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q))
            using (var qrCode = new PngByteQRCode(data))
            {
                byte[] png = qrCode.GetGraphic(8, true);

                using (var stream = new MemoryStream(png))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
        }
    }
}
