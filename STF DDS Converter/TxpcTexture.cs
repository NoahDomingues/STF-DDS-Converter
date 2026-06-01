using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace STF_DDS_Converter
{
    /// <summary>
    /// OFDR TXPC (.stf) texture container: small header, mip offset table, mip blobs (optional 16-byte OFDR prefix per level).
    /// </summary>
    public sealed class TxpcTexture
    {
        public const int OfdrMipPrefixSize = 16;

        public byte[] PreMipBytes { get; init; } = Array.Empty<byte>();
        public List<MipSegment> Segments { get; init; } = new();
        public string Format { get; init; } = "DXT1";
        public int HeaderWidth { get; init; }
        public int DdsWidth { get; init; }
        public int DdsHeight { get; init; }

        public sealed class MipSegment
        {
            public int Size { get; init; }
            public bool HasOfdrPrefix { get; init; }
            public byte[] Prefix { get; init; } = Array.Empty<byte>();
            public int DxtByteLength => HasOfdrPrefix ? Math.Max(0, Size - OfdrMipPrefixSize) : Size;
        }

        public sealed class Sidecar
        {
            public string Format { get; set; } = "DXT1";
            public int HeaderWidth { get; set; }
            public int DdsWidth { get; set; }
            public int DdsHeight { get; set; }
            public List<SidecarMip> Mips { get; set; } = new();
        }

        public sealed class SidecarMip
        {
            public int Size { get; set; }
            public bool HasOfdrPrefix { get; set; }
            public string? PrefixHex { get; set; }
        }

        public static TxpcTexture ParseFromStf(string stfPath) => Parse(File.ReadAllBytes(stfPath));

        public static TxpcTexture Parse(byte[] data)
        {
            if (data.Length < 0x48 || ReadAscii(data, 0, 4) != "TXPC")
                throw new InvalidDataException("Not a TXPC (.stf) file.");

            int headerSize = ReadU32Le(data, 4);
            if (headerSize < 0x10 || headerSize > data.Length)
                throw new InvalidDataException($"Invalid TXPC header_size: {headerSize}");

            string format = DetectCompression(data)
                ?? throw new InvalidDataException("Unknown compression (expected DXT1/DXT5 in header).");
            int headerWidth = DetectHeaderWidth(data)
                ?? throw new InvalidDataException("Could not detect texture width from TXPC header.");

            var mipOffsets = ReadMipOffsets(data);
            if (mipOffsets.Count == 0 || mipOffsets[0] <= headerSize)
                throw new InvalidDataException("Mip offset table missing or invalid.");

            int mipStart = mipOffsets[0];
            var segments = new List<MipSegment>();
            for (int i = 0; i < mipOffsets.Count; i++)
            {
                int start = mipOffsets[i];
                int end = (i + 1 < mipOffsets.Count) ? mipOffsets[i + 1] : data.Length;
                if (end <= start)
                    continue;
                int size = end - start;
                byte[] chunk = new byte[size];
                Buffer.BlockCopy(data, start, chunk, 0, size);
                bool wrap = LooksLikeOfdrPrefix(chunk);
                byte[] prefix = wrap ? chunk[..OfdrMipPrefixSize] : Array.Empty<byte>();
                segments.Add(new MipSegment { Size = size, HasOfdrPrefix = wrap, Prefix = prefix });
            }

            if (segments.Count == 0)
                throw new InvalidDataException("No mip segments found.");

            int ddsWidth = InferDimensionFromMip0(segments[0].DxtByteLength, format);

            byte[] preMip = new byte[mipStart];
            Buffer.BlockCopy(data, 0, preMip, 0, mipStart);

            return new TxpcTexture
            {
                PreMipBytes = preMip,
                Segments = segments,
                Format = format,
                HeaderWidth = headerWidth,
                DdsWidth = ddsWidth,
                DdsHeight = ddsWidth,
            };
        }

        public Sidecar ToSidecar() => new Sidecar
        {
            Format = Format,
            HeaderWidth = HeaderWidth,
            DdsWidth = DdsWidth,
            DdsHeight = DdsHeight,
            Mips = Segments.Select(s => new SidecarMip
            {
                Size = s.Size,
                HasOfdrPrefix = s.HasOfdrPrefix,
                PrefixHex = s.HasOfdrPrefix ? Convert.ToHexString(s.Prefix) : null,
            }).ToList(),
        };

        public static Sidecar LoadSidecar(string path)
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Sidecar>(json)
                ?? throw new InvalidDataException("Invalid sidecar JSON.");
        }

        public static void SaveSidecar(Sidecar sidecar, string path)
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(sidecar, opts));
        }

        public static TxpcTexture FromSidecar(Sidecar sidecar, byte[] preMipBytes)
        {
            var segments = sidecar.Mips.Select(m =>
            {
                byte[] prefix = Array.Empty<byte>();
                if (m.HasOfdrPrefix && !string.IsNullOrEmpty(m.PrefixHex))
                    prefix = Convert.FromHexString(m.PrefixHex);
                return new MipSegment
                {
                    Size = m.Size,
                    HasOfdrPrefix = m.HasOfdrPrefix,
                    Prefix = prefix,
                };
            }).ToList();

            return new TxpcTexture
            {
                PreMipBytes = preMipBytes,
                Segments = segments,
                Format = sidecar.Format,
                HeaderWidth = sidecar.HeaderWidth,
                DdsWidth = sidecar.DdsWidth,
                DdsHeight = sidecar.DdsHeight,
            };
        }

        public byte[] BuildLinearDxt(byte[] stfData)
        {
            var mipOffsets = ReadMipOffsets(stfData);
            using var ms = new MemoryStream();
            for (int i = 0; i < Segments.Count; i++)
            {
                int start = mipOffsets[i];
                var seg = Segments[i];
                int dxtLen = seg.DxtByteLength;
                if (dxtLen > 0)
                    ms.Write(stfData, start + (seg.HasOfdrPrefix ? OfdrMipPrefixSize : 0), dxtLen);
            }
            return ms.ToArray();
        }

        public byte[] BuildMipBlobFromLinearDxt(byte[] linearDxt)
        {
            using var ms = new MemoryStream();
            int pos = 0;
            foreach (var seg in Segments)
            {
                int dxtLen = seg.DxtByteLength;
                if (seg.HasOfdrPrefix)
                {
                    if (seg.Prefix.Length == OfdrMipPrefixSize)
                        ms.Write(seg.Prefix, 0, OfdrMipPrefixSize);
                    else
                        ms.Write(new byte[OfdrMipPrefixSize], 0, OfdrMipPrefixSize);
                }
                if (dxtLen > 0)
                {
                    if (pos + dxtLen > linearDxt.Length)
                        throw new InvalidDataException(
                            "DDS mip data is too short. Re-export with all mip levels preserved (see README).");
                    ms.Write(linearDxt, pos, dxtLen);
                    pos += dxtLen;
                }
            }
            if (pos != linearDxt.Length)
                throw new InvalidDataException(
                    $"DDS mip data length mismatch: STF expects {pos} bytes of DXT data, DDS has {linearDxt.Length}.");
            return ms.ToArray();
        }

        public static byte[] BuildDdsFile(byte[] linearDxt, int width, int height, string format)
        {
            byte[] header = BuildDdsHeader(width, height, format, CountMipLevels(width, height));
            var result = new byte[header.Length + linearDxt.Length];
            Buffer.BlockCopy(header, 0, result, 0, header.Length);
            Buffer.BlockCopy(linearDxt, 0, result, header.Length, linearDxt.Length);
            return result;
        }

        public static byte[] ReadLinearDxtFromDds(string ddsPath)
        {
            byte[] dds = File.ReadAllBytes(ddsPath);
            int headerLen = GetDdsHeaderLength(dds);
            var payload = new byte[dds.Length - headerLen];
            Buffer.BlockCopy(dds, headerLen, payload, 0, payload.Length);
            return payload;
        }

        public static string? DetectCompression(byte[] header)
        {
            if (header.Length < 10) return null;
            ushort code = BitConverter.ToUInt16(header, 0x08);
            return code switch
            {
                0xB2B8 => "DXT1",
                0x5D70 => "DXT5",
                _ => null,
            };
        }

        public static int? DetectHeaderWidth(byte[] header)
        {
            foreach (int off in new[] { 0x44, 0x45 })
            {
                if (header.Length > off)
                {
                    byte exp = header[off];
                    if (exp >= 1 && exp <= 13)
                        return 1 << exp;
                }
            }
            return null;
        }

        private static List<int> ReadMipOffsets(byte[] data)
        {
            var list = new List<int>();
            for (int i = 0; i < 16; i++)
            {
                int off = 0x10 + i * 4;
                if (off + 4 > data.Length) break;
                int v = ReadU32Le(data, off);
                if (v == 0) break;
                if (v >= data.Length) break;
                if (list.Count > 0 && v <= list[^1]) break;
                list.Add(v);
            }
            return list;
        }

        private static bool LooksLikeOfdrPrefix(byte[] chunk)
        {
            if (chunk.Length < OfdrMipPrefixSize) return false;
            return chunk[0] == 0x00 && chunk[1] == 0x01 && chunk[2] == 0x00 && chunk[3] == 0x00
                && chunk[4] == 0x00 && chunk[5] == 0x00 && chunk[6] == 0x00 && chunk[7] == 0x00;
        }

        public static int InferDimensionFromMip0(int mip0DxtBytes, string format)
        {
            int blockBytes = format == "DXT1" ? 8 : 16;
            if (mip0DxtBytes <= 0 || mip0DxtBytes % blockBytes != 0)
                throw new InvalidDataException($"Mip0 DXT size {mip0DxtBytes} is not valid for {format}.");
            int blockCount = mip0DxtBytes / blockBytes;
            int blocksPerSide = (int)Math.Round(Math.Sqrt(blockCount));
            if (blocksPerSide * blocksPerSide != blockCount)
                throw new InvalidDataException($"Cannot infer square texture size from mip0 ({mip0DxtBytes} bytes).");
            return blocksPerSide * 4;
        }

        private static int CountMipLevels(int width, int height)
        {
            int n = 0;
            int w = width, h = height;
            while (w >= 4 && h >= 4)
            {
                n++;
                w >>= 1;
                h >>= 1;
            }
            return Math.Max(1, n);
        }

        private static int GetDdsHeaderLength(byte[] dds)
        {
            if (dds.Length < 128 || ReadAscii(dds, 0, 4) != "DDS ")
                throw new InvalidDataException("Not a DDS file (missing DDS magic).");
            if (dds.Length >= 148 && ReadU32Le(dds, 128) == 0x30315844)
                return 148;
            return 128;
        }

        public static byte[] BuildDdsHeader(int width, int height, string format, int mipCount)
        {
            var h = new byte[128];
            WriteAscii(h, 0, "DDS ");
            WriteU32Le(h, 4, 124);
            uint flags = 0x00021007 | 0x00020000;
            WriteU32Le(h, 8, flags);
            WriteU32Le(h, 12, height);
            WriteU32Le(h, 16, width);
            int pitch = format == "DXT1" ? Math.Max(1, width / 2) : width;
            WriteU32Le(h, 20, pitch);
            WriteU32Le(h, 24, mipCount);
            WriteU32Le(h, 76, 32);
            WriteU32Le(h, 80, 0x4);
            uint fourCC = format switch
            {
                "DXT1" => 0x31545844u,
                "DXT3" => 0x33545844u,
                "DXT5" => 0x35545844u,
                _ => throw new ArgumentException($"Unsupported format: {format}"),
            };
            WriteU32Le(h, 84, fourCC);
            WriteU32Le(h, 108, 0x401008);
            return h;
        }

        private static string ReadAscii(byte[] b, int off, int len)
            => System.Text.Encoding.ASCII.GetString(b, off, len);

        private static int ReadU32Le(byte[] b, int off) => BitConverter.ToInt32(b, off);
        private static void WriteU32Le(byte[] b, int off, uint v) => BitConverter.TryWriteBytes(b.AsSpan(off), v);
        private static void WriteU32Le(byte[] b, int off, int v) => WriteU32Le(b, off, (uint)v);
        private static void WriteAscii(byte[] b, int off, string s)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(s);
            Buffer.BlockCopy(bytes, 0, b, off, bytes.Length);
        }
    }
}
