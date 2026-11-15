using System;
using System.Globalization;
using System.IO;
using System.Text;
using ArrowFixes;

// Usage: dotnet run --project scripts/ClothPreview -- <output dir>
// Writes BoneArrowCloth.obj and ModernArrowCloth.obj in Unity mesh space (no axis mirroring).
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
