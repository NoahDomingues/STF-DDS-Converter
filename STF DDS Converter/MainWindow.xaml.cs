using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;

namespace STF_DDS_Converter
{
    public partial class MainWindow : Window
    {
        private const int HeaderSize = 0x800;

        private enum Mode { None, StfToDds, DdsToStf }
        private Mode _mode = Mode.None;

        // full, un-trimmed paths
        private string _stfPath;
        private string _ddsPath;

        public MainWindow()
        {
            InitializeComponent();
            FormatBox.SelectedIndex = 0;

            // placeholders
            StfPathText.Text = "No file selected";
            DdsPathText.Text = "No file selected";

            // re-trim on resize
            StfPathText.SizeChanged += PathText_SizeChanged;
            DdsPathText.SizeChanged += PathText_SizeChanged;
        }

        private void PathText_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender == StfPathText)
                UpdateFilePathDisplay(_stfPath, StfPathText);
            else if (sender == DdsPathText)
                UpdateFilePathDisplay(_ddsPath, DdsPathText);
        }

        private void UpdateFilePathDisplay(string fullPath, TextBlock target)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                target.Text = "No file selected";
                return;
            }

            // measure full text width
            var ft = new FormattedText(
                fullPath,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(target.FontFamily, target.FontStyle, target.FontWeight, target.FontStretch),
                target.FontSize,
                Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            if (ft.Width <= target.ActualWidth)
            {
                target.Text = fullPath;
                return;
            }

            // compute chars we can show
            double avgW = ft.Width / fullPath.Length;
            int maxChars = Math.Max(4, (int)(target.ActualWidth / avgW));
            int keep = Math.Max(1, (maxChars - 3) / 2);

            string start = fullPath.Substring(0, keep);
            string end = fullPath.Substring(fullPath.Length - keep);
            target.Text = $"{start}…{end}";
        }

        /// <summary>
        /// Animate ProgressBar.Value → toValue over durationMs ms.
        /// </summary>
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

        /// <summary>
        /// Append a line to the log and scroll into view.
        /// </summary>
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
            // clear any previous DDS selection
            _ddsPath = null;
            UpdateFilePathDisplay(null, DdsPathText);

            var dlg = new OpenFileDialog { Filter = "STF files|*.stf" };
            if (dlg.ShowDialog() != true) return;

            _stfPath = dlg.FileName;
            _mode = Mode.StfToDds;

            UpdateFilePathDisplay(_stfPath, StfPathText);
            MessageBlock.Text = "";
            LogBox.Items.Clear();
            AnimateProgress(0);
            ProgressBar.Foreground = (Brush)FindResource("AccentBrush");

            // detect compression & width
            var header = new byte[HeaderSize];
            using var fs = new FileStream(_stfPath, FileMode.Open, FileAccess.Read);
            fs.Read(header, 0, HeaderSize);

            FormatBox.Text = DetectCompression(header) ?? "";
            WidthBox.Text = DetectWidth(header)?.ToString() ?? "";

            Log($"Selected STF → width={WidthBox.Text}, format={FormatBox.Text}");
        }

        private void SelectDds_Click(object sender, RoutedEventArgs e)
        {
            // clear any previous STF selection
            _stfPath = null;
            UpdateFilePathDisplay(null, StfPathText);
            WidthBox.Text = "";
            FormatBox.SelectedIndex = 0;

            var dlg = new OpenFileDialog { Filter = "DDS files|*.dds" };
            if (dlg.ShowDialog() != true) return;

            _ddsPath = dlg.FileName;
            _mode = Mode.DdsToStf;

            UpdateFilePathDisplay(_ddsPath, DdsPathText);
            MessageBlock.Text = "";
            LogBox.Items.Clear();
            AnimateProgress(0);
            ProgressBar.Foreground = (Brush)FindResource("AccentBrush");

            Log("Selected DDS → " + _ddsPath);
        }

        private async void Convert_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MessageBlock.Text = "";
                Log("Starting conversion...");
                AnimateProgress(0);
                ProgressBar.Foreground = (Brush)FindResource("AccentBrush");

                // build target path & validate
                string targetPath;
                if (_mode == Mode.StfToDds)
                {
                    if (string.IsNullOrEmpty(_stfPath))
                        throw new InvalidOperationException("No STF selected.");
                    if (!int.TryParse(WidthBox.Text, out var w) || w <= 0)
                        throw new InvalidOperationException("Invalid width.");
                    if (!(FormatBox.SelectedItem is ComboBoxItem cb) ||
                        string.IsNullOrEmpty(cb.Content as string))
                        throw new InvalidOperationException("Select a compression format.");

                    var dir = Path.GetDirectoryName(_stfPath);
                    var name = Path.GetFileNameWithoutExtension(_stfPath);
                    targetPath = Path.Combine(dir!, name + ".dds");
                }
                else if (_mode == Mode.DdsToStf)
                {
                    if (string.IsNullOrEmpty(_ddsPath))
                        throw new InvalidOperationException("No DDS selected.");

                    var dir = Path.GetDirectoryName(_ddsPath);
                    var name = Path.GetFileNameWithoutExtension(_ddsPath);
                    targetPath = Path.Combine(dir!, name + ".stf");
                }
                else
                {
                    throw new InvalidOperationException("Select a file first.");
                }

                // prompt overwrite on UI thread
                if (File.Exists(targetPath))
                {
                    var confirm = new ConfirmDialog(
                        "Confirm Overwrite",
                        $"Output file already exists:\n{targetPath}\n\nOverwrite?")
                    {
                        Owner = this
                    };
                    if (confirm.ShowDialog() != true)
                    {
                        ProgressBar.Foreground = Brushes.Red;
                        AnimateProgress(100, 200);
                        MessageBlock.Text = "Operation cancelled.";
                        MessageBlock.Foreground = Brushes.Red;
                        Log("Conversion cancelled by user.");
                        return;
                    }
                }

                // run conversion off UI thread
                if (_mode == Mode.StfToDds)
                {
                    int width = int.Parse(WidthBox.Text);
                    string fmt = (FormatBox.SelectedItem as ComboBoxItem)!.Content as string;
                    Log($"Converting STF→DDS: {width}×{width}, {fmt}");
                    await Task.Run(() => ConvertStfToDds(_stfPath, width, fmt));
                }
                else
                {
                    Log("Converting DDS→STF");
                    await Task.Run(() => ConvertDdsToStf(_ddsPath));
                }

                AnimateProgress(100);
                Log("Conversion complete.");
                MessageBlock.Text = "Done.";
                MessageBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x00));
            }
            catch (Exception ex)
            {
                var dlg = new ErrorDialog("Error", ex.Message) { Owner = this };
                dlg.ShowDialog();
                // update main‐window status
                MessageBlock.Text = ex.Message;
                MessageBlock.Foreground = Brushes.Red;
                Log("Error: " + ex.Message);
            }
        }

        private void ConvertStfToDds(string stfPath, int width, string format)
        {
            // header → 10%
            Log("Saving .header");
            string dir = Path.GetDirectoryName(stfPath)!;
            string name = Path.GetFileNameWithoutExtension(stfPath);
            string hdrFile = Path.Combine(dir, name + ".header");
            using (var inFs = new FileStream(stfPath, FileMode.Open, FileAccess.Read))
            using (var hdrFs = new FileStream(hdrFile, FileMode.Create, FileAccess.Write))
            {
                var buf = new byte[HeaderSize];
                inFs.Read(buf, 0, HeaderSize);
                hdrFs.Write(buf, 0, HeaderSize);
            }
            Dispatcher.Invoke(() => AnimateProgress(10));

            // build DDS header → 30%
            Log("Building DDS header");
            var ddsHeader = BuildDdsHeader(width, format);
            int padLen = HeaderSize - ddsHeader.Length;
            Dispatcher.Invoke(() => AnimateProgress(30));

            // chunked copy → 30–100%
            Log("Writing .dds file");
            string ddsFile = Path.Combine(Path.GetDirectoryName(stfPath)!, name + ".dds");
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
                int pct = 30 + (int)(70 * (copied / (double)totalBytes));
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
            // ensure header → 30%
            string dir = Path.GetDirectoryName(ddsPath)!;
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
            Dispatcher.Invoke(() => AnimateProgress(30));

            // copy header → 60%
            Log("Rebuilding .stf");
            using (var outFs = new FileStream(stfFile, FileMode.Create, FileAccess.Write))
            using (var hdrFs = new FileStream(hdrFile, FileMode.Open, FileAccess.Read))
            {
                hdrFs.CopyTo(outFs);
            }
            Dispatcher.Invoke(() => AnimateProgress(60));

            // append DDS data → 100%
            using (var inFs = new FileStream(ddsPath, FileMode.Open, FileAccess.Read))
            using (var outFs2 = new FileStream(stfFile, FileMode.Append, FileAccess.Write))
            {
                inFs.Seek(HeaderSize, SeekOrigin.Begin);
                inFs.CopyTo(outFs2);
            }
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
                "DXT1" => 0x31545844u,
                "DXT3" => 0x33545844u,
                "DXT5" => 0x35545844u,
                _ => 0u
            };
            Array.Copy(BitConverter.GetBytes(fourCC), 0, h, 84, 4);
            Array.Copy(BitConverter.GetBytes(0x1000), 0, h, 108, 4);
            return h;
        }

        // Link click handlers
        private void AboutLink_Click(object sender, RoutedEventArgs e)
        {
            var about = new AboutWindow
            {
                Owner = this
            };
            about.ShowDialog();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            e.Handled = true;
        }

    }
}
