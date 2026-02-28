using System.Windows;
using System.Windows.Input;
using SpaceCleaning.BaseControl;

namespace SpaceCleaning
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private SystemClean systemCleanControl;
        private AppClean appCleanControl;
        private PrivacyClean privacycleanControl;
        private RegistryClean registryCleanControl;
        private PluginClean pluginCleanControl;

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;

            systemCleanControl = new SystemClean();
            appCleanControl = new AppClean();
            privacycleanControl = new PrivacyClean();
            registryCleanControl = new RegistryClean();
            pluginCleanControl = new PluginClean();

            // 设置默认页面
            this.MainContent.Content = systemCleanControl;
            this.BtnSystemClean.IsSelected = true;

            // 绑定委托事件

            systemCleanControl.ProgressChanged += SetProgressBarValue;
            systemCleanControl.ScanStarted += SetProgressBarVisibility;
            appCleanControl.ProgressChanged += SetProgressBarValue;
            appCleanControl.ScanStarted += SetProgressBarVisibility;
            privacycleanControl.ProgressChanged += SetProgressBarValue;
            privacycleanControl.ScanStarted += SetProgressBarVisibility;
            registryCleanControl.ProgressChanged += SetProgressBarValue;
            registryCleanControl.ScanStarted += SetProgressBarVisibility;
            pluginCleanControl.ProgressChanged += SetProgressBarValue;
            pluginCleanControl.ScanStarted += SetProgressBarVisibility;
        }
        // 弹出使用须知窗口
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 弹出使用须知窗口（模态）
            var notice = new UsageNotice();
            notice.Owner = this;  // 设置主窗口为 Owner，这样弹窗会在主窗上方
            notice.ShowDialog();
        }

        private void SystemCleanClick(object sender, RoutedEventArgs e)
        {
            // 将所有按钮取消选中
            SetAllButtonFalse();
            this.BtnSystemClean.IsSelected = true;

            this.MainContent.Content = systemCleanControl;
        }

        private void AppCleanClick(object sender, RoutedEventArgs e)
        {
            SetAllButtonFalse();
            this.BtnAppClean.IsSelected = true;
            this.MainContent.Content = appCleanControl;
        }
        private void PrivacyCleanClick(object sender, RoutedEventArgs e)
        {
            SetAllButtonFalse();
            this.BtnPrivacyClean.IsSelected = true;
            this.MainContent.Content = privacycleanControl;
        }
        private void RegistryCleanClick(object sender, RoutedEventArgs e)
        {
            SetAllButtonFalse();
            this.BtnRegistryClean.IsSelected = true;
            this.MainContent.Content = registryCleanControl;
        }
        private void PluginCleanClick(object sender, RoutedEventArgs e)
        {
            SetAllButtonFalse();
            this.BtnPluginClean.IsSelected = true;
            this.MainContent.Content = pluginCleanControl;
        }
        private void FileShredClick(object sender, RoutedEventArgs e)
        {
            SetAllButtonFalse();
            this.BtnFileShred.IsSelected = true;
            this.MainContent.Content = new FileShred();
        }
        private void RecyCleBinClick(object sender, RoutedEventArgs e)
        {
            SetAllButtonFalse();
            this.BtnRecycleBin.IsSelected = true;
            this.MainContent.Content = new RecyCleBin();
        }

        private void SetAllButtonFalse()
        {
            foreach (var child in Left_Button.Children)
            {
                if (child is ImageButton btn)
                {
                    btn.IsSelected = false;
                }
            }
        }

        // 设置进度条的值
        public void SetProgressBarValue(int value)
        {
            Dispatcher.Invoke(() =>
            {
                this.ProgressBar.IsIndeterminate = false;
                this.ProgressBar.Value = value;
            });
        }
        // 设置进度条是否可见
        public void SetProgressBarVisibility(bool isVisible)
        {
            Dispatcher.Invoke(() =>
            {
                this.ProgressBar.Visibility = isVisible ? Visibility.Visible : Visibility.Hidden;
                this.ProgressBar.IsIndeterminate = true;
            });
        }
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 当在标题栏区域按下鼠标左键并拖动时，移动窗口
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
        // 最小化窗口
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        // 关闭窗口
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
