using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;

namespace STF_DDS_Converter
{
    public partial class MainWindow : Window
    {
        private const int HeaderSize = 0x800;

        public MainWindow()
        {
            InitializeComponent();
            FormatBox.SelectedIndex = 0; // default to DXT1
        }

        // ----------------------------------------------------------------
        // Button Handler: STF → DDS
        // ----------------------------------------------------------------
        private void ConvertStfToDds_Click(object sender, RoutedEventArgs e)
        {
            // 1) Prompt for .stf file
            var ofd = new OpenFileDialog
            {
                Filter = "STF files|*.stf",
                Title = "Select STF to convert to DDS"
            };
            if (ofd.ShowDialog() != true) return;

            string stfPath = ofd.FileName;
            byte[] header = new byte[HeaderSize];
            long fileLen;

            // 2) Read the first 0x800 bytes
            using (var fs = new FileStream(stfPath, FileMode.Open, FileAccess.Read))
            {
                fileLen = fs.Length;
                if (fs.Read(header, 0, HeaderSize) < HeaderSize)
                {
                    MessageBlock.Text = "File too small or corrupt.";
                    return;
                }
            }

            // 3) Auto‐detect format & width
            string autoFormat = DetectCompression(header);
            int? autoWidth = DetectWidth(header);

            // 4) If auto‐detect fails, show fallback UI
            if (autoFormat == null || autoWidth == null)
            {
                MessageBlock.Text = "Auto‐detect failed – please fill in the values below.";
                WidthBox.Text = autoWidth?.ToString() ?? "";
                FormatBox.Text = autoFormat ?? "";
                return;
            }

            // 5) Overwrite UI with detected values
            MessageBlock.Text = "";
            WidthBox.Text = autoWidth.Value.ToString();
            FormatBox.Text = autoFormat;

            // 6) Perform conversion
            DoStfToDds(stfPath, autoWidth.Value, autoFormat);
        }

        private void DoStfToDds(string stfPath, int width, string format)
        {
            string dir = Path.GetDirectoryName(stfPath);
            string baseName = Path.GetFileNameWithoutExtension(stfPath);
            string headerOut = Path.Combine(dir, baseName + ".header");
            string ddsOut = Path.Combine(dir, baseName + ".dds");

            // a) Save .header (first 0x800 bytes)
            using (var inFs = new FileStream(stfPath, FileMode.Open, FileAccess.Read))
            using (var hdrFs = new FileStream(headerOut, FileMode.Create, FileAccess.Write))
            {
                var buf = new byte[HeaderSize];
                inFs.Read(buf, 0, HeaderSize);
                hdrFs.Write(buf, 0, HeaderSize);
            }

            // b) Build DDS header + padding
            var ddsHdr = BuildDdsHeader(width, format);
            int padLen = HeaderSize - ddsHdr.Length;
            var pad = new byte[padLen];

            // c) Write .dds
            using (var outFs = new FileStream(ddsOut, FileMode.Create, FileAccess.Write))
            {
                outFs.Write(ddsHdr, 0, ddsHdr.Length);
                outFs.Write(pad, 0, padLen);
                using var inFs = new FileStream(stfPath, FileMode.Open, FileAccess.Read);
                inFs.Seek(HeaderSize, SeekOrigin.Begin);
                inFs.CopyTo(outFs);
            }

            MessageBox.Show($"Wrote: {ddsOut}", "Done",
                            MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ----------------------------------------------------------------
        // Button Handler: DDS → STF
        // ----------------------------------------------------------------
        private void ConvertDdsToStf_Click(object sender, RoutedEventArgs e)
        {
            // 1) Prompt for .dds
            var ofd = new OpenFileDialog
            {
                Filter = "DDS files|*.dds",
                Title = "Select DDS to convert to STF"
            };
            if (ofd.ShowDialog() != true) return;

            string ddsPath = ofd.FileName;
            string dir = Path.GetDirectoryName(ddsPath);
            string baseName = Path.GetFileNameWithoutExtension(ddsPath);
            string stfOut = Path.Combine(dir, baseName + ".stf");
            string hdrPath = Path.Combine(dir, baseName + ".header");

            // 2) Ensure .header exists (or ask user)
            if (!File.Exists(hdrPath))
            {
                MessageBlock.Text = ".header not found – please locate it.";
                var hdrOfd = new OpenFileDialog
                {
                    Filter = "Header files|*.header",
                    Title = "Locate the corresponding .header file"
                };
                if (hdrOfd.ShowDialog() != true) return;
                hdrPath = hdrOfd.FileName;
                MessageBlock.Text = "";
            }

            // 3) Rebuild .stf
            using (var outFs = new FileStream(stfOut, FileMode.Create, FileAccess.Write))
            {
                // prepend .header
                using var hdrFs = new FileStream(hdrPath, FileMode.Open, FileAccess.Read);
                hdrFs.CopyTo(outFs);

                // append compressed data from DDS after 0x800
                using var inFs = new FileStream(ddsPath, FileMode.Open, FileAccess.Read);
                inFs.Seek(HeaderSize, SeekOrigin.Begin);
                inFs.CopyTo(outFs);
            }

            MessageBox.Show($"Wrote: {stfOut}", "Done",
                            MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ----------------------------------------------------------------
        // Helper Methods
        // ----------------------------------------------------------------

        // Reads 2 bytes at 0x08 for DXT1/DXT5 tag
        public static string DetectCompression(byte[] header)
        {
            if (header.Length < 10) return null;
            ushort id = BitConverter.ToUInt16(header, 0x08);
            return id switch
            {
                0xB2B8 => "DXT1",
                0x5D70 => "DXT5",
                _ => null
            };
        }

        // Reads exponent at 0x44, verifies against file size

        // Helper to infer width purely from file length and format
        public static int? InferWidthFromSize(long fileLength, string format)
        {
            const int HeaderSize = 0x800;
            long dataLen = fileLength - HeaderSize;
            if (dataLen <= 0) return null;

            int blockSize = format switch
            {
                "DXT1" => 8,
                "DXT3" => 16,
                "DXT5" => 16,
                _ => 0
            };
            if (blockSize == 0) return null;

            // try exponents 7…12 (128…4096)
            for (int exp = 7; exp <= 12; exp++)
            {
                int dim = 1 << exp;
                int blocks = (dim / 4) * (dim / 4);
                long expected = blocks * blockSize;
                if (expected == dataLen)
                    return dim;
            }
            return null;
        }

        // Revised DetectWidth
        /// <summary>
        /// Reads the size‐exponent from the STF header (offset 0x44, or 0x45 if 0x44 is bogus)
        /// and returns the width (2^exp), or null if no valid exponent is found.
        /// </summary>
        public static int? DetectWidth(byte[] header)
        {
            // Possible exponent offsets: 0x44 (standard), 0x45 (moon.stf variant)
            foreach (int off in new[] { 0x44, 0x45 })
            {
                if (header.Length > off)
                {
                    byte exp = header[off];
                    // only accept reasonable exponents (2^1=2 up to 2^13=8192)
                    if (exp >= 1 && exp <= 13)
                    {
                        return 1 << exp;
                    }
                }
            }

            // nothing valid found
            return null;
        }



        // Builds a 128-byte DDS header for DXT1/3/5
        public static byte[] BuildDdsHeader(int width, string format)
        {
            byte[] h = new byte[128];
            // magic "DDS "
            Array.Copy(new byte[] { (byte)'D', (byte)'D', (byte)'S', (byte)' ' }, 0, h, 0, 4);
            // dwSize = 124
            Array.Copy(BitConverter.GetBytes(124), 0, h, 4, 4);
            // flags = CAPS|HEIGHT|WIDTH|PIXELFORMAT|PITCH
            Array.Copy(BitConverter.GetBytes(0x00021007), 0, h, 8, 4);
            // height & width
            Array.Copy(BitConverter.GetBytes(width), 0, h, 12, 4);
            Array.Copy(BitConverter.GetBytes(width), 0, h, 16, 4);
            // pitchOrLinearSize
            int pitch = (format == "DXT1") ? width / 2 : width;
            Array.Copy(BitConverter.GetBytes(pitch), 0, h, 20, 4);
            // mipMapCount = 0
            Array.Copy(BitConverter.GetBytes(0), 0, h, 28, 4);
            // pixel format size & flags
            Array.Copy(BitConverter.GetBytes(32), 0, h, 76, 4);
            Array.Copy(BitConverter.GetBytes(0x4), 0, h, 80, 4);
            // FourCC
            uint fourCC = format switch
            {
                "DXT1" => 0x31545844,
                "DXT3" => 0x33545844,
                "DXT5" => 0x35545844,
                _ => 0
            };
            Array.Copy(BitConverter.GetBytes(fourCC), 0, h, 84, 4);
            // caps1 = TEXTURE
            Array.Copy(BitConverter.GetBytes(0x1000), 0, h, 108, 4);

            return h;
        }
    }
}
