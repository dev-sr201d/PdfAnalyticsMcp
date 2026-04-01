using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tests;

public class JpegEncoderTests
{
    private static readonly byte[] JpegSoiMarker = [0xFF, 0xD8];
    private static readonly byte[] JpegEoiMarker = [0xFF, 0xD9];

    [Fact]
    public void Encode_2x2_StartsWithSoiAndEndsWithEoi()
    {
        // 2×2 opaque white pixels
        byte[] bgra = new byte[2 * 2 * 4];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = 255;     // B
            bgra[i + 1] = 255; // G
            bgra[i + 2] = 255; // R
            bgra[i + 3] = 255; // A
        }

        byte[] jpeg = JpegEncoder.Encode(bgra, 2, 2, 80);

        Assert.True(jpeg.Length >= 4);
        Assert.Equal(JpegSoiMarker, jpeg[..2]);
        Assert.Equal(JpegEoiMarker, jpeg[^2..]);
    }

    [Fact]
    public void Encode_QualityAffectsFileSize()
    {
        int width = 100, height = 100;
        byte[] bgra = new byte[width * height * 4];

        // Fill with varied colors to ensure compression difference is measurable
        var rng = new Random(42);
        rng.NextBytes(bgra);
        // Set all alpha to opaque
        for (int i = 3; i < bgra.Length; i += 4)
        {
            bgra[i] = 255;
        }

        byte[] lowQuality = JpegEncoder.Encode(bgra, width, height, 10);
        byte[] highQuality = JpegEncoder.Encode(bgra, width, height, 100);

        Assert.True(highQuality.Length > lowQuality.Length,
            $"Expected quality=100 ({highQuality.Length} bytes) > quality=10 ({lowQuality.Length} bytes)");
    }

    [Fact]
    public void Encode_AlphaCompositesAgainstWhite()
    {
        // Single fully-transparent black pixel: B=0, G=0, R=0, A=0
        // After compositing against white, should become white (255, 255, 255)
        byte[] bgra = [0, 0, 0, 0];

        byte[] jpeg = JpegEncoder.Encode(bgra, 1, 1, 100);

        // Verify it's a valid JPEG
        Assert.Equal(JpegSoiMarker, jpeg[..2]);
        Assert.Equal(JpegEoiMarker, jpeg[^2..]);

        // Decode the JPEG and verify the pixel is close to white
        using var bitmap = SkiaSharp.SKBitmap.Decode(jpeg);
        Assert.NotNull(bitmap);
        var pixel = bitmap.GetPixel(0, 0);

        // JPEG is lossy, but a 1×1 white pixel at quality=100 should be very close to white
        Assert.InRange(pixel.Red, 250, 255);
        Assert.InRange(pixel.Green, 250, 255);
        Assert.InRange(pixel.Blue, 250, 255);
    }

    [Fact]
    public void Encode_OpaquePixelPassthrough()
    {
        // Single fully-opaque pixel: B=50, G=100, R=200, A=255
        byte[] bgra = [50, 100, 200, 255];

        byte[] jpeg = JpegEncoder.Encode(bgra, 1, 1, 100);

        // Decode and verify approximate color preservation
        using var bitmap = SkiaSharp.SKBitmap.Decode(jpeg);
        Assert.NotNull(bitmap);
        var pixel = bitmap.GetPixel(0, 0);

        // JPEG lossy tolerance: within ~5 of original values
        Assert.InRange(pixel.Red, 195, 205);
        Assert.InRange(pixel.Green, 95, 105);
        Assert.InRange(pixel.Blue, 45, 55);
    }

    [Fact]
    public void Encode_InvalidDataLength_ThrowsArgumentException()
    {
        byte[] bgra = new byte[10]; // Not valid for any dimensions

        var ex = Assert.Throws<ArgumentException>(() => JpegEncoder.Encode(bgra, 2, 2, 80));
        Assert.Contains("does not match expected length", ex.Message);
    }

    [Fact]
    public void Encode_QualityBelowRange_ThrowsArgumentException()
    {
        byte[] bgra = new byte[2 * 2 * 4];

        var ex = Assert.Throws<ArgumentException>(() => JpegEncoder.Encode(bgra, 2, 2, 0));
        Assert.Contains("between 1 and 100", ex.Message);
    }

    [Fact]
    public void Encode_QualityAboveRange_ThrowsArgumentException()
    {
        byte[] bgra = new byte[2 * 2 * 4];

        var ex = Assert.Throws<ArgumentException>(() => JpegEncoder.Encode(bgra, 2, 2, 101));
        Assert.Contains("between 1 and 100", ex.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Encode_QualityAtBoundaries_Succeeds(int quality)
    {
        byte[] bgra = new byte[2 * 2 * 4];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = 128;     // B
            bgra[i + 1] = 128; // G
            bgra[i + 2] = 128; // R
            bgra[i + 3] = 255; // A
        }

        byte[] jpeg = JpegEncoder.Encode(bgra, 2, 2, quality);

        Assert.True(jpeg.Length > 0);
        Assert.Equal(JpegSoiMarker, jpeg[..2]);
        Assert.Equal(JpegEoiMarker, jpeg[^2..]);
    }

    [Fact]
    public void Encode_100x100_ProducesValidJpeg()
    {
        int width = 100, height = 100;
        byte[] bgra = new byte[width * height * 4];

        // Fill with a repeating color pattern
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = (byte)(i % 256);         // B
            bgra[i + 1] = (byte)(i / 2 % 256); // G
            bgra[i + 2] = (byte)(i / 3 % 256); // R
            bgra[i + 3] = 255;                   // A
        }

        byte[] jpeg = JpegEncoder.Encode(bgra, width, height, 80);

        Assert.True(jpeg.Length > 100, "JPEG output should have non-trivial length");
        Assert.Equal(JpegSoiMarker, jpeg[..2]);
        Assert.Equal(JpegEoiMarker, jpeg[^2..]);
    }
}
