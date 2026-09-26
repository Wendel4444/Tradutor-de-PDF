using PDFtoImage;
using SkiaSharp;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace TradutorPdf;

public static class RenderizadorPagina
{
    private const int Dpi = 150;

    public static List<byte[]?> Recortar(byte[] pdf, Page pagina, List<PdfRectangle> areas)
    {
        using var bitmap = Conversion.ToImage(pdf, pagina.Number - 1, options: new RenderOptions(Dpi: Dpi));

        var visivel = pagina.CropBox.Bounds;
        double escalaX = bitmap.Width / visivel.Width;
        double escalaY = bitmap.Height / visivel.Height;

        var resultado = new List<byte[]?>();
        foreach (var area in areas)
        {
            var pixels = SKRectI.Round(new SKRect(
                (float)((area.Left - visivel.Left) * escalaX),
                (float)((visivel.Top - area.Top) * escalaY),
                (float)((area.Right - visivel.Left) * escalaX),
                (float)((visivel.Top - area.Bottom) * escalaY)));
            pixels.Intersect(new SKRectI(0, 0, bitmap.Width, bitmap.Height));

            if (pixels.Width < 10 || pixels.Height < 10)
            {
                resultado.Add(null);
                continue;
            }

            using var recorte = new SKBitmap();
            if (!bitmap.ExtractSubset(recorte, pixels))
            {
                resultado.Add(null);
                continue;
            }
            using var imagem = SKImage.FromBitmap(recorte);
            using var png = imagem.Encode(SKEncodedImageFormat.Png, 100);
            resultado.Add(png.ToArray());
        }
        return resultado;
    }
}
