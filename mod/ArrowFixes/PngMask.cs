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
            var pixels = Decode(stream, out int width, out int height, out int channels);
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

        /// <summary>A PNG's pixels as RGBA bytes, rows top first.</summary>
        public static byte[] ReadRgba(Stream stream, out int width, out int height)
        {
            var pixels = Decode(stream, out width, out height, out int channels);
            var rgba = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                int o = i * channels;
                byte grey = pixels[o];
                rgba[i * 4] = grey;
                rgba[i * 4 + 1] = channels >= 3 ? pixels[o + 1] : grey;
                rgba[i * 4 + 2] = channels >= 3 ? pixels[o + 2] : grey;
                rgba[i * 4 + 3] = channels == 4 ? pixels[o + 3] : channels == 2 ? pixels[o + 1] : (byte)255;
            }
            return rgba;
        }

        /// <summary>The raw 8-bit pixels (rows top first) of a non-interlaced PNG, and its channel count.</summary>
        private static byte[] Decode(Stream stream, out int width, out int height, out int channels)
        {
            var reader = new BinaryReader(stream);
            var signature = reader.ReadBytes(8);
            if (signature.Length != 8 || signature[1] != 'P' || signature[2] != 'N' || signature[3] != 'G')
                throw new FormatException("not a PNG");
            width = 0;
            height = 0;
            int colorType = 0;
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
                    if (data[8] != 8 || data[12] != 0) throw new FormatException("only 8-bit, non-interlaced PNGs");
                    colorType = data[9];
                }
                else if (type == "IDAT") idat.Write(data, 0, data.Length);
                else if (type == "IEND") break;
            }
            channels = colorType == 6 ? 4 : colorType == 2 ? 3 : colorType == 4 ? 2 : colorType == 0 ? 1 :
                throw new FormatException("unsupported PNG colour type " + colorType);

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
            return pixels;
        }

        /// <summary>A mask from 8-bit alpha values, rows bottom first (Unity's texture order).</summary>
        public static PngMask FromAlpha(int width, int height, byte[] alphaBottomUp)
        {
            var coverage = new float[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                coverage[y * width + x] = alphaBottomUp[(height - 1 - y) * width + x] / 255f;
            return new PngMask { Width = width, Height = height, _coverage = coverage };
        }

        /// <summary>
        /// The shape only: a game icon's chalky grain (see-through specks) filled in (grown by a pixel, then any real
        /// coverage counts as solid), so the icon painter's own grain isn't added on top of it.
        /// </summary>
        public PngMask AsSolidShape()
        {
            var solid = new float[_coverage.Length];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float max = 0f;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int sx = x + dx, sy = y + dy;
                    if (sx >= 0 && sx < Width && sy >= 0 && sy < Height) max = Math.Max(max, _coverage[sy * Width + sx]);
                }
                solid[y * Width + x] = Math.Max(0f, Math.Min(1f, (max - 0.08f) / 0.2f));
            }
            return new PngMask { Width = Width, Height = Height, _coverage = solid }.CroppedSquare();
        }

        /// <summary>The square around what is covered (centred, a pixel of margin), so the shape fills a badge box.</summary>
        private PngMask CroppedSquare()
        {
            int minX = Width, minY = Height, maxX = -1, maxY = -1;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                if (_coverage[y * Width + x] <= 0.1f) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            if (maxX < 0) return this;
            int size = Math.Max(maxX - minX, maxY - minY) + 3;
            int left = (minX + maxX) / 2 - size / 2, top = (minY + maxY) / 2 - size / 2;
            var cropped = new float[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int sx = left + x, sy = top + y;
                cropped[y * size + x] = sx >= 0 && sx < Width && sy >= 0 && sy < Height ? _coverage[sy * Width + sx] : 0f;
            }
            return new PngMask { Width = size, Height = size, _coverage = cropped };
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
