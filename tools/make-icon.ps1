# Converts the Verzlo logo (navy line art on a light background) into
# transparent multi-size .ico files (16/24/32/48/256).
#   hermes.ico        navy, used for the exe and for light taskbars
#   hermes-light.ico  white, used for the tray icon on dark taskbars
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param(
    [string]$Source = "$PSScriptRoot\..\src\Hermes\Assets\verzlo-logo.jpg",
    [string]$OutDir = "$PSScriptRoot\..\src\Hermes\Assets"
)

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class IconMaker
{
    // Luminance at or above this is background, at or below InkLum is full ink.
    const double BgLum = 200, InkLum = 90;

    public static void Make(string src, string outDir)
    {
        using (var input = new Bitmap(src))
        {
            int w = input.Width, h = input.Height;
            var alpha = new double[w, h];
            int minX = w, minY = h, maxX = 0, maxY = 0;
            double inkR = 0, inkG = 0, inkB = 0, inkN = 0;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = input.GetPixel(x, y);
                double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                double a = Math.Max(0, Math.Min(1, (BgLum - lum) / (BgLum - InkLum)));
                alpha[x, y] = a;
                if (a > 0.1) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
                if (a >= 1) { inkR += c.R; inkG += c.G; inkB += c.B; inkN++; }
            }
            Color ink = Color.FromArgb((int)(inkR / inkN), (int)(inkG / inkN), (int)(inkB / inkN));

            // Square crop around the logo with a little padding.
            int side = Math.Max(maxX - minX, maxY - minY) + 1;
            int pad = (int)(side * 0.04);
            side += pad * 2;
            int cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            int ox = cx - side / 2, oy = cy - side / 2;

            Write(Path.Combine(outDir, "hermes.ico"), Render(alpha, w, h, ox, oy, side, ink));
            Write(Path.Combine(outDir, "hermes-light.ico"), Render(alpha, w, h, ox, oy, side, Color.White));
            using (var png = Master(alpha, w, h, ox, oy, side, ink)) png.Save(Path.Combine(outDir, "hermes-256.png"), ImageFormat.Png);
        }
    }

    static Bitmap Master(double[,] alpha, int w, int h, int ox, int oy, int side, Color color)
    {
        var bmp = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        for (int y = 0; y < side; y++)
        for (int x = 0; x < side; x++)
        {
            int sx = ox + x, sy = oy + y;
            double a = (sx >= 0 && sy >= 0 && sx < w && sy < h) ? alpha[sx, sy] : 0;
            bmp.SetPixel(x, y, Color.FromArgb((int)Math.Round(a * 255), color));
        }
        return bmp;
    }

    static List<KeyValuePair<int, Bitmap>> Render(double[,] alpha, int w, int h, int ox, int oy, int side, Color color)
    {
        var list = new List<KeyValuePair<int, Bitmap>>();
        using (var master = Master(alpha, w, h, ox, oy, side, color))
        {
            foreach (int size in new[] { 16, 24, 32, 48, 256 })
            {
                var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(master, new Rectangle(0, 0, size, size));
                }
                // Thin strokes wash out when shrunk; boost coverage on small sizes.
                if (size <= 32)
                {
                    double boost = size <= 16 ? 1.8 : 1.4;
                    for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        Color c = bmp.GetPixel(x, y);
                        bmp.SetPixel(x, y, Color.FromArgb((int)Math.Min(255, c.A * boost), color));
                    }
                }
                list.Add(new KeyValuePair<int, Bitmap>(size, bmp));
            }
        }
        return list;
    }

    static void Write(string path, List<KeyValuePair<int, Bitmap>> images)
    {
        var blobs = new List<byte[]>();
        foreach (var kv in images) blobs.Add(kv.Key >= 256 ? Png(kv.Value) : Dib(kv.Value));

        using (var fs = File.Create(path))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)images.Count);
            int offset = 6 + 16 * images.Count;
            for (int i = 0; i < images.Count; i++)
            {
                int size = images[i].Key;
                bw.Write((byte)(size >= 256 ? 0 : size));
                bw.Write((byte)(size >= 256 ? 0 : size));
                bw.Write((byte)0); bw.Write((byte)0);
                bw.Write((short)1); bw.Write((short)32);
                bw.Write(blobs[i].Length); bw.Write(offset);
                offset += blobs[i].Length;
            }
            foreach (var b in blobs) bw.Write(b);
        }
        foreach (var kv in images) kv.Value.Dispose();
    }

    static byte[] Png(Bitmap bmp)
    {
        using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    // 32bpp BGRA DIB, bottom-up, followed by an all-zero AND mask.
    static byte[] Dib(Bitmap bmp)
    {
        int s = bmp.Width;
        int maskStride = ((s + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(40); bw.Write(s); bw.Write(s * 2); bw.Write((short)1); bw.Write((short)32);
            bw.Write(0); bw.Write(s * s * 4 + maskStride * s); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            for (int y = s - 1; y >= 0; y--)
            for (int x = 0; x < s; x++)
            {
                Color c = bmp.GetPixel(x, y);
                bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
            }
            bw.Write(new byte[maskStride * s]);
            return ms.ToArray();
        }
    }
}
"@

[IconMaker]::Make((Resolve-Path $Source).Path, (Resolve-Path $OutDir).Path)
Write-Host "Icons written to $OutDir"
