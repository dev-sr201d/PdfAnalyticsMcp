using System.Runtime.InteropServices;
using SkiaSharp;

namespace PdfAnalyticsMcp.Services;

/// <summary>
/// Encodes raw BGRA pixel data into a valid JPEG file using SkiaSharp.
/// </summary>
internal static class JpegEncoder
{
    /// <summary>
    /// Encodes raw BGRA pixel data into a JPEG byte array.
    /// </summary>
    /// <param name="bgraData">Raw pixel data in BGRA format (4 bytes per pixel).</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="quality">JPEG quality from 1 (max compression) to 100 (min compression).</param>
    /// <returns>A byte array containing a valid JPEG file.</returns>
    public static byte[] Encode(byte[] bgraData, int width, int height, int quality)
    {
        ArgumentNullException.ThrowIfNull(bgraData);

        int expectedLength = width * height * 4;
        if (bgraData.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Pixel data length {bgraData.Length} does not match expected length {expectedLength} for a {width}x{height} image.",
                nameof(bgraData));
        }

        if (quality < 1 || quality > 100)
        {
            throw new ArgumentException(
                "Quality must be between 1 and 100.",
                nameof(quality));
        }

        // Composite alpha against white background into a working copy
        byte[] composited = new byte[bgraData.Length];
        for (int i = 0; i < bgraData.Length; i += 4)
        {
            byte b = bgraData[i];
            byte g = bgraData[i + 1];
            byte r = bgraData[i + 2];
            byte a = bgraData[i + 3];

            // Alpha-composite against white: out = src * a/255 + 255 * (1 - a/255)
            composited[i] = (byte)((b * a + 255 * (255 - a)) / 255);
            composited[i + 1] = (byte)((g * a + 255 * (255 - a)) / 255);
            composited[i + 2] = (byte)((r * a + 255 * (255 - a)) / 255);
            composited[i + 3] = 255; // Fully opaque after compositing
        }

        using var bitmap = new SKBitmap();
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);

        var handle = GCHandle.Alloc(composited, GCHandleType.Pinned);
        try
        {
            bitmap.InstallPixels(info, handle.AddrOfPinnedObject(), width * 4);

            using var image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            return data.ToArray();
        }
        finally
        {
            handle.Free();
        }
    }
}
