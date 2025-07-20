using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace STF_DDS_Converter
{
    public partial class MainWindow : Window
    {
        private const int HeaderSize = 0x800;

        private enum Mode { None, StfToDds, DdsToStf }
        private Mode _mode = Mode.None;

        private string _stfPath;
        private string _ddsPath;

        public MainWindow()
        {
            InitializeComponent();
            FormatBox.SelectedIndex = 0; // default to DXT1
        }

        // Append a line to the log
        private void Log(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                LogBox.Items.Add(msg);
                LogBox.ScrollIntoView(LogBox.Items[^1]);
            });
        }

        // STF file picker + auto-detect
        private void SelectStf_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "STF files|*.stf" };
            if (dlg.ShowDialog() != true) return;

            _stfPath = dlg.FileName;
            StfPathText.Text = _stfPath;
            _mode = Mode.StfToDds;

            MessageBlock.Text = "";
            LogBox.Items.Clear();
            ProgressBar.Value = 0;

            // read header
            var header = new byte[HeaderSize];
            using var fs = new FileStream(_stfPath, FileMode.Open, FileAccess.Read);
            fs.Read(header, 0, HeaderSize);

            // detect
            var fmt = DetectCompression(header) ?? "";
            var w = DetectWidth(header);

            FormatBox.Text = fmt;
            WidthBox.Text = w?.ToString() ?? "";

            Log($"Selected STF → width={WidthBox.Text}, format={FormatBox.Text}");
        }

        // DDS file picker
        private void SelectDds_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "DDS files|*.dds" };
            if (dlg.ShowDialog() != true) return;

            _ddsPath = dlg.FileName;
            DdsPathText.Text = _ddsPath;
            _mode = Mode.DdsToStf;

            MessageBlock.Text = "";
            LogBox.Items.Clear();
            ProgressBar.Value = 0;

            Log("Selected DDS → " + _ddsPath);
        }

        // Convert button click
        private void Convert_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MessageBlock.Text = "";
                Log("Starting conversion...");
                ProgressBar.Value = 0;

                if (_mode == Mode.StfToDds)
                {
                    if (string.IsNullOrEmpty(_stfPath))
                        throw new InvalidOperationException("No STF selected.");

                    if (!int.TryParse(WidthBox.Text, out int width) || width <= 0)
                        throw new InvalidOperationException("Invalid width.");

                    var format = (FormatBox.SelectedItem as ComboBoxItem)?.Content as string;
                    if (string.IsNullOrEmpty(format))
                        throw new InvalidOperationException("Select a compression format.");

                    ProgressBar.Value = 10;
                    Log($"Converting STF→DDS: {width}×{width}, {format}");
                    ConvertStfToDds(_stfPath, width, format);
                }
                else if (_mode == Mode.DdsToStf)
                {
                    if (string.IsNullOrEmpty(_ddsPath))
                        throw new InvalidOperationException("No DDS selected.");

                    ProgressBar.Value = 10;
                    Log("Converting DDS→STF");
                    ConvertDdsToStf(_ddsPath);
                }
                else
                {
                    throw new InvalidOperationException("Select a file first.");
                }

                ProgressBar.Value = 100;
                Log("Conversion complete.");
                MessageBlock.Text = "Done.";
            }
            catch (Exception ex)
            {
                MessageBlock.Text = ex.Message;
                Log("Error: " + ex.Message);
            }
        }

        // STF→DDS implementation
        private void ConvertStfToDds(string stfPath, int width, string format)
        {
            string dir = Path.GetDirectoryName(stfPath);
            string name = Path.GetFileNameWithoutExtension(stfPath);
            string hdrFile = Path.Combine(dir, name + ".header");
            string ddsFile = Path.Combine(dir, name + ".dds");

            Log("Saving .header");
            using (var inFs = new FileStream(stfPath, FileMode.Open, FileAccess.Read))
            using (var hdrFs = new FileStream(hdrFile, FileMode.Create, FileAccess.Write))
            {
                var buf = new byte[HeaderSize];
                inFs.Read(buf, 0, HeaderSize);
                hdrFs.Write(buf, 0, HeaderSize);
            }
            ProgressBar.Value = 30;

            Log("Building DDS header");
            var ddsHeader = BuildDdsHeader(width, format);
            int padLen = HeaderSize - ddsHeader.Length;
            ProgressBar.Value = 60;

            Log("Writing .dds file");
            using (var outFs = new FileStream(ddsFile, FileMode.Create, FileAccess.Write))
            {
                outFs.Write(ddsHeader, 0, ddsHeader.Length);
                outFs.Write(new byte[padLen], 0, padLen);
                using var inFs = new FileStream(stfPath, FileMode.Open, FileAccess.Read);
                inFs.Seek(HeaderSize, SeekOrigin.Begin);
                inFs.CopyTo(outFs);
            }
            Log("Wrote: " + ddsFile);
        }

        // DDS→STF implementation
        private void ConvertDdsToStf(string ddsPath)
        {
            string dir = Path.GetDirectoryName(ddsPath);
            string name = Path.GetFileNameWithoutExtension(ddsPath);
            string stfFile = Path.Combine(dir, name + ".stf");
            string hdrFile = Path.Combine(dir, name + ".header");

            if (!File.Exists(hdrFile))
            {
                Log(".header missing, prompt user");
                var dlg = new OpenFileDialog { Filter = "Header files|*.header" };
                if (dlg.ShowDialog() != true)
                    throw new InvalidOperationException("Header not selected.");
                hdrFile = dlg.FileName;
            }
            ProgressBar.Value = 50;

            Log("Rebuilding .stf");
            using (var outFs = new FileStream(stfFile, FileMode.Create, FileAccess.Write))
            {
                using var hdrFs = new FileStream(hdrFile, FileMode.Open, FileAccess.Read);
                hdrFs.CopyTo(outFs);

                using var inFs = new FileStream(ddsPath, FileMode.Open, FileAccess.Read);
                inFs.Seek(HeaderSize, SeekOrigin.Begin);
                inFs.CopyTo(outFs);
            }
            Log("Wrote: " + stfFile);
        }

        // Helper: detect DXT1/DXT5 tag at 0x08
        public static string DetectCompression(byte[] header)
        {
            if (header.Length < 10) return null;
            ushort code = BitConverter.ToUInt16(header, 0x08);
            return code switch
            {
                0xB2B8 => "DXT1",
                0x5D70 => "DXT5",
                _ => null
            };
        }

        // Helper: read exponent at 0x44 or 0x45
        public static int? DetectWidth(byte[] header)
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

        // Helper: build 128-byte DDS header
        public static byte[] BuildDdsHeader(int width, string format)
        {
            var h = new byte[128];
            Array.Copy(new byte[] { (byte)'D', (byte)'D', (byte)'S', (byte)' ' }, 0, h, 0, 4);
            Array.Copy(BitConverter.GetBytes(124), 0, h, 4, 4);
            Array.Copy(BitConverter.GetBytes(0x00021007), 0, h, 8, 4);
            Array.Copy(BitConverter.GetBytes(width), 0, h, 12, 4);
            Array.Copy(BitConverter.GetBytes(width), 0, h, 16, 4);
            int pitch = format == "DXT1" ? width / 2 : width;
            Array.Copy(BitConverter.GetBytes(pitch), 0, h, 20, 4);
            Array.Copy(BitConverter.GetBytes(0), 0, h, 28, 4);
            Array.Copy(BitConverter.GetBytes(32), 0, h, 76, 4);
            Array.Copy(BitConverter.GetBytes(0x4), 0, h, 80, 4);

            uint fourCC = format switch
            {
                "DXT1" => 0x31545844,
                "DXT3" => 0x33545844,
                "DXT5" => 0x35545844,
                _ => 0
            };
            Array.Copy(BitConverter.GetBytes(fourCC), 0, h, 84, 4);
            Array.Copy(BitConverter.GetBytes(0x1000), 0, h, 108, 4);
            return h;
        }
    }
}
