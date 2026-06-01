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
        private enum Mode { None, StfToDds, DdsToStf }
        private Mode _mode = Mode.None;

        private string _stfPath;
        private string _ddsPath;

        public MainWindow()
        {
            InitializeComponent();
            FormatBox.SelectedIndex = 0;

            StfPathText.Text = "No file selected";
            DdsPathText.Text = "No file selected";

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

            double avgW = ft.Width / fullPath.Length;
            int maxChars = Math.Max(4, (int)(target.ActualWidth / avgW));
            int keep = Math.Max(1, (maxChars - 3) / 2);

            string start = fullPath.Substring(0, keep);
            string end = fullPath.Substring(fullPath.Length - keep);
            target.Text = $"{start}…{end}";
        }

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

        private void Log(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                LogBox.Items.Add(msg);
                LogBox.ScrollIntoView(LogBox.Items[^1]);
            });
        }

        private static string SidecarPathFor(string basePath)
            => Path.ChangeExtension(basePath, ".stfmips.json");

        private static string LegacyHeaderPathFor(string basePath)
            => Path.Combine(Path.GetDirectoryName(basePath)!, Path.GetFileNameWithoutExtension(basePath) + ".header");

        private void SelectStf_Click(object sender, RoutedEventArgs e)
        {
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

            try
            {
                var tex = TxpcTexture.ParseFromStf(_stfPath);
                FormatBox.Text = tex.Format;
                WidthBox.Text = tex.DdsWidth.ToString();

                Log($"Selected STF: {Path.GetFileName(_stfPath)}");
                Log($"  TXPC header width (engine): {tex.HeaderWidth}");
                Log($"  DDS export size: {tex.DdsWidth}×{tex.DdsHeight}, {tex.Format}");
                Log($"  Mip levels in file: {tex.Segments.Count}");
                Log($"  Pre-mip bytes: {tex.PreMipBytes.Length} (saved to .header)");
            }
            catch (Exception ex)
            {
                FormatBox.Text = TxpcTexture.DetectCompression(File.ReadAllBytes(_stfPath)) ?? "";
                WidthBox.Text = "";
                Log("Parse warning: " + ex.Message);
            }
        }

        private void SelectDds_Click(object sender, RoutedEventArgs e)
        {
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

            string sidecar = SidecarPathFor(_ddsPath);
            string header = LegacyHeaderPathFor(_ddsPath);
            Log("Selected DDS → " + _ddsPath);
            if (File.Exists(sidecar))
                Log("  Found sidecar: " + Path.GetFileName(sidecar));
            else if (File.Exists(header))
                Log("  Found header: " + Path.GetFileName(header));
            else
                Log("  Need .stfmips.json + .header from STF→DDS export (same folder).");
        }

        private async void Convert_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MessageBlock.Text = "";
                Log("Starting conversion...");
                AnimateProgress(0);
                ProgressBar.Foreground = (Brush)FindResource("AccentBrush");

                string targetPath;
                if (_mode == Mode.StfToDds)
                {
                    if (string.IsNullOrEmpty(_stfPath))
                        throw new InvalidOperationException("No STF selected.");

                    var dir = Path.GetDirectoryName(_stfPath)!;
                    var name = Path.GetFileNameWithoutExtension(_stfPath);
                    targetPath = Path.Combine(dir, name + ".dds");
                }
                else if (_mode == Mode.DdsToStf)
                {
                    if (string.IsNullOrEmpty(_ddsPath))
                        throw new InvalidOperationException("No DDS selected.");

                    var dir = Path.GetDirectoryName(_ddsPath)!;
                    var name = Path.GetFileNameWithoutExtension(_ddsPath);
                    targetPath = Path.Combine(dir, name + ".stf");
                }
                else
                {
                    throw new InvalidOperationException("Select a file first.");
                }

                if (File.Exists(targetPath))
                {
                    var confirm = new ConfirmDialog(
                        "Confirm Overwrite",
                        $"Output file already exists:\n{targetPath}\n\nOverwrite?")
                    { Owner = this };
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

                if (_mode == Mode.StfToDds)
                    await Task.Run(() => ConvertStfToDds(_stfPath));
                else
                    await Task.Run(() => ConvertDdsToStf(_ddsPath));

                AnimateProgress(100);
                Log("Conversion complete.");
                MessageBlock.Text = "Done.";
                MessageBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x00));
            }
            catch (Exception ex)
            {
                var dlg = new ErrorDialog("Error", ex.Message) { Owner = this };
                dlg.ShowDialog();
                MessageBlock.Text = ex.Message;
                MessageBlock.Foreground = Brushes.Red;
                Log("Error: " + ex.Message);
            }
        }

        private void ConvertStfToDds(string stfPath)
        {
            Log("Parsing TXPC...");
            var tex = TxpcTexture.ParseFromStf(stfPath);
            byte[] stfData = File.ReadAllBytes(stfPath);

            string dir = Path.GetDirectoryName(stfPath)!;
            string name = Path.GetFileNameWithoutExtension(stfPath);
            string hdrFile = Path.Combine(dir, name + ".header");
            string sidecarFile = SidecarPathFor(stfPath);
            string ddsFile = Path.Combine(dir, name + ".dds");

            Dispatcher.Invoke(() => AnimateProgress(15));

            Log($"Saving pre-mip header ({tex.PreMipBytes.Length} bytes) → .header");
            File.WriteAllBytes(hdrFile, tex.PreMipBytes);

            Log("Saving mip layout → .stfmips.json");
            TxpcTexture.SaveSidecar(tex.ToSidecar(), sidecarFile);

            Dispatcher.Invoke(() => AnimateProgress(35));

            Log("Extracting DXT mip chain (strips OFDR per-mip prefix when present)...");
            byte[] linearDxt = tex.BuildLinearDxt(stfData);

            Log($"Building DDS {tex.DdsWidth}×{tex.DdsHeight} {tex.Format}, payload {linearDxt.Length} bytes");
            byte[] dds = TxpcTexture.BuildDdsFile(linearDxt, tex.DdsWidth, tex.DdsHeight, tex.Format);
            File.WriteAllBytes(ddsFile, dds);

            Dispatcher.Invoke(() => AnimateProgress(100));
            Log("Wrote: " + ddsFile);
            Log("Keep .header and .stfmips.json beside the DDS for import.");
        }

        private void ConvertDdsToStf(string ddsPath)
        {
            string dir = Path.GetDirectoryName(ddsPath)!;
            string name = Path.GetFileNameWithoutExtension(ddsPath);
            string stfFile = Path.Combine(dir, name + ".stf");
            string hdrFile = Path.Combine(dir, name + ".header");
            string sidecarFile = SidecarPathFor(Path.Combine(dir, name + ".stf"));

            if (!File.Exists(sidecarFile))
                sidecarFile = SidecarPathFor(ddsPath);

            if (!File.Exists(hdrFile))
            {
                var dlg = new OpenFileDialog { Filter = "Header files|*.header" };
                if (dlg.ShowDialog() != true)
                    throw new InvalidOperationException("Pre-mip .header not found (export from STF first).");
                hdrFile = dlg.FileName;
            }

            if (!File.Exists(sidecarFile))
                throw new InvalidOperationException(
                    "Missing .stfmips.json sidecar. Re-export from STF with this converter version.");

            Dispatcher.Invoke(() => AnimateProgress(20));

            byte[] preMip = File.ReadAllBytes(hdrFile);
            if (preMip.Length == 0x800)
            {
                Log("WARNING: .header is 2048 bytes (old converter). Re-export STF→DDS with this build.");
            }

            var sidecar = TxpcTexture.LoadSidecar(sidecarFile);
            var tex = TxpcTexture.FromSidecar(sidecar, preMip);

            Log($"Importing {tex.DdsWidth}×{tex.DdsHeight} {tex.Format}, {tex.Segments.Count} mip segments");

            Dispatcher.Invoke(() => AnimateProgress(45));

            byte[] linearDxt = TxpcTexture.ReadLinearDxtFromDds(ddsPath);
            byte[] mipBlob = tex.BuildMipBlobFromLinearDxt(linearDxt);

            Dispatcher.Invoke(() => AnimateProgress(75));

            Log("Writing .stf");
            using (var outFs = new FileStream(stfFile, FileMode.Create, FileAccess.Write))
            {
                outFs.Write(preMip, 0, preMip.Length);
                outFs.Write(mipBlob, 0, mipBlob.Length);
            }

            long expected = preMip.Length + mipBlob.Length;
            Log($"STF size: {expected} bytes (pre-mip {preMip.Length} + mips {mipBlob.Length})");

            Dispatcher.Invoke(() => AnimateProgress(100));
            Log("Wrote: " + stfFile);
        }

        private void AboutLink_Click(object sender, RoutedEventArgs e)
        {
            var about = new AboutWindow { Owner = this };
            about.ShowDialog();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
