using System.Runtime.InteropServices;
using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tests;

public class ConvertToBgraTests
{
    [Fact]
    public void ConvertToBgra_BgraFormat_CopiesDirectly()
    {
        // Arrange: 2x2 BGRA image (format 4), stride = width * 4
        int width = 2, height = 2;
        int stride = width * 4;
        byte[] source =
        [
            10, 20, 30, 255,   40, 50, 60, 128,   // row 0
            70, 80, 90, 200,   100, 110, 120, 0    // row 1
        ];

        var handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        try
        {
            var result = PageImagesService.ConvertToBgra(
                handle.AddrOfPinnedObject(), width, height, stride, format: 4);

            Assert.Equal(source, result);
        }
        finally
        {
            handle.Free();
        }
    }

    [Fact]
    public void ConvertToBgra_BgraFormat_HandlesStridePadding()
    {
        // Arrange: 2x1 BGRA with stride = 12 (8 bytes data + 4 bytes padding)
        int width = 2, height = 1;
        int stride = 12;
        byte[] source =
        [
            10, 20, 30, 255,   40, 50, 60, 128,   0, 0, 0, 0  // data + padding
        ];

        var handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        try
        {
            var result = PageImagesService.ConvertToBgra(
                handle.AddrOfPinnedObject(), width, height, stride, format: 4);

            byte[] expected = [10, 20, 30, 255, 40, 50, 60, 128];
            Assert.Equal(expected, result);
        }
        finally
        {
            handle.Free();
        }
    }

    [Fact]
    public void ConvertToBgra_BgrxFormat_SetsAlphaTo255()
    {
        // Arrange: 2x1 BGRx (format 3) — alpha byte is garbage, should become 255
        int width = 2, height = 1;
        int stride = width * 4;
        byte[] source =
        [
            10, 20, 30, 0,   40, 50, 60, 99
        ];

        var handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        try
        {
            var result = PageImagesService.ConvertToBgra(
                handle.AddrOfPinnedObject(), width, height, stride, format: 3);

            byte[] expected = [10, 20, 30, 255, 40, 50, 60, 255];
            Assert.Equal(expected, result);
        }
        finally
        {
            handle.Free();
        }
    }

    [Fact]
    public void ConvertToBgra_BgrFormat_ExpandsToFourChannels()
    {
        // Arrange: 2x2 BGR (format 2), 3 bytes per pixel, stride = width * 3
        int width = 2, height = 2;
        int stride = width * 3;
        byte[] source =
        [
            10, 20, 30,   40, 50, 60,     // row 0: B,G,R for 2 pixels
            70, 80, 90,   100, 110, 120    // row 1
        ];

        var handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        try
        {
            var result = PageImagesService.ConvertToBgra(
                handle.AddrOfPinnedObject(), width, height, stride, format: 2);

            byte[] expected =
            [
                10, 20, 30, 255,   40, 50, 60, 255,     // row 0 BGRA
                70, 80, 90, 255,   100, 110, 120, 255    // row 1 BGRA
            ];
            Assert.Equal(expected, result);
        }
        finally
        {
            handle.Free();
        }
    }

    [Fact]
    public void ConvertToBgra_BgrFormat_HandlesStridePadding()
    {
        // Arrange: 2x1 BGR with stride = 8 (6 bytes data + 2 bytes padding)
        int width = 2, height = 1;
        int stride = 8;
        byte[] source =
        [
            10, 20, 30,   40, 50, 60,   0, 0   // data + padding
        ];

        var handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        try
        {
            var result = PageImagesService.ConvertToBgra(
                handle.AddrOfPinnedObject(), width, height, stride, format: 2);

            byte[] expected = [10, 20, 30, 255, 40, 50, 60, 255];
            Assert.Equal(expected, result);
        }
        finally
        {
            handle.Free();
        }
    }

    [Fact]
    public void ConvertToBgra_GrayFormat_ExpandsGrayToAllChannels()
    {
        // Arrange: 2x2 grayscale (format 1), 1 byte per pixel
        int width = 2, height = 2;
        int stride = width;
        byte[] source = [50, 100, 150, 200];

        var handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        try
        {
            var result = PageImagesService.ConvertToBgra(
                handle.AddrOfPinnedObject(), width, height, stride, format: 1);

            byte[] expected =
            [
                50, 50, 50, 255,     100, 100, 100, 255,     // row 0
                150, 150, 150, 255,  200, 200, 200, 255      // row 1
            ];
            Assert.Equal(expected, result);
        }
        finally
        {
            handle.Free();
        }
    }

    [Fact]
    public void ConvertToBgra_GrayFormat_HandlesStridePadding()
    {
        // Arrange: 2x1 grayscale with stride = 4 (2 bytes data + 2 bytes padding)
        int width = 2, height = 1;
        int stride = 4;
        byte[] source = [50, 100, 0, 0];

        var handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        try
        {
            var result = PageImagesService.ConvertToBgra(
                handle.AddrOfPinnedObject(), width, height, stride, format: 1);

            byte[] expected = [50, 50, 50, 255, 100, 100, 100, 255];
            Assert.Equal(expected, result);
        }
        finally
        {
            handle.Free();
        }
    }

    [Fact]
    public void ConvertToBgra_UnsupportedFormat_ThrowsInvalidOperationException()
    {
        int width = 1, height = 1;
        int stride = 4;
        byte[] source = [0, 0, 0, 0];

        var handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                PageImagesService.ConvertToBgra(
                    handle.AddrOfPinnedObject(), width, height, stride, format: 99));

            Assert.Contains("Unsupported", ex.Message);
            Assert.Contains("99", ex.Message);
        }
        finally
        {
            handle.Free();
        }
    }

    [Fact]
    public void ConvertToBgra_BgrFormat_SinglePixel()
    {
        int width = 1, height = 1;
        int stride = 3;
        byte[] source = [255, 128, 64]; // B=255, G=128, R=64

        var handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        try
        {
            var result = PageImagesService.ConvertToBgra(
                handle.AddrOfPinnedObject(), width, height, stride, format: 2);

            byte[] expected = [255, 128, 64, 255];
            Assert.Equal(expected, result);
        }
        finally
        {
            handle.Free();
        }
    }

    [Fact]
    public void ConvertToBgra_GrayFormat_BlackAndWhite()
    {
        int width = 2, height = 1;
        int stride = 2;
        byte[] source = [0, 255]; // black, white

        var handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        try
        {
            var result = PageImagesService.ConvertToBgra(
                handle.AddrOfPinnedObject(), width, height, stride, format: 1);

            byte[] expected = [0, 0, 0, 255, 255, 255, 255, 255];
            Assert.Equal(expected, result);
        }
        finally
        {
            handle.Free();
        }
    }
}
