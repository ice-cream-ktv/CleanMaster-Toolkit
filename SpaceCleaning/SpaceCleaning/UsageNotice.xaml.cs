using System.Windows;

namespace SpaceCleaning
{
    /// <summary>
    /// UsageNotice.xaml 的交互逻辑
    /// </summary>
    public partial class UsageNotice : Window
    {
        public UsageNotice()
        {
            InitializeComponent();
        }
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
