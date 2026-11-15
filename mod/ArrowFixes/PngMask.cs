using System;
using System.IO;
using System.IO.Compression;

namespace ArrowFixes
{
    /// <summary>
    /// Reads the coverage (alpha, or darkness when there is no alpha) of a non-interlaced 8-bit PNG. Small and
    /// dependency-free so the plugin (.NET 3.5) and scripts/ClothPreview share it for the icon badges.
    /// </summary>
    internal sealed class PngMask
    {
        public int Width, Height;
        private float[] _coverage; // row 0 = top

        public static PngMask Read(Stream stream)
        {
            var reader = new BinaryReader(stream);
            var signature = reader.ReadBytes(8);
            if (signature.Length != 8 || signature[1] != 'P' || signature[2] != 'N' || signature[3] != 'G')
                throw new InvalidDataException("not a PNG");
            int width = 0, height = 0, colorType = 0;
            var idat = new MemoryStream();
            while (true)
            {
                int length = BigEndian(reader.ReadBytes(4));
                string type = new string(Array.ConvertAll(reader.ReadBytes(4), b => (char)b));
                var data = reader.ReadBytes(length);
                reader.ReadBytes(4); // CRC
                if (type == "IHDR")
                {
                    width = BigEndian(data);
                    height = BigEndian(new[] { data[4], data[5], data[6], data[7] });
                    if (data[8] != 8 || data[12] != 0) throw new InvalidDataException("only 8-bit, non-interlaced PNGs");
                    colorType = data[9];
                }
                else if (type == "IDAT") idat.Write(data, 0, data.Length);
                else if (type == "IEND") break;
            }
            int channels = colorType == 6 ? 4 : colorType == 2 ? 3 : colorType == 4 ? 2 : colorType == 0 ? 1 :
                throw new InvalidDataException("unsupported PNG colour type " + colorType);

            idat.Position = 2; // skip the zlib header
            var raw = new MemoryStream();
            using (var inflate = new DeflateStream(idat, CompressionMode.Decompress))
            {
                var buffer = new byte[16384];
                int n;
                while ((n = inflate.Read(buffer, 0, buffer.Length)) > 0) raw.Write(buffer, 0, n);
            }
            var bytes = raw.ToArray();
            int stride = width * channels;
            var pixels = new byte[stride * height];
            var prev = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                int filter = bytes[y * (stride + 1)];
                var row = new byte[stride];
                Buffer.BlockCopy(bytes, y * (stride + 1) + 1, row, 0, stride);
                for (int i = 0; i < stride; i++)
                {
                    int a = i >= channels ? row[i - channels] : 0, b = prev[i], c = i >= channels ? prev[i - channels] : 0;
                    switch (filter)
                    {
                        case 1: row[i] = (byte)(row[i] + a); break;
                        case 2: row[i] = (byte)(row[i] + b); break;
                        case 3: row[i] = (byte)(row[i] + (a + b) / 2); break;
                        case 4: row[i] = (byte)(row[i] + Paeth(a, b, c)); break;
                    }
                }
                Buffer.BlockCopy(row, 0, pixels, y * stride, stride);
                prev = row;
            }

            var coverage = new float[width * height];
            for (int i = 0; i < coverage.Length; i++)
            {
                int o = i * channels;
                coverage[i] = channels == 4 ? pixels[o + 3] / 255f
                    : channels == 2 ? pixels[o + 1] / 255f
                    : 1f - (channels == 3 ? (pixels[o] + pixels[o + 1] + pixels[o + 2]) / 765f : pixels[o] / 255f);
            }
            return new PngMask { Width = width, Height = height, _coverage = coverage };
        }

        /// <summary>Bilinear coverage at (u, v) in [0,1]², v = 0 at the top; 0 outside.</summary>
        public float Sample(float u, float v)
        {
            if (u < 0f || u > 1f || v < 0f || v > 1f) return 0f;
            float x = u * (Width - 1), y = v * (Height - 1);
            int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, Width - 1), y1 = Math.Min(y0 + 1, Height - 1);
            float fx = x - x0, fy = y - y0;
            float top = _coverage[y0 * Width + x0] * (1 - fx) + _coverage[y0 * Width + x1] * fx;
            float bottom = _coverage[y1 * Width + x0] * (1 - fx) + _coverage[y1 * Width + x1] * fx;
            return top * (1 - fy) + bottom * fy;
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        private static int BigEndian(byte[] b) => (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
    }
}
