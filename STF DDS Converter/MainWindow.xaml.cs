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

        // Animate ProgressBar.Value → toValue over durationMs
        private void AnimateProgress(double toValue, int durationMs = 150)
        {
            var anim = new DoubleAnimation
            {
                To = toValue,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            ProgressBar.BeginAnimation(ProgressBar.ValueProperty, anim);
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

        private void SelectStf_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "STF files|*.stf" };
            if (dlg.ShowDialog() != true) return;

            _stfPath = dlg.FileName;
            StfPathText.Text = _stfPath;
            _mode = Mode.StfToDds;

            MessageBlock.Text = "";
            LogBox.Items.Clear();
            AnimateProgress(0);

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

        private void SelectDds_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "DDS files|*.dds" };
            if (dlg.ShowDialog() != true) return;

            _ddsPath = dlg.FileName;
            DdsPathText.Text = _ddsPath;
            _mode = Mode.DdsToStf;

            MessageBlock.Text = "";
            LogBox.Items.Clear();
            AnimateProgress(0);

            Log("Selected DDS → " + _ddsPath);
        }

        private void Convert_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MessageBlock.Text = "";
                Log("Starting conversion...");
                AnimateProgress(0);

                if (_mode == Mode.StfToDds)
                {
                    if (string.IsNullOrEmpty(_stfPath))
                        throw new InvalidOperationException("No STF selected.");
                    if (!int.TryParse(WidthBox.Text, out int width) || width <= 0)
                        throw new InvalidOperationException("Invalid width.");
                    var format = (FormatBox.SelectedItem as ComboBoxItem)?.Content as string;
                    if (string.IsNullOrEmpty(format))
                        throw new InvalidOperationException("Select a compression format.");

                    Log($"Converting STF→DDS: {width}×{width}, {format}");
                    // Step 1: header saved → 10%
                    ConvertStfToDds(_stfPath, width, format);
                }
                else if (_mode == Mode.DdsToStf)
                {
                    if (string.IsNullOrEmpty(_ddsPath))
                        throw new InvalidOperationException("No DDS selected.");

                    Log("Converting DDS→STF");
                    ConvertDdsToStf(_ddsPath);
                }
                else
                {
                    throw new InvalidOperationException("Select a file first.");
                }

                AnimateProgress(100);
                Log("Conversion complete.");
                MessageBlock.Text = "Done.";
            }
            catch (Exception ex)
            {
                MessageBlock.Text = ex.Message;
                Log("Error: " + ex.Message);
            }
        }

        private void ConvertStfToDds(string stfPath, int width, string format)
        {
            string dir = Path.GetDirectoryName(stfPath);
            string name = Path.GetFileNameWithoutExtension(stfPath);
            string hdrFile = Path.Combine(dir, name + ".header");
            string ddsFile = Path.Combine(dir, name + ".dds");

            // 1) Save header (10%)
            Log("Saving .header");
            using (var inFs = new FileStream(stfPath, FileMode.Open, FileAccess.Read))
            using (var hdrFs = new FileStream(hdrFile, FileMode.Create, FileAccess.Write))
            {
                var buf = new byte[HeaderSize];
                inFs.Read(buf, 0, HeaderSize);
                hdrFs.Write(buf, 0, HeaderSize);
            }
            Dispatcher.Invoke(() => AnimateProgress(10));

            // 2) Build DDS header (30%)
            Log("Building DDS header");
            var ddsHeader = BuildDdsHeader(width, format);
            int padLen = HeaderSize - ddsHeader.Length;
            Dispatcher.Invoke(() => AnimateProgress(30));

            // 3) Write DDS with chunked copy loop (30→100%)
            Log("Writing .dds file");
            using var outFs = new FileStream(ddsFile, FileMode.Create, FileAccess.Write);

            outFs.Write(ddsHeader, 0, ddsHeader.Length);
            outFs.Write(new byte[padLen], 0, padLen);

            using var inFs2 = new FileStream(stfPath, FileMode.Open, FileAccess.Read);
            inFs2.Seek(HeaderSize, SeekOrigin.Begin);

            long totalBytes = inFs2.Length - HeaderSize;
            long copied = 0;
            int lastPct = 30;
            var buffer = new byte[81920];

            int bytesRead;
            while ((bytesRead = inFs2.Read(buffer, 0, buffer.Length)) > 0)
            {
                outFs.Write(buffer, 0, bytesRead);
                copied += bytesRead;

                // calculate percent from 30→100
                int pct = 30 + (int)(70 * (copied / (double)totalBytes));

                // only animate on change
                if (pct != lastPct)
                {
                    lastPct = pct;
                    Dispatcher.Invoke(() => AnimateProgress(pct));
                }
            }

            Log("Wrote: " + ddsFile);
        }

        private void ConvertDdsToStf(string ddsPath)
        {
            string dir = Path.GetDirectoryName(ddsPath);
            string name = Path.GetFileNameWithoutExtension(ddsPath);
            string stfFile = Path.Combine(dir, name + ".stf");
            string hdrFile = Path.Combine(dir, name + ".header");

            // find header
            if (!File.Exists(hdrFile))
            {
                Log(".header missing, prompt user");
                var dlg = new OpenFileDialog { Filter = "Header files|*.header" };
                if (dlg.ShowDialog() != true)
                    throw new InvalidOperationException("Header not selected.");
                hdrFile = dlg.FileName;
            }
            Dispatcher.Invoke(() => AnimateProgress(30));

            // rebuild STF (30→100)
            Log("Rebuilding .stf");
            using var outFs = new FileStream(stfFile, FileMode.Create, FileAccess.Write);
            using var hdrFs = new FileStream(hdrFile, FileMode.Open, FileAccess.Read);
            hdrFs.CopyTo(outFs);
            Dispatcher.Invoke(() => AnimateProgress(60));

            using var inFs = new FileStream(ddsPath, FileMode.Open, FileAccess.Read);
            inFs.Seek(HeaderSize, SeekOrigin.Begin);
            inFs.CopyTo(outFs);
            Dispatcher.Invoke(() => AnimateProgress(100));

            Log("Wrote: " + stfFile);
        }

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
