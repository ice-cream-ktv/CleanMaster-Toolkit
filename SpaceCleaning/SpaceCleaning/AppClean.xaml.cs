using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace SpaceCleaning
{
    /// <summary>
    /// AppClean.xaml 的交互逻辑
    /// </summary>
    public partial class AppClean : UserControl
    {
        // 用户添加目录
        public ObservableCollection<CustomDirectory> UploadedDirectories { get; } = new ObservableCollection<CustomDirectory>();
        public ObservableCollection<JunkFile> ScannedFiles { get; } = new ObservableCollection<JunkFile>();
        public AppClean()
        {
            InitializeComponent();
            this.DataContext = this;
        }
        private void UploadDirectory_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new CommonOpenFileDialog())
            {
                dialog.IsFolderPicker = true;
                dialog.Title = "选择要添加的目录";

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    string selectedPath = dialog.FileName;

                    // 检查是否重复添加
                    if (!UploadedDirectories.Any(d => d.Path.Equals(selectedPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        UploadedDirectories.Add(new CustomDirectory { Path = selectedPath });

                        // 输出UploadedDirectories中所有文件
                        foreach (var directory in UploadedDirectories)
                        {
                            Console.WriteLine(directory.Path);
                        }
                    }
                    else
                    {
                        MessageBox.Show("该目录已添加", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is string path)
            {
                // 查找匹配项并删除
                var item = UploadedDirectories.FirstOrDefault(d => d.Path == path);
                if (item != null)
                {
                    UploadedDirectories.Remove(item);
                }
            }
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            switch (currentState)
            {
                case ScanState.ReadyToScan:
                    cancelRequested = false;
                    currentState = ScanState.Scanning;
                    ScanButton.Content = "取消扫描";
                    ScannedFiles.Clear();

                    ScanStarted?.Invoke(true);

                    await Task.Run(() => ScanFiles());

                    ScanButton.Content = "开始扫描";
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
                        ScanStarted?.Invoke(true);

                        await Task.Run(() => CleanFiles()); // 执行清理
                        ScannedFiles.Clear();
                        MessageBox.Show("清理完成！");
                        string recyclePath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "SpaceCleaner", "RecycleBin");
                        MessageBox.Show($"回收目录路径：{recyclePath}");

                        ProgressChanged?.Invoke(100);
                        await Task.Delay(1000);
                        ScanStarted?.Invoke(false);

                        ScanButton.Content = "开始扫描";
                        currentState = ScanState.ReadyToScan;
                        SkipCleanTextBlock.Visibility = Visibility.Hidden;
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

        private bool IsFileLocked(string filePath)
        {
            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    // 能打开文件，说明文件没被锁定
                    return false;
                }
            }
            catch (IOException)
            {
                // 文件被锁定，抛出异常
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                // 无权限访问也算作“不可操作”
                return true;
            }
        }

        private void ScanFiles()
        {
            if (UploadedDirectories.Count == 0)
            {
                currentState = ScanState.ReadyToScan;
                MessageBox.Show("请先添加需要扫描的目录", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var scanPaths = UploadedDirectories.Select(d => d.Path).ToList();

            var JunkExtensions = new[]
            { ".tmp", ".log", ".bak", ".old", ".gid", ".chk", ".dmp", ".cache", ".~" };

            // 可显示的文件   
            int foundCount = 0;

            // 目录遍历
            foreach (var rootPath in scanPaths)
            {
                if (!Directory.Exists(rootPath)) continue;
                try
                {
                    var dirs = new Stack<string>();
                    dirs.Push(rootPath); // 初始目录入栈

                    while (dirs.Count > 0)
                    {
                        if (cancelRequested) return;
                        var currentDir = dirs.Pop(); // 获取当前目录

                        string[] files = Array.Empty<string>();
                        string[] subDirs = Array.Empty<string>();
                        try
                        {
                            // 获取当前目录下的所有文件
                            files = Directory.GetFiles(currentDir);
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[无权限访问文件] {currentDir}：{ex.Message}");
                            continue;
                        }
                        try
                        {
                            //  获取所有子目录
                            subDirs = Directory.GetDirectories(currentDir);
                            foreach (var sub in subDirs)
                            {
                                dirs.Push(sub);
                            }
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[无权限访问子目录] {currentDir}：{ex.Message}");
                        }

                        foreach (var file in files)
                        {
                            if (cancelRequested) return;
                            try
                            {
                                string ext = Path.GetExtension(file).ToLower(); // 获取文件扩展名
                                if (JunkExtensions.Contains(ext))
                                {
                                    if (IsFileLocked(file))
                                    {
                                        System.Diagnostics.Debug.WriteLine($"文件被占用，跳过：{file}");
                                        continue;
                                    }
                                    // 创建垃圾文件信息对象
                                    var info = new FileInfo(file);
                                    var junk = new JunkFile
                                    {
                                        FilePath = file,
                                        FileSize = $"{info.Length / 1024.0:F2}KB",
                                        FileType = ext
                                    };

                                    foundCount++; // 可显示的文件数量加1

                                    Dispatcher.Invoke(() =>
                                    {
                                        ScannedFiles.Add(junk);
                                    });

                                    Thread.Sleep(10);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[处理文件失败] {file}：{ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex1)
                {
                    System.Diagnostics.Debug.WriteLine($"扫描路径 {rootPath} 出错：{ex1.Message}");
                }
            }
            Dispatcher.Invoke(() =>
            {
                if (foundCount == 0)
                {
                    currentState = ScanState.ReadyToScan;
                    ScanButton.Content = "开始扫描";
                    MessageBox.Show("没有找到可清理的文件！");
                }
            });
        }

        private void CleanFiles()
        {
            string recyclePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SpaceCleaner", "RecycleBin");

            if (!Directory.Exists(recyclePath))
                Directory.CreateDirectory(recyclePath);

            foreach (var file in ScannedFiles)
            {
                try
                {
                    string fileName = Path.GetFileName(file.FilePath);
                    string destPath = Path.Combine(recyclePath, Guid.NewGuid().ToString() + "_" + fileName);

                    File.Move(file.FilePath, destPath);
                    File.WriteAllText(destPath + ".meta", file.FilePath);
                    file.MovedPath = destPath;
                }
                catch (IOException ioEx) when ((ioEx.HResult & 0x0000FFFF) == 32) // 32 = ERROR_SHARING_VIOLATION
                {
                    System.Diagnostics.Debug.WriteLine($"文件被占用，跳过：{file.FilePath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"移动失败：{file.FilePath}，错误：{ex.Message}");
                }
            }
            System.Diagnostics.Debug.WriteLine($"回收目录路径：{recyclePath}");

        }
        public class CustomDirectory
        {
            public string Path { get; set; }
        }
        public class JunkFile
        {
            public string FilePath { get; set; } // 文件原始
            public string FileSize { get; set; }
            public string FileType { get; set; }
            public string MovedPath { get; set; }
        }
        private enum ScanState
        {
            ReadyToScan,
            Scanning,
            ReadyToClean
        }

        private ScanState currentState = ScanState.ReadyToScan;
        private bool cancelRequested = false;
        public event Action<int> ProgressChanged; // 进度百分比0~100
        public event Action<bool> ScanStarted; // true开始，false结束

    }
}
