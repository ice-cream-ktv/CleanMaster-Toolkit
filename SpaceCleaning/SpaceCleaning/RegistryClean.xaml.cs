using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SpaceCleaning
{
    /// <summary>
    /// RegistryClean.xaml 的交互逻辑
    /// </summary>
    public partial class RegistryClean : UserControl
    {
        public ObservableCollection<JunkFile> ScannedFiles { get; set; } = new ObservableCollection<JunkFile>();
        public RegistryClean()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            if (!IsRunningAsAdmin())
            {
                MessageBox.Show("需要管理员权限才能扫描和清理注册表！", "权限错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            switch (currentState)
            {
                case ScanState.ReadyToScan:
                    cancelRequested = false;
                    currentState = ScanState.Scanning;
                    ScanButton.Content = "取消扫描";
                    ScannedFiles.Clear();

                    ScanStarted?.Invoke(true);

                    await Task.Run(() => ScanRegistry());
                    // 不管是完成还是停止，都到清理状态       

                    if (currentState != ScanState.ReadyToScan)
                    {
                        currentState = ScanState.ReadyToClean;
                        ScanButton.Content = "一键清理";
                        SkipCleanTextBlock.Visibility = Visibility.Visible;
                    }

                    ProgressChanged?.Invoke(100);

                    await Task.Delay(1000);
                    ScanStarted?.Invoke(false);
                    break;

                case ScanState.Scanning:
                    cancelRequested = true;
                    break;

                case ScanState.ReadyToClean:
                    var result = MessageBox.Show("此操作只会将文件放到垃圾箱中，可恢复", "确认清理", MessageBoxButton.OKCancel, MessageBoxImage.Information);
                    if (result == MessageBoxResult.OK)
                    {
                        CleanFiles(); // 执行清理
                        MessageBox.Show("清理完成！");
                        ScannedFiles.Clear();
                        ScanButton.Content = "开始扫描";
                        currentState = ScanState.ReadyToScan;
                    }
                    break;
            }
        }

        private void SkipClean_Click(object sender, RoutedEventArgs e)
        {
            ScanButton.Content = "开始扫描";
            currentState = ScanState.ReadyToScan;
            SkipCleanTextBlock.Visibility = Visibility.Hidden;
            ScannedFiles.Clear();
        }

        private void ScanRegistry()
        {
            var registryRoots = new[]
            {
                // 只扫描易残留无效项的特定路径
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", false),
                Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", false),
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", false),
                Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", false) //系统服务、驱动程序的配置
            };

            foreach (var root in registryRoots)
            {
                if (root == null) continue;
                try
                {
                    ScanSubKeys(root);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"扫描注册表 {root.Name} 出错：{ex.Message}");
                }
                finally
                {
                    root.Close();
                }
            }

            Dispatcher.Invoke(() =>
            {
                System.Diagnostics.Debug.WriteLine($"[扫描完成] 总项数: {ScannedFiles.Count}");
                foreach (var item in ScannedFiles)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {item.FilePath}");
                }
                if (!ScannedFiles.Any())
                {
                    currentState = ScanState.ReadyToScan;
                    ScanButton.Content = "开始扫描";
                    MessageBox.Show("没有找到无效的注册表项！");
                }
            });
        }

        // 并发运行提升效率
        private void ScanSubKeys(RegistryKey key)
        {
            if (cancelRequested) return;

            string[] subKeyNames;
            try
            {
                subKeyNames = key.GetSubKeyNames();
            }
            catch
            {
                return;
            }
            var localResults = new List<JunkFile>(); // 临时结果列表，线程安全
            // 动态分区，线程池调度
            Parallel.ForEach(subKeyNames, (subKeyName, state) =>
            {
                if (cancelRequested)
                {
                    state.Stop();
                    return;
                }
                try
                {
                    using (var subKey = key.OpenSubKey(subKeyName, false))
                    {
                        if (subKey == null) return;
                        // 判断无效项
                        if (GetInvalidRegistryReason(subKey))
                        {
                            string type, details;

                            if (subKey.ValueCount == 0 && subKey.SubKeyCount == 0)
                            {
                                type = "空键";
                                details = "该项无值无子项，可能是残留";
                            }
                            else if (subKey.Name.IndexOf("Uninstall", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                type = "残留卸载项";
                                details = "缺少DisplayName或UninstallString";
                            }
                            else
                            {
                                type = "无效键";
                                details = "判定为无效项";
                            }
                            var item = new JunkFile
                            {
                                FilePath = $@"{key.Name}\{subKeyName}",
                                FileType = type,
                                FileDetails = details
                            };
                            lock (localResults) // 多线程写入局部列表需加锁
                            {
                                localResults.Add(item);
                            }
                        }
                        // 递归调用下一层子键
                        ScanSubKeys(subKey);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[处理注册表失败] {key.Name}\\{subKeyName}：{ex.Message}");
                }
            });
            if (localResults.Count > 0)
            {
                Dispatcher.Invoke(() =>
                {
                    foreach (var item in localResults)
                    {
                        ScannedFiles.Add(item);
                    }
                });
            }
        }

        private bool GetInvalidRegistryReason(RegistryKey key)
        {
            // 1. 空键（无值无子项）视为无效项
            if (key.ValueCount == 0 && key.SubKeyCount == 0)
                return true;

            // 2. 仅针对卸载路径的卸载项判定残留
            if (key.Name.IndexOf("Uninstall", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var displayName = key.GetValue("DisplayName") as string;
                var uninstallString = key.GetValue("UninstallString") as string;

                // 缺少DisplayName 或 卸载的命令行路径，认为是残留无效项
                if (string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(uninstallString))
                    return true;
            }
            return false;
        }

        private bool CheckServiceImagePath(RegistryKey key)
        {
            var imagePath = key.GetValue("ImagePath") as string;
            if (string.IsNullOrEmpty(imagePath)) return true;

            try
            {
                var fullPath = Environment.ExpandEnvironmentVariables(imagePath);
                return !File.Exists(fullPath);
            }
            catch (SecurityException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[权限错误] 无法访问 {key.Name} 的 ImagePath：{ex.Message}");
                return false; // 权限问题不视为无效
            }
            catch
            {
                return true; // 路径无效视为无效项
            }
        }
        private void CleanFiles()
        {
            string recyclePath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SpaceCleaner", "RecycleBin");

            if (!Directory.Exists(recyclePath))
                Directory.CreateDirectory(recyclePath);

            foreach (var file in ScannedFiles)
            {
                try
                {
                    string fileName = System.IO.Path.GetFileName(file.FilePath);
                    string destPath = System.IO.Path.Combine(recyclePath, Guid.NewGuid().ToString() + "_" + fileName);

                    File.Move(file.FilePath, destPath);
                    File.WriteAllText(destPath + ".meta", file.FilePath);
                    file.MovedPath = destPath;
                }
                catch (IOException ioEx) when ((ioEx.HResult & 0x0000FFFF) == 32) // 32 = ERROR_SHARING_VIOLATION
                {
                    System.Diagnostics.Debug.WriteLine($"文件被占用，跳过：{file.FilePath}");
                    // 可以选择记录下来，之后告知用户
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"移动失败：{file.FilePath}，错误：{ex.Message}");
                }
            }
            System.Diagnostics.Debug.WriteLine($"回收目录路径：{recyclePath}");
            MessageBox.Show($"回收目录路径：{recyclePath}");
        }
        // 检查是否以管理员权限运行
        private bool IsRunningAsAdmin()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
        public class JunkFile
        {
            public string FilePath { get; set; } // 文件原始
            public string FileType { get; set; }
            public string FileDetails { get; set; }
            public string MovedPath { get; set; }
        }

        private enum ScanState
        {
            ReadyToScan,
            Scanning,
            ReadyToClean
        }
        public class InvalidRegistryResult
        {
            public bool IsInvalid { get; set; }
            public string Type { get; set; }
            public string Details { get; set; }
        }

        private ScanState currentState = ScanState.ReadyToScan;
        private bool cancelRequested = false;
        public event Action<int> ProgressChanged; // 进度百分比0~100
        public event Action<bool> ScanStarted; // true开始，false结束
    }
}
