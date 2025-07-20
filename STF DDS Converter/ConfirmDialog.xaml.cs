using System.Windows;

namespace STF_DDS_Converter
{
    public partial class ConfirmDialog : Window
    {
        public ConfirmDialog(string title, string message)
        {
            InitializeComponent();
            TitleText.Text = title;
            MessageText.Text = message;
        }

        private void Yes_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void No_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
