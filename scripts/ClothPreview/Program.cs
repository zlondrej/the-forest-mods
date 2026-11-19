using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using ArrowFixes;

// Usage: dotnet run --project scripts/ClothPreview -- <output dir>
// Writes BoneArrowCloth.obj and ModernArrowCloth.obj in Unity mesh space (no axis mirroring),
// and the arrow-type HUD icons as Icon_<Base>_<Coating>.png.
var outDir = args.Length > 0 ? args[0] : throw new ArgumentException("Pass the output directory");
Directory.CreateDirectory(outDir);
foreach (var spec in new[] { ClothWrapSpecs.Bone, ClothWrapSpecs.Modern })
{
    var m = ClothWrapBuilder.Build(spec);
    var sb = new StringBuilder();
    var ci = CultureInfo.InvariantCulture;
    for (int i = 0; i < m.Positions.Length; i += 3)
        sb.AppendLine(string.Format(ci, "v {0} {1} {2}", m.Positions[i], m.Positions[i + 1], m.Positions[i + 2]));
    for (int i = 0; i < m.Uvs.Length; i += 2)
        sb.AppendLine(string.Format(ci, "vt {0} {1}", m.Uvs[i], m.Uvs[i + 1]));
    for (int i = 0; i < m.Normals.Length; i += 3)
        sb.AppendLine(string.Format(ci, "vn {0} {1} {2}", m.Normals[i], m.Normals[i + 1], m.Normals[i + 2]));
    for (int i = 0; i < m.Triangles.Length; i += 3)
    {
        int a = m.Triangles[i] + 1, b = m.Triangles[i + 1] + 1, c = m.Triangles[i + 2] + 1;
        sb.AppendLine($"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}");
    }
    var path = Path.Combine(outDir, spec.Name + ".obj");
    File.WriteAllText(path, sb.ToString());
    Console.WriteLine($"{path}: {m.Positions.Length / 3} vertices, {m.Triangles.Length / 3} triangles");
}

// Optional: the game's stick/bone HUD icons as PNG (extracted from the game's assets; the plugin reads them in game)
// for the material badges: --stick <png> --bone <png>. Without them, the drawn fallbacks.
for (int i = 1; i + 1 < args.Length; i += 2)
{
    var arrow = args[i] == "--stick" ? IconBase.Wooden : args[i] == "--bone" ? IconBase.Bone : (IconBase?)null;
    if (arrow == null) continue;
    using var stream = File.OpenRead(args[i + 1]);
    ArrowIconPainter.SetMaterialBadge(arrow.Value, PngMask.Read(stream).AsSolidShape());
}

const int iconSize = 152; // same as the game's arrow icon
foreach (IconBase b in Enum.GetValues(typeof(IconBase)))
foreach (IconCoating c in Enum.GetValues(typeof(IconCoating)))
{
    var path = Path.Combine(outDir, $"Icon_{b}_{c}.png");
    Png.Write(path, ArrowIconPainter.Paint(b, c, iconSize), iconSize);
    Console.WriteLine(path);
}

/// <summary>Minimal RGBA PNG writer (input rows bottom-first, as Unity textures).</summary>
static class Png
{
    public static void Write(string path, byte[] rgbaBottomUp, int size)
    {
        var raw = new MemoryStream();
        for (int y = size - 1; y >= 0; y--)
        {
            raw.WriteByte(0);
            raw.Write(rgbaBottomUp, y * size * 4, size * 4);
        }
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) raw.WriteTo(zs);
        using var f = File.Create(path);
        f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        Be(ihdr, 0, size); Be(ihdr, 4, size); ihdr[8] = 8; ihdr[9] = 6;
        Chunk(f, "IHDR", ihdr);
        Chunk(f, "IDAT", z.ToArray());
        Chunk(f, "IEND", Array.Empty<byte>());
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; Be(len, 0, data.Length); s.Write(len);
        var td = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type).CopyTo(td, 0); data.CopyTo(td, 4);
        s.Write(td);
        var crc = new byte[4]; Be(crc, 0, (int)Crc(td)); s.Write(crc);
    }

    private static void Be(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    private static uint Crc(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in data)
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFF;
    }
}
