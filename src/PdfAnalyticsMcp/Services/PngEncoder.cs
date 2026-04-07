using System.Buffers.Binary;
using System.IO.Compression;

namespace PdfAnalyticsMcp.Services;

/// <summary>
/// Encodes raw BGRA pixel data into a valid PNG file using only built-in .NET APIs.
/// </summary>
internal static class PngEncoder
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] Crc32Table = GenerateCrc32Table();

    /// <summary>
    /// Encodes raw BGRA pixel data into a PNG byte array.
    /// </summary>
    /// <param name="bgraData">Raw pixel data in BGRA format (4 bytes per pixel).</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="preserveAlpha">
    /// When <c>false</c> (default): composites alpha against white and outputs RGB (color type 2) — used by page rendering.
    /// When <c>true</c>: preserves the alpha channel and outputs RGBA (color type 6) — used by image extraction.
    /// </param>
    /// <returns>A byte array containing a valid PNG file.</returns>
    public static byte[] Encode(byte[] bgraData, int width, int height, bool preserveAlpha = false)
    {
        ArgumentNullException.ThrowIfNull(bgraData);

        int expectedLength = width * height * 4;
        if (bgraData.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Pixel data length {bgraData.Length} does not match expected length {expectedLength} for a {width}x{height} image.",
                nameof(bgraData));
        }

        // Estimate output size — compressed PNG is typically much smaller than raw data
        int estimatedSize = PngSignature.Length + 25 + 12 + bgraData.Length / 2 + 12;
        using var output = new MemoryStream(estimatedSize);

        // PNG signature
        output.Write(PngSignature);

        // IHDR chunk: color type 6 (RGBA) when preserving alpha, 2 (RGB) otherwise
        byte colorType = preserveAlpha ? (byte)6 : (byte)2;
        WriteIhdrChunk(output, width, height, colorType);

        // IDAT chunk(s)
        WriteIdatChunk(output, bgraData, width, height, preserveAlpha);

        // IEND chunk
        WriteIendChunk(output);

        return output.ToArray();
    }

    private static void WriteIhdrChunk(MemoryStream output, int width, int height, byte colorType)
    {
        Span<byte> ihdrData = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdrData[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdrData[4..8], height);
        ihdrData[8] = 8;         // Bit depth
        ihdrData[9] = colorType; // Color type: 2 (RGB) or 6 (RGBA)
        ihdrData[10] = 0; // Compression method: deflate
        ihdrData[11] = 0; // Filter method
        ihdrData[12] = 0; // Interlace method: none

        WriteChunk(output, "IHDR"u8, ihdrData);
    }

    private static void WriteIdatChunk(MemoryStream output, byte[] bgraData, int width, int height, bool preserveAlpha)
    {
        using var compressedStream = new MemoryStream();
        using (var zlibStream = new ZLibStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            int srcRowBytes = width * 4;

            if (preserveAlpha)
            {
                // RGBA mode: convert BGRA to RGBA, preserving alpha as-is
                int dstRowBytes = width * 4;
                Span<byte> rgbaRow = new byte[dstRowBytes];

                for (int y = 0; y < height; y++)
                {
                    zlibStream.WriteByte(0); // filter byte = None

                    int rowStart = y * srcRowBytes;
                    for (int x = 0; x < width; x++)
                    {
                        int srcOffset = rowStart + x * 4;
                        int dstOffset = x * 4;

                        rgbaRow[dstOffset] = bgraData[srcOffset + 2];     // R
                        rgbaRow[dstOffset + 1] = bgraData[srcOffset + 1]; // G
                        rgbaRow[dstOffset + 2] = bgraData[srcOffset];     // B
                        rgbaRow[dstOffset + 3] = bgraData[srcOffset + 3]; // A
                    }

                    zlibStream.Write(rgbaRow);
                }
            }
            else
            {
                // RGB mode: convert BGRA to RGB, compositing alpha against white background
                int dstRowBytes = width * 3;
                Span<byte> rgbRow = new byte[dstRowBytes];

                for (int y = 0; y < height; y++)
                {
                    zlibStream.WriteByte(0); // filter byte = None

                    int rowStart = y * srcRowBytes;
                    for (int x = 0; x < width; x++)
                    {
                        int srcOffset = rowStart + x * 4;
                        int dstOffset = x * 3;

                        byte b = bgraData[srcOffset];
                        byte g = bgraData[srcOffset + 1];
                        byte r = bgraData[srcOffset + 2];
                        byte a = bgraData[srcOffset + 3];

                        // Alpha-composite against white (255): out = src * a/255 + 255 * (1 - a/255)
                        rgbRow[dstOffset] = (byte)((r * a + 255 * (255 - a)) / 255);
                        rgbRow[dstOffset + 1] = (byte)((g * a + 255 * (255 - a)) / 255);
                        rgbRow[dstOffset + 2] = (byte)((b * a + 255 * (255 - a)) / 255);
                    }

                    zlibStream.Write(rgbRow);
                }
            }
        }

        WriteChunk(output, "IDAT"u8, compressedStream.GetBuffer().AsSpan(0, (int)compressedStream.Length));
    }

    private static void WriteIendChunk(MemoryStream output)
    {
        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(MemoryStream output, ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> data)
    {
        Span<byte> lengthBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, data.Length);
        output.Write(lengthBytes);

        output.Write(chunkType);
        output.Write(data);

        // CRC-32 over chunk type + data
        uint crc = ComputeCrc32(chunkType, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;

        foreach (byte b in chunkType)
        {
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (byte b in data)
        {
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static uint[] GenerateCrc32Table()
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
            {
                crc = (crc & 1) != 0 ? (0xEDB88320 ^ (crc >> 1)) : (crc >> 1);
            }
            table[i] = crc;
        }
        return table;
    }
}
