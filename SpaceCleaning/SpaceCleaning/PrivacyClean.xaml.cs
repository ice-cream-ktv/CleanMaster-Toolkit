using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SpaceCleaning
{
    /// <summary>
    /// PrivacyClean.xaml 的交互逻辑
    /// </summary>
    public partial class PrivacyClean : UserControl
    {
        public ObservableCollection<JunkFile> ScannedFiles { get; set; } = new ObservableCollection<JunkFile>();
        public PrivacyClean()
        {
            InitializeComponent();
            this.DataContext = this;
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

                    await Task.Run(() =>
                    {
                        ScanChrome();
                        ScanEdge();
                        ScanFirefox();
                        Scan360();
                        ScanQQBrowser();
                    });

                    if (currentState != ScanState.ReadyToScan)
                    {
                        currentState = ScanState.ReadyToClean;
                        ScanButton.Content = "一键清理";
                        SkipCleanTextBlock.Visibility = Visibility.Visible;
                    }

                    // 显示扫描文件数量

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
        private void AddItem(string name, string path)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                Dispatcher.Invoke(() =>
                {
                    ScannedFiles.Add(new JunkFile
                    {
                        Name = name,
                        FilePath = path
                    });
                });
            }
        }
        public static string GetChromeDataPath()
        {
            string[] regKeys = {
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Google Chrome",
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\Google Chrome",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Google Chrome"
        };

            string userDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data");
            string defaultPath = Path.Combine(userDataRoot, "Default");

            // 记录注册表信息 验证安装状态
            bool regKeyFound = false;
            foreach (string key in regKeys)
            {
                string installPath = Registry.GetValue(key, "InstallLocation", null) as string;
                if (installPath != null)
                {
                    regKeyFound = true;
                    break;
                }
                System.Diagnostics.Debug.WriteLine($"Chrome 注册表路径: {key}, 值: {installPath}");
            }
            if (!regKeyFound)
            {
                System.Diagnostics.Debug.WriteLine("错误：未在注册表中找到Chrome安装路径");
                return null; // 立即终止后续逻辑
            }

            System.Diagnostics.Debug.WriteLine($"Chrome 默认用户数据路径: {defaultPath}, 存在: {Directory.Exists(defaultPath)}");

            // 如果 Default 存在，直接返回
            if (Directory.Exists(defaultPath))
            {
                return defaultPath;
            }

            // 扫描其他 Profile 目录
            if (Directory.Exists(userDataRoot))
            {
                var profiles = Directory.GetDirectories(userDataRoot, "Profile *");
                foreach (var profile in profiles)
                {
                    if (Directory.Exists(profile))
                    {
                        System.Diagnostics.Debug.WriteLine($"找到 Chrome 配置文件: {profile}");
                        return profile;
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine("未找到 Chrome 用户数据路径");
            return null;
        }

        private void ScanChrome()
        {
            string basePath = GetChromeDataPath();
            if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
            {
                System.Diagnostics.Debug.WriteLine($"Chrome 数据路径无效或不存在: {basePath}");
                return;
            }
            if (IsBrowserRunning("chrome"))
            {
                System.Diagnostics.Debug.WriteLine("Chrome 浏览器正在运行，可能影响扫描");
                Dispatcher.Invoke(() => MessageBox.Show("请关闭 Chrome 浏览器以确保扫描准确"));
            }
            string cachePath = Path.Combine(basePath, "Cache", "Cache_Data");
            AddItem("Chrome 缓存", Directory.Exists(cachePath) ? cachePath : Path.Combine(basePath, "Cache"));
            Thread.Sleep(10);
            AddItem("Chrome 历史记录", System.IO.Path.Combine(basePath, "History"));
            Thread.Sleep(10);
            string networkCookiesPath = Path.Combine(basePath, "Network", "Cookies");
            string cookiesPath = Path.Combine(basePath, "Cookies");
            if (File.Exists(networkCookiesPath))
            {
                System.Diagnostics.Debug.WriteLine($"找到 Chrome Cookies 文件: {networkCookiesPath}");
                AddItem("Chrome Cookies", networkCookiesPath);
                Thread.Sleep(10);
            }
            else if (File.Exists(cookiesPath))
            {
                System.Diagnostics.Debug.WriteLine($"找到 Chrome Cookies 文件: {cookiesPath}");
                AddItem("Chrome Cookies", cookiesPath);
                Thread.Sleep(10);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Chrome Cookies 文件不存在: {cookiesPath} 或 {networkCookiesPath}");
                string foundCookies = FindCookiesFile(basePath);
                if (!string.IsNullOrEmpty(foundCookies))
                {
                    System.Diagnostics.Debug.WriteLine($"找到备用 Chrome Cookies 文件: {foundCookies}");
                    AddItem("Chrome Cookies", foundCookies);
                    Thread.Sleep(10);
                }
            }
        }

        public static string GetEdgeDataPath()
        {
            // 检查注册表以确认 Edge 是否安装
            string key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe";
            string exePath = Registry.GetValue(key, "", null) as string;
            System.Diagnostics.Debug.WriteLine($"Edge 注册表路径: {exePath}");

            // 构造默认用户数据路径
            string userDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\User Data");
            string defaultPath = Path.Combine(userDataRoot, "Default");
            System.Diagnostics.Debug.WriteLine($"Edge 默认用户数据路径: {defaultPath}, 存在: {Directory.Exists(defaultPath)}");

            // 如果 Default 目录存在，直接返回
            if (Directory.Exists(defaultPath))
            {
                return defaultPath;
            }

            // 如果 Default 不存在，扫描 User Data 下的其他 Profile 目录
            if (Directory.Exists(userDataRoot))
            {
                var profiles = Directory.GetDirectories(userDataRoot, "Profile *");
                foreach (var profile in profiles)
                {
                    if (Directory.Exists(profile))
                    {
                        System.Diagnostics.Debug.WriteLine($"找到 Edge 配置文件: {profile}");
                        return profile; // 返回第一个找到的 Profile 目录
                    }
                }
            }

            // 如果未找到任何有效路径，返回 null
            System.Diagnostics.Debug.WriteLine("未找到 Edge 用户数据路径");
            return null;
        }
        private void ScanEdge()
        {
            string basePath = GetEdgeDataPath();
            AddItem("Edge 缓存", System.IO.Path.Combine(basePath, "Cache"));
            Thread.Sleep(10);
            AddItem("Edge 历史记录", System.IO.Path.Combine(basePath, "History"));
            Thread.Sleep(10);
            string networkCookiesPath = Path.Combine(basePath, "Network", "Cookies");
            string cookiesPath = Path.Combine(basePath, "Cookies");
            if (File.Exists(networkCookiesPath))
            {
                System.Diagnostics.Debug.WriteLine($"找到 Edge Cookies 文件: {networkCookiesPath}");
                AddItem("Edge Cookies", networkCookiesPath);
                Thread.Sleep(10);
            }
            else if (File.Exists(cookiesPath))
            {
                System.Diagnostics.Debug.WriteLine($"找到 Edge Cookies 文件: {cookiesPath}");
                AddItem("Edge Cookies", cookiesPath);
                Thread.Sleep(10);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Edge Cookies 文件不存在: {cookiesPath} 或 {networkCookiesPath}");
                string foundCookies = FindCookiesFile(basePath);
                if (!string.IsNullOrEmpty(foundCookies))
                {
                    System.Diagnostics.Debug.WriteLine($"找到备用 Edge Cookies 文件: {foundCookies}");
                    AddItem("Edge Cookies", foundCookies);
                    Thread.Sleep(10);
                }
            }

        }
        public static List<string> GetFirefoxProfilePaths()
        {
            List<string> paths = new List<string>();
            string profilesRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Mozilla\Firefox\Profiles");
            System.Diagnostics.Debug.WriteLine($"Firefox 配置文件根路径: {profilesRoot}, 存在: {Directory.Exists(profilesRoot)}");

            // 读取默认 Profile
            string defaultProfile = null;
            string regKey = @"HKEY_CURRENT_USER\Software\Mozilla\Mozilla Firefox";
            string currentVersion = Registry.GetValue(regKey, "CurrentVersion", null) as string;

            if (!string.IsNullOrEmpty(currentVersion))
            {
                string profileKey = $@"HKEY_CURRENT_USER\Software\Mozilla\Mozilla Firefox\{currentVersion}\Main";
                defaultProfile = Registry.GetValue(profileKey, "DefaultProfile", null) as string;
                System.Diagnostics.Debug.WriteLine($"Firefox 默认 Profile: {defaultProfile}");
            }

            if (!string.IsNullOrEmpty(defaultProfile))
            {
                string profilePath = Path.Combine(profilesRoot, defaultProfile);
                if (Directory.Exists(profilePath))
                {
                    System.Diagnostics.Debug.WriteLine($"找到 Firefox 默认配置文件: {profilePath}");
                    paths.Add(profilePath);
                }
            }

            // 扫描所有 Profile 目录
            if (Directory.Exists(profilesRoot))
            {
                var profileDirs = Directory.GetDirectories(profilesRoot);
                foreach (var dir in profileDirs)
                {
                    if (!paths.Contains(dir))
                    {
                        System.Diagnostics.Debug.WriteLine($"找到 Firefox 配置文件: {dir}");
                        paths.Add(dir);
                    }
                }
            }

            if (paths.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("未找到 Firefox 用户数据路径");
            }
            return paths;
        }


        private void ScanFirefox()
        {
            var profilePaths = GetFirefoxProfilePaths();
            if (profilePaths.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("未找到 Firefox 配置文件");
                return;
            }
            if (IsBrowserRunning("firefox"))
            {
                System.Diagnostics.Debug.WriteLine("Firefox 浏览器正在运行，可能影响扫描");
                Dispatcher.Invoke(() => MessageBox.Show("请关闭 Firefox 浏览器以确保扫描准确"));
            }
            foreach (var profileDir in profilePaths)
            {
                AddItem("Firefox 缓存", Path.Combine(profileDir, "cache2"));
                Thread.Sleep(10);
                AddItem("Firefox 历史记录", Path.Combine(profileDir, "places.sqlite"));
                Thread.Sleep(10);
                string cookiesPath = Path.Combine(profileDir, "cookies.sqlite");
                string networkCookiesPath = Path.Combine(profileDir, "Network", "Cookies");
                if (File.Exists(cookiesPath))
                {
                    System.Diagnostics.Debug.WriteLine($"找到 Firefox Cookies 文件: {cookiesPath}");
                    AddItem("Firefox Cookies", cookiesPath);
                    Thread.Sleep(10);
                }
                else if (File.Exists(networkCookiesPath))
                {
                    System.Diagnostics.Debug.WriteLine($"找到 Firefox Cookies 文件: {networkCookiesPath}");
                    AddItem("Firefox Cookies", networkCookiesPath);
                    Thread.Sleep(10);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Firefox Cookies 文件不存在: {cookiesPath} 或 {networkCookiesPath}");
                    string foundCookies = FindCookiesFile(profileDir);
                    if (!string.IsNullOrEmpty(foundCookies))
                    {
                        System.Diagnostics.Debug.WriteLine($"找到备用 Firefox Cookies 文件: {foundCookies}");
                        AddItem("Firefox Cookies", foundCookies);
                        Thread.Sleep(10);
                    }
                }
            }
        }

        public static string Get360DataPath()
        {
            string key = @"HKEY_LOCAL_MACHINE\SOFTWARE\360Safe\360se";
            string installPath = Registry.GetValue(key, "Path", null) as string;
            System.Diagnostics.Debug.WriteLine($"360 注册表路径: {installPath}");

            string userDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"360se6\User Data");
            string defaultPath = Path.Combine(userDataRoot, "Default");
            System.Diagnostics.Debug.WriteLine($"360 默认用户数据路径: {defaultPath}, 存在: {Directory.Exists(defaultPath)}");

            if (Directory.Exists(defaultPath))
            {
                return defaultPath;
            }

            if (Directory.Exists(userDataRoot))
            {
                var profiles = Directory.GetDirectories(userDataRoot, "Profile *");
                foreach (var profile in profiles)
                {
                    if (Directory.Exists(profile))
                    {
                        System.Diagnostics.Debug.WriteLine($"找到 360 配置文件: {profile}");
                        return profile;
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine("未找到 360 用户数据路径");
            return null;
        }
        private void Scan360()
        {
            string basePath = Get360DataPath();
            if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
            {
                System.Diagnostics.Debug.WriteLine($"360 数据路径无效或不存在: {basePath}");
                return;
            }

            if (IsBrowserRunning("360se"))
            {
                System.Diagnostics.Debug.WriteLine("360 浏览器正在运行，可能影响扫描");
                Dispatcher.Invoke(() => MessageBox.Show("请关闭 360 浏览器以确保扫描准确"));
            }
            AddItem("360浏览器 缓存", System.IO.Path.Combine(basePath, "Cache"));
            Thread.Sleep(10);
            AddItem("360浏览器 历史记录", System.IO.Path.Combine(basePath, "360History"));
            Thread.Sleep(10);
            string networkCookiesPath = Path.Combine(basePath, "Network", "Cookies");
            string cookiesPath = Path.Combine(basePath, "Cookies");
            if (File.Exists(networkCookiesPath))
            {
                System.Diagnostics.Debug.WriteLine($"找到 360 Cookies 文件: {networkCookiesPath}");
                AddItem("360 Cookies", networkCookiesPath);
                Thread.Sleep(10);
            }
            else if (File.Exists(cookiesPath))
            {
                System.Diagnostics.Debug.WriteLine($"找到 360 Cookies 文件: {cookiesPath}");
                AddItem("360 Cookies", cookiesPath);
                Thread.Sleep(10);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"360 Cookies 文件不存在: {cookiesPath} 或 {networkCookiesPath}");
                string foundCookies = FindCookiesFile(basePath);
                if (!string.IsNullOrEmpty(foundCookies))
                {
                    System.Diagnostics.Debug.WriteLine($"找到备用 360 Cookies 文件: {foundCookies}");
                    AddItem("360 Cookies", foundCookies);
                    Thread.Sleep(10);
                }
            }
        }
        public static string GetQQBrowserDataPath()
        {
            string key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Tencent\QQBrowser";
            string installPath = Registry.GetValue(key, "InstallPath", null) as string;
            System.Diagnostics.Debug.WriteLine($"QQBrowser 注册表路径: {installPath}");

            string userDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Tencent\QQBrowser\User Data");
            string defaultPath = Path.Combine(userDataRoot, "Default");
            System.Diagnostics.Debug.WriteLine($"QQBrowser 默认用户数据路径: {defaultPath}, 存在: {Directory.Exists(defaultPath)}");

            if (Directory.Exists(defaultPath))
            {
                return defaultPath;
            }

            if (Directory.Exists(userDataRoot))
            {
                var profiles = Directory.GetDirectories(userDataRoot, "Profile *");
                foreach (var profile in profiles)
                {
                    if (Directory.Exists(profile))
                    {
                        System.Diagnostics.Debug.WriteLine($"找到 QQBrowser 配置文件: {profile}");
                        return profile;
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine("未找到 QQBrowser 用户数据路径");
            return null;
        }

        private void ScanQQBrowser()
        {
            string basePath = GetQQBrowserDataPath();
            if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
            {
                System.Diagnostics.Debug.WriteLine($"QQBrowser 数据路径无效或不存在: {basePath}");
                return;
            }
            if (IsBrowserRunning("qqbrowser"))
            {
                System.Diagnostics.Debug.WriteLine("QQ 浏览器正在运行，可能影响扫描");
                Dispatcher.Invoke(() => MessageBox.Show("请关闭 QQ 浏览器以确保扫描准确"));
            }
            System.Diagnostics.Debug.WriteLine($"QQ 路径: {basePath}, 存在: {Directory.Exists(basePath)}");
            AddItem("QQ浏览器 缓存", Path.Combine(basePath, "Cache"));
            Thread.Sleep(10);
            AddItem("QQ浏览器 历史记录", Path.Combine(basePath, "History"));
            Thread.Sleep(10);
            string networkCookiesPath = Path.Combine(basePath, "Network", "Cookies");
            string cookiesPath = Path.Combine(basePath, "Cookies");
            if (File.Exists(networkCookiesPath))
            {
                System.Diagnostics.Debug.WriteLine($"找到 QQ Cookies 文件: {networkCookiesPath}");
                AddItem("QQ Cookies", networkCookiesPath);
                Thread.Sleep(10);
            }
            else if (File.Exists(cookiesPath))
            {
                System.Diagnostics.Debug.WriteLine($"找到 QQ Cookies 文件: {cookiesPath}");
                AddItem("QQ Cookies", cookiesPath);
                Thread.Sleep(10);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"QQ Cookies 文件不存在: {cookiesPath} 或 {networkCookiesPath}");
                string foundCookies = FindCookiesFile(basePath);
                if (!string.IsNullOrEmpty(foundCookies))
                {
                    System.Diagnostics.Debug.WriteLine($"找到备用 UC Cookies 文件: {foundCookies}");
                    AddItem("QQ Cookies", foundCookies);
                    Thread.Sleep(10);
                }
            }
        }
        private bool IsBrowserRunning(string processName)
        {
            return System.Diagnostics.Process.GetProcessesByName(processName).Length > 0;
        }
        private string FindCookiesFile(string basePath)
        {
            try
            {
                // 检查标准 Cookies 文件和 Network 子目录，模糊匹配
                var files = Directory.GetFiles(basePath, "Cookies", SearchOption.AllDirectories)
                                   .Concat(Directory.GetFiles(basePath, "cookies.*", SearchOption.AllDirectories));
                foreach (var file in files)
                {
                    if (File.Exists(file))
                    {
                        System.Diagnostics.Debug.WriteLine($"找到 Cookies 文件: {file}");
                        return file;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"扫描 Cookies 文件失败: {basePath}, 错误: {ex.Message}");
            }
            return null;
        }
        public class JunkFile
        {
            public string Name { get; set; }
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
