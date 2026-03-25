using System.Buffers.Binary;
using System.IO.Compression;
using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tests;

public class PngEncoderTests
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    [Fact]
    public void Encode_2x2_StartsWithPngSignature()
    {
        byte[] bgra = new byte[2 * 2 * 4];
        byte[] png = PngEncoder.Encode(bgra, 2, 2);

        Assert.True(png.Length >= 8);
        Assert.Equal(PngSignature, png[..8]);
    }

    [Fact]
    public void Encode_4x3_IhdrContainsCorrectDimensions()
    {
        int width = 4, height = 3;
        byte[] bgra = new byte[width * height * 4];
        byte[] png = PngEncoder.Encode(bgra, width, height);

        // IHDR starts after signature (8 bytes) + 4 bytes length + 4 bytes "IHDR" type
        int ihdrDataOffset = 8 + 4 + 4;
        int parsedWidth = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(ihdrDataOffset, 4));
        int parsedHeight = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(ihdrDataOffset + 4, 4));

        Assert.Equal(width, parsedWidth);
        Assert.Equal(height, parsedHeight);
    }

    [Fact]
    public void Encode_IhdrHasBitDepth8AndColorType2()
    {
        byte[] bgra = new byte[2 * 2 * 4];
        byte[] png = PngEncoder.Encode(bgra, 2, 2);

        int ihdrDataOffset = 8 + 4 + 4;
        byte bitDepth = png[ihdrDataOffset + 8];
        byte colorType = png[ihdrDataOffset + 9];

        Assert.Equal(8, bitDepth);
        Assert.Equal(2, colorType); // RGB — alpha composited against white
    }

    [Fact]
    public void Encode_CompositesBgraAgainstWhite()
    {
        // Single pixel: B=0, G=128, R=255, A=200
        byte[] bgra = [0, 128, 255, 200];
        byte[] png = PngEncoder.Encode(bgra, 1, 1);

        // Extract and decompress IDAT data
        byte[] idatData = ExtractIdatData(png);
        using var decompressed = new MemoryStream();
        using (var zlibStream = new ZLibStream(new MemoryStream(idatData), CompressionMode.Decompress))
        {
            zlibStream.CopyTo(decompressed);
        }

        byte[] raw = decompressed.ToArray();
        // Row: filter byte (0) + RGB pixel data (alpha composited against white)
        Assert.Equal(4, raw.Length); // 1 filter byte + 3 bytes for 1 RGB pixel
        Assert.Equal(0, raw[0]); // Filter byte = None

        // Alpha-composite: out = src * a/255 + 255 * (1 - a/255)
        // R: (255 * 200 + 255 * 55) / 255 = 255
        // G: (128 * 200 + 255 * 55) / 255 = 155
        // B: (0 * 200 + 255 * 55) / 255 = 55
        Assert.Equal(255, raw[1]); // R
        Assert.Equal(155, raw[2]); // G
        Assert.Equal(55, raw[3]);  // B
    }

    [Fact]
    public void Encode_EndsWithIendChunk()
    {
        byte[] bgra = new byte[2 * 2 * 4];
        byte[] png = PngEncoder.Encode(bgra, 2, 2);

        // IEND chunk: 4 bytes length (0) + 4 bytes "IEND" + 4 bytes CRC = 12 bytes
        int iendStart = png.Length - 12;

        int iendLength = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(iendStart, 4));
        string iendType = System.Text.Encoding.ASCII.GetString(png, iendStart + 4, 4);

        Assert.Equal(0, iendLength);
        Assert.Equal("IEND", iendType);
    }

    [Fact]
    public void Encode_InvalidDataLength_ThrowsArgumentException()
    {
        byte[] bgra = new byte[10]; // Not a valid size for any dimensions

        Assert.Throws<ArgumentException>(() => PngEncoder.Encode(bgra, 2, 2));
    }

    [Fact]
    public void Encode_SinglePixel_ProducesValidPng()
    {
        byte[] bgra = [100, 150, 200, 255];
        byte[] png = PngEncoder.Encode(bgra, 1, 1);

        // Verify PNG signature
        Assert.Equal(PngSignature, png[..8]);

        // Verify IHDR
        int ihdrDataOffset = 8 + 4 + 4;
        int width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(ihdrDataOffset, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(ihdrDataOffset + 4, 4));
        Assert.Equal(1, width);
        Assert.Equal(1, height);

        // Verify it ends with IEND
        int iendStart = png.Length - 12;
        string iendType = System.Text.Encoding.ASCII.GetString(png, iendStart + 4, 4);
        Assert.Equal("IEND", iendType);
    }

    [Fact]
    public void Encode_100x100_ProducesValidPng()
    {
        int width = 100, height = 100;
        byte[] bgra = new byte[width * height * 4];

        // Fill with a repeating pattern
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = (byte)(i % 256);       // B
            bgra[i + 1] = (byte)(i / 2 % 256); // G
            bgra[i + 2] = (byte)(i / 3 % 256); // R
            bgra[i + 3] = 255;                 // A
        }

        byte[] png = PngEncoder.Encode(bgra, width, height);

        // Verify PNG signature
        Assert.Equal(PngSignature, png[..8]);

        // Verify IHDR dimensions
        int ihdrDataOffset = 8 + 4 + 4;
        int parsedWidth = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(ihdrDataOffset, 4));
        int parsedHeight = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(ihdrDataOffset + 4, 4));
        Assert.Equal(width, parsedWidth);
        Assert.Equal(height, parsedHeight);
    }

    /// <summary>
    /// Walks the PNG chunks and extracts concatenated IDAT data.
    /// </summary>
    private static byte[] ExtractIdatData(byte[] png)
    {
        using var result = new MemoryStream();
        int offset = 8; // Skip PNG signature

        while (offset < png.Length)
        {
            int dataLength = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            string chunkType = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);

            if (chunkType == "IDAT")
            {
                result.Write(png, offset + 8, dataLength);
            }

            // Move to next chunk: 4 (length) + 4 (type) + data + 4 (CRC)
            offset += 4 + 4 + dataLength + 4;
        }

        return result.ToArray();
    }
}
