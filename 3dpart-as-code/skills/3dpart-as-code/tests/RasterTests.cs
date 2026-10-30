using System.IO.Compression;
using Mesh;
using Raster;
using Xunit;
using static Part3dAsCode.Tests.TestMeshes;

namespace Part3dAsCode.Tests;

public class RasterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "part3dascode-raster-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Render_coversTheMiddle_leavesTheCornersWhite_andDrawsOutlines()
    {
        var img = new Scene().Add(MeshOf(Box(0, 0, 0, 10, 10, 10))).Render(View.Front, 200, 160);
        Assert.NotEqual(Rgb.White, img.Get(100, 80));
        Assert.Equal(Rgb.White, img.Get(2, 150));
        Assert.Equal(Rgb.White, img.Get(197, 40));
        var ink = 0;
        for (var y = 0; y < img.H; y++)
            for (var x = 0; x < img.W; x++)
                if (img.Get(x, y) == Rgb.Ink) ink++;
        Assert.True(ink > 200, $"{ink} outline/text pixels");
    }

    [Fact]
    public void EveryView_rendersSomething()
    {
        var s = new Scene().Add(MeshOf(Box(0, 0, 0, 30, 10, 5)));
        foreach (var v in Enum.GetValues<View>())
            Assert.NotEqual(Rgb.White, s.Render(v, 120, 90).Get(60, 45));
    }

    [Fact]
    public void Cameras_areRightHanded()
    {
        foreach (var v in Enum.GetValues<View>())
        {
            var (d, r, u) = Scene.Camera(v);
            Assert.Equal(0, d.Dot(r), 9);
            Assert.Equal(0, d.Dot(u), 9);
            Assert.Equal(1, r.Cross(u).Dot(d * -1), 9);   // right × up points at the viewer
        }
    }

    [Fact]
    public void Callout_drawsAMarker()
    {
        var s = new Scene().Add(MeshOf(Box(0, 0, 0, 10, 10, 10))).Mark(new Callout("D1", new V3(5, 0, 5)));
        var img = s.Render(View.Front, 200, 160);
        var red = 0;
        for (var y = 0; y < img.H; y++)
            for (var x = 0; x < img.W; x++)
                if (img.Get(x, y) == Rgb.Marker) red++;
        Assert.True(red > 100, $"{red} marker pixels");
    }

    [Fact]
    public void EmptyScene_saysSo()
    {
        var img = new Scene().Render(View.Iso, 100, 60, "nothing");
        Assert.Equal(Rgb.White, img.Get(50, 40));
    }

    [Fact]
    public void DegenerateProjection_saysEmpty_insteadOfCrashing()
    {
        // a zero-thickness sheet in the bed plane seen edge-on: every
        // triangle projects to a line, so the depth buffer stays empty
        var img = new Scene().Add(MeshOf(Box(0, 0, 0, 10, 10, 0))).Render(View.Front, 200, 160);
        Assert.Equal(Rgb.White, img.Get(100, 80));
    }

    [Fact]
    public void Png_isWellFormed()
    {
        var img = new Scene().Add(MeshOf(Box(0, 0, 0, 10, 10, 10))).Render(View.Iso, 64, 48);
        var path = Path.Combine(_dir, "x.png");
        img.SavePng(path);
        var (w, h, rgb) = DecodePng(File.ReadAllBytes(path));
        Assert.Equal((64, 48), (w, h));
        Assert.Equal(img.Px, rgb);
    }

    /// A strict little decoder for what SavePng writes: signature, chunk
    /// CRCs, IHDR 8-bit RGB, filter-0 scanlines.
    static (int W, int H, byte[] Rgb) DecodePng(byte[] b)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, b[..8]);
        int pos = 8, w = 0, h = 0;
        using var idat = new MemoryStream();
        while (true)
        {
            var len = (int)BE(b, pos);
            var type = System.Text.Encoding.ASCII.GetString(b, pos + 4, 4);
            Assert.Equal(BE(b, pos + 8 + len), Crc(b.AsSpan(pos + 4, 4 + len)));
            if (type == "IHDR")
            {
                w = (int)BE(b, pos + 8);
                h = (int)BE(b, pos + 12);
                Assert.Equal(8, b[pos + 16]);
                Assert.Equal(2, b[pos + 17]);
            }
            else if (type == "IDAT")
                idat.Write(b, pos + 8, len);
            else if (type == "IEND")
                break;
            pos += 12 + len;
        }
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        var bytes = raw.ToArray();
        Assert.Equal(h * (1 + w * 3), bytes.Length);
        var rgb = new byte[w * h * 3];
        for (var y = 0; y < h; y++)
        {
            Assert.Equal(0, bytes[y * (1 + w * 3)]);
            Array.Copy(bytes, y * (1 + w * 3) + 1, rgb, y * w * 3, w * 3);
        }
        return (w, h, rgb);
    }

    static uint BE(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

    static uint Crc(ReadOnlySpan<byte> data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var x in data)
        {
            c ^= x;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFFu;
    }
}
