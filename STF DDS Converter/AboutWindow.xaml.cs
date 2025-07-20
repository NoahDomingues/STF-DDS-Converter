// AboutWindow.xaml.cs
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace STF_DDS_Converter
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
        }

        private void Logo_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl("https://github.com/NoahDomingues/STF-DDS-Converter");
        }

        private void DiscordIcon_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl("https://discord.gg/Z88NnTgpWU");
        }

        private void YouTubeIcon_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl("https://www.youtube.com/@NoahDomingues?sub_confirmation=1");
        }

        private void GitHubIcon_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl("https://github.com/NoahDomingues/STF-DDS-Converter");
        }

        private void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
    }
}
