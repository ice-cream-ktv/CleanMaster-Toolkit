using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace SpaceCleaning
{
    /// <summary>
    /// FileShred.xaml 的交互逻辑
    /// </summary>
    public partial class FileShred : UserControl
    {
        // 用户添加目录
        public ObservableCollection<CustomDirectory> UploadedDirectories { get; } = new ObservableCollection<CustomDirectory>();
        public FileShred()
        {
            InitializeComponent();
            this.DataContext = this;
        }
        private void UploadDirectory_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new CommonOpenFileDialog())
            {
                // 支持同时选择文件和目录
                dialog.IsFolderPicker = false;
                dialog.EnsureFileExists = false; // 允许选择不存在的路径
                dialog.Multiselect = true;      // 允许选择多个项目
                dialog.Title = "选择要添加的文件或目录";

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    foreach (string path in dialog.FileNames)
                    {
                        if (!UploadedDirectories.Any(d => d.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                        {
                            UploadedDirectories.Add(new CustomDirectory
                            {
                                Path = path,
                                Type = "文件" // 直接标记为文件
                            });
                        }
                        else
                        {
                            MessageBox.Show($"\"{Path.GetFileName(path)}\"已添加",
                                           "提示",
                                           MessageBoxButton.OK,
                                           MessageBoxImage.Information);
                        }
                    }

                    // 显示所有已添加项目
                    Debug.WriteLine("当前已添加项目:");
                    foreach (var item in UploadedDirectories)
                    {
                        Debug.WriteLine($"{item.Path} ({item.Type})");
                    }
                }
            }
        }
        private void UploadFloderDirectory_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new CommonOpenFileDialog())
            {
                // 支持同时选择文件和目录
                dialog.IsFolderPicker = true;
                dialog.EnsureFileExists = false; // 允许选择不存在的路径
                dialog.Multiselect = true;      // 允许选择多个项目
                dialog.Title = "选择要添加的文件夹";

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    foreach (string path in dialog.FileNames)
                    {
                        // 检查是否重复添加
                        if (!UploadedDirectories.Any(d => d.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                        {
                            UploadedDirectories.Add(new CustomDirectory
                            {
                                Path = path,
                                Type = "文件夹",
                            });
                        }
                        else
                        {
                            MessageBox.Show($"\"{Path.GetFileName(path)}\"已添加",
                                           "提示",
                                           MessageBoxButton.OK,
                                           MessageBoxImage.Information);
                        }
                    }

                    // 显示所有已添加项目
                    Debug.WriteLine("当前已添加项目:");
                    foreach (var item in UploadedDirectories)
                    {
                        Debug.WriteLine($"{item.Path} ({item.Type})");
                    }
                }
            }
        }
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var path in paths)
            {
                if (!UploadedDirectories.Any(d => d.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                {
                    if (Directory.Exists(path))
                        UploadedDirectories.Add(new CustomDirectory
                        {
                            Path = path,
                            Type = "文件夹"
                        }); // 添加到UploadedDirectories集合
                    else if (File.Exists(path))
                        UploadedDirectories.Add(new CustomDirectory
                        {
                            Path = path,
                            Type = "文件"
                        });
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
        private void FileShred_Click(object sender, RoutedEventArgs e)
        {
            if (UploadedDirectories.Count == 0)
            {
                MessageBox.Show("请先添加要粉碎的文件或目录", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    ScanStarted?.Invoke(true);

                    foreach (var item in UploadedDirectories.ToList())
                    {
                        if (item.Type == "文件夹")
                        {
                            ShredDirectory(item.Path); // 处理目录
                        }
                        else // 处理文件
                        {
                            ShredFile(item.Path); // 直接调用文件粉碎方法
                        }
                    }


                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("文件粉碎完成", "操作成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        UploadedDirectories.Clear();
                    });

                    ProgressChanged?.Invoke(100);

                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                        MessageBox.Show($"粉碎失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error));
                }
                finally
                {
                    Dispatcher.Invoke(() =>
                    {
                        Task.Delay(1000);
                        ScanStarted?.Invoke(false);
                    });
                }
            });
        }
        // 递归粉碎目录
        private void ShredDirectory(string path)
        {
            // 粉碎目录中的所有文件
            foreach (var file in Directory.GetFiles(path))
            {
                ShredFile(file);
            }

            // 递归处理子目录
            foreach (var subDir in Directory.GetDirectories(path))
            {
                ShredDirectory(subDir);
            }

            // 粉碎空目录
            try
            {
                Directory.Delete(path);
            }
            catch { /* 忽略目录删除错误 */ }
        }

        // 文件粉碎核心方法（安全删除）
        private void ShredFile(string filePath)
        {
            const int bufferSize = 1024 * 1024; // 1MB缓冲区
            const int overwriteCount = 3;       // 覆盖次数

            try
            {
                var fileInfo = new FileInfo(filePath);
                long fileLength = fileInfo.Length;

                // 多次覆盖文件内容
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[bufferSize];
                    Random random = new Random();

                    for (int i = 0; i < overwriteCount; i++)
                    {
                        stream.Position = 0;
                        long bytesRemaining = fileLength;

                        while (bytesRemaining > 0)
                        {
                            int bytesToWrite = (int)Math.Min(bufferSize, bytesRemaining);

                            if (i == 0)
                                FillBuffer(buffer, 0x00); // 第一次用 0x00
                            else if (i == 1)
                                FillBuffer(buffer, 0xFF); // 第二次用 0xFF
                            else
                                random.NextBytes(buffer);      // 第三次用随机数
                            stream.Write(buffer, 0, bytesToWrite);

                            bytesRemaining -= bytesToWrite;
                        }

                        stream.Flush(); // 确保数据写入磁盘
                    }
                }

                // 重命名后删除
                string tempName = Path.Combine(
                    Path.GetDirectoryName(filePath),
                    Guid.NewGuid().ToString() + ".tmp"
                );

                File.Move(filePath, tempName);
                File.Delete(tempName);
            }
            catch (IOException ex)
            {
                // 处理文件被占用的情况
                if (IsRunning(filePath))
                {
                    UnlockFile(filePath);
                    File.Delete(filePath); // 直接删除已被解锁的文件
                }
                else
                {
                    throw;
                }
            }
        }
        void FillBuffer(byte[] buffer, byte value)
        {
            for (int j = 0; j < buffer.Length; j++)
                buffer[j] = value;
        }
        private void UnlockFile(string filePath)
        {
            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "handle.exe",
                    Arguments = $"/accepteula \"{filePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // 解析并结束占用进程
            var matches = Regex.Matches(output, @"(?<pid>\d+)\s+pid:");
            foreach (Match match in matches)
            {
                if (int.TryParse(match.Groups["pid"].Value, out int pid))
                {
                    try
                    {
                        Process.GetProcessById(pid).Kill();
                        Thread.Sleep(100); // 给进程结束时间
                    }
                    catch { /* 忽略结束进程失败 */ }
                }
            }
        }
        private bool IsRunning(string processName)
        {
            return System.Diagnostics.Process.GetProcessesByName(processName).Length > 0;
        }
        public class CustomDirectory
        {
            public string Path { get; set; }
            public string Type { get; set; }

        }
        public class JunkFile
        {
            public string FilePath { get; set; } // 文件原始
            public string FileSize { get; set; }
            public string FileType { get; set; }
            public string MovedPath { get; set; }
        }
        public event Action<int> ProgressChanged; // 进度百分比0~100
        public event Action<bool> ScanStarted; // true开始，false结束
    }
}
