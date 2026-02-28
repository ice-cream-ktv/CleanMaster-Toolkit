using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SpaceCleaning
{
    public partial class PluginClean : UserControl
    {
        public ObservableCollection<JunkFile> ScannedFiles { get; set; } = new ObservableCollection<JunkFile>();
        public PluginClean()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            if (!IsRunningAsAdmin())
            {
                MessageBox.Show("需要管理员权限才能扫描和清理插件！", "权限错误", MessageBoxButton.OK, MessageBoxImage.Error);
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

                    await Task.Run(() => ScanPlugins());

                    if (currentState != ScanState.ReadyToScan)
                    {
                        currentState = ScanState.ReadyToClean;
                        ScanButton.Content = "一键清理";
                        SkipCleanTextBlock.Visibility = Visibility.Visible;
                    }
                    if (ScannedFiles.Count == 0)
                    {
                        currentState = ScanState.ReadyToScan;
                        ScanButton.Content = "开始扫描";
                        SkipCleanTextBlock.Visibility = Visibility.Hidden;
                    }

                    ProgressChanged?.Invoke(100);
                    await Task.Delay(1000);
                    ScanStarted?.Invoke(false);
                    break;

                case ScanState.Scanning:
                    cancelRequested = true;
                    break;

                case ScanState.ReadyToClean:
                    var result = MessageBox.Show("此操作会将插件移动到回收区，可恢复", "确认清理", MessageBoxButton.OKCancel, MessageBoxImage.Information);
                    if (result == MessageBoxResult.OK)
                    {
                        CleanPlugins();
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

        private void ScanPlugins()
        {
            // 1. 扫描浏览器插件
            ScanBrowserPlugins();

            // 2. 扫描系统插件
            // ScanSystemPlugins();

            // 3. 扫描应用程序插件
            ScanApplicationPlugins();

            // 4. 扫描注册表中的插件残留
            ScanRegistryPlugins();
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
        public static string GetChromeDataPath()
        {
            string[] regKeys = {
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Google Chrome",
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\Google Chrome",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Google Chrome"
        };

            string userDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data");
            string defaultPath = Path.Combine(userDataRoot, "Default");

            // 记录注册表信息
            foreach (string key in regKeys)
            {
                string installPath = Registry.GetValue(key, "InstallLocation", null) as string;
                System.Diagnostics.Debug.WriteLine($"Chrome 注册表路径: {key}, 值: {installPath}");
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

        private void ScanBrowserPlugins()
        {
            // Chrome 插件扫描
            string chromeData = GetChromeDataPath();
            if (!string.IsNullOrEmpty(chromeData))
            {
                ScanBrowserPluginDirectory(
                    Path.Combine(chromeData, "Extensions"),
                    "Chrome插件");
            }

            // Firefox 插件扫描
            var firefoxProfiles = GetFirefoxProfilePaths();
            foreach (var profile in firefoxProfiles)
            {
                // 火狐插件存储在extensions目录 
                string extensionsPath = Path.Combine(profile, "extensions");
                ScanBrowserPluginDirectory(extensionsPath, "Firefox插件");
            }

            // Edge 插件扫描
            string edgeData = GetEdgeDataPath();
            if (!string.IsNullOrEmpty(edgeData))
            {
                ScanBrowserPluginDirectory(
                    Path.Combine(edgeData, "Extensions"), // 修正为正确插件目录
                    "Edge插件");
            }
        }

        private void ScanBrowserPluginDirectory(string path, string pluginType)
        {
            if (!Directory.Exists(path)) return;
            Debug.WriteLine($"扫描浏览器位置: {path}");
            try
            {
                // 扫描一级子目录（每个插件一个目录）
                foreach (var pluginDir in Directory.GetDirectories(path))
                {
                    if (cancelRequested) return;

                    // 检查插件是否必要（示例：使用频率）
                    if (IsUnnecessaryPlugin(pluginDir))
                    {
                        var dirInfo = new DirectoryInfo(pluginDir);
                        long size = CalculateDirectorySize(pluginDir);

                        Dispatcher.Invoke(() =>
                        {
                            ScannedFiles.Add(new JunkFile
                            {
                                FilePath = pluginDir,
                                FileSize = $"{size / 1024}KB",
                                FileType = pluginType,
                                PluginName = Path.GetFileName(pluginDir)
                            });
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"扫描浏览器插件失败: {path}, 错误: {ex.Message}");
            }
        }

        private void ScanSystemPlugins()
        {
            //1.扫描 System32 目录下的插件
            string system32Path = Environment.GetFolderPath(Environment.SpecialFolder.System);
            Debug.WriteLine($"扫描系统插件位置: {system32Path}");
            ScanPluginDirectory(system32Path, "系统插件");

            //2.扫描 SysWOW64 目录（32 位插件在 64 位系统中）
            string sysWow64Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64");
            Debug.WriteLine($"扫描系统插件位置2: {sysWow64Path}");
            if (Directory.Exists(sysWow64Path))
            {
                ScanPluginDirectory(sysWow64Path, "系统插件");
            }

            //// 3. Windows Media Player 插件
            string wmpPluginsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Media Player", "Plugins");
            Debug.WriteLine($"扫描媒体播放位置: {wmpPluginsPath}{Directory.Exists(wmpPluginsPath)}");
            ScanPluginDirectory(wmpPluginsPath, "媒体插件");

            // 4. 注册表中 Shell 扩展（Shell Extensions，可选扩展）
            ScanShellExtensionRegistry();
        }

        private void ScanShellExtensionRegistry()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved"))
                {
                    if (key != null)
                    {
                        foreach (var name in key.GetValueNames())
                        {
                            string description = key.GetValue(name)?.ToString() ?? "";

                            Dispatcher.Invoke(() =>
                            {
                                ScannedFiles.Add(new JunkFile
                                {
                                    FilePath = $"CLSID: {name}",
                                    FileSize = "-",
                                    FileType = "Shell扩展",
                                    PluginName = description
                                });
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"扫描Shell扩展失败: {ex.Message}");
            }
        }


        private string GetWpsInstallPathFromRegistry()
        {
            string[] registryKeys = {
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Kingsoft\Office\6.0\common",  // 64位系统标准路径[2](@ref)
                @"HKEY_CURRENT_USER\Software\Kingsoft\Office\6.0\common",              // 用户级配置路径[4](@ref)
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Kingsoft\Office",                        // 传统路径
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WPS Office"
            };

            // 尝试的键值名称（不同版本可能使用不同键名）
            string[] valueNames = { "InstallPath", "Path", "RootPath", "InstallRoot" };

            foreach (var key in registryKeys)
            {
                foreach (var valueName in valueNames)
                {
                    string installPath = (string)Registry.GetValue(key, valueName, null);
                    if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                    {
                        return installPath;
                    }
                }
            }

            // 通过App Paths查询
            string appPath = (string)Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\wps.exe",
                "Path",
                null
            );

            if (!string.IsNullOrEmpty(appPath)) return appPath;

            Debug.WriteLine("未找到WPS安装路径");
            return null;
        }
        private void ScanApplicationPlugins()
        {
            // Office插件
            ScanPluginDirectory(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "AddIns"),
                "Office插件");

            // 扫描所有可能的Adobe插件路径
            string[] adobePaths = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Adobe", "Plug-ins"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Adobe", "Plug-ins"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "Adobe", "Plug-ins")
            };
            foreach (var path in adobePaths)
            {
                if (Directory.Exists(path))
                {
                    ScanPluginDirectory(path, "Adobe插件");
                }
            }

            // 扫描VS插件
            string vsExtensionsPath = Path.Combine(
                 Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                 "Microsoft", "VisualStudio"
             );
            if (Directory.Exists(vsExtensionsPath))
            {
                // 遍历 VisualStudio 目录下的子文件夹（不同版本，如 16.0, 17.0 等）
                foreach (var versionDir in Directory.GetDirectories(vsExtensionsPath))
                {
                    string extensionsPath = Path.Combine(versionDir, "Extensions");
                    // Debug.WriteLine($"扫描VS插件位置: {extensionsPath}");
                    if (Directory.Exists(extensionsPath))
                    {
                        ScanPluginDirectory(extensionsPath, $"Visual Studio 插件 (版本 {Path.GetFileName(versionDir)})");
                    }
                }
            }

            // 扫描vscode
            string vscodeUserPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".vscode", "extensions"
            );
            ScanPluginDirectory(vscodeUserPath, "VSCode插件");

            // WPS Windows版插件路径
            string wpsPath = GetWpsInstallPathFromRegistry();
            Debug.WriteLine($"WPS插件位置: {wpsPath}");
            if (wpsPath != null)
            {
                string addonsPath = Path.Combine(wpsPath, "office6", "addons");
                ScanPluginDirectory(addonsPath, "WPS插件");
            }
        }

        private void ScanRegistryPlugins()
        {
            try
            {
                // 扫描 Internet Explorer 的浏览器助手对象 (BHO)
                ScanBHOPlugins(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects", "浏览器助手对象 (用户)");
                ScanBHOPlugins(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects", "浏览器助手对象 (系统)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"扫描注册表插件失败: {ex.Message}");
            }
        }

        // 扫描 BHO 插件的辅助方法IE
        private void ScanBHOPlugins(RegistryKey rootKey, string subKeyPath, string pluginType)
        {
            try
            {
                using (var key = rootKey.OpenSubKey(subKeyPath))
                {
                    if (key == null)
                    {
                        Debug.WriteLine($"注册表路径 {subKeyPath} 不存在");
                        return;
                    }

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using (var subKey = key.OpenSubKey(subKeyName))
                            {
                                if (subKey == null)
                                {
                                    Debug.WriteLine($"无法打开子键 {subKeyName}");
                                    continue;
                                }

                                // 获取 CLSID 对应的文件路径
                                string pluginPath = GetPluginFilePath(subKeyName);
                                if (string.IsNullOrEmpty(pluginPath) || !File.Exists(pluginPath))
                                {
                                    Debug.WriteLine($"插件路径无效或文件不存在: CLSID={subKeyName}, 路径={pluginPath ?? "未找到"}");
                                    continue;
                                }

                                // 检查插件是否必要
                                if (IsUnnecessaryPlugin(pluginPath))
                                {
                                    var fileInfo = new FileInfo(pluginPath);
                                    Dispatcher.Invoke(() =>
                                    {
                                        ScannedFiles.Add(new JunkFile
                                        {
                                            FilePath = pluginPath,
                                            FileSize = $"{fileInfo.Length / 1024}KB",
                                            FileType = pluginType,
                                            PluginName = Path.GetFileNameWithoutExtension(pluginPath)
                                        });
                                    });
                                    Debug.WriteLine($"添加插件: {pluginPath}, 类型: {pluginType}");
                                }
                                else
                                {
                                    Debug.WriteLine($"插件被认为必要，跳过: {pluginPath}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"处理子键 {subKeyName} 失败: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"扫描 {subKeyPath} 失败: {ex.Message}");
            }
        }

        // 获取插件文件路径（通过 CLSID 查找 InprocServer32）
        private string GetPluginFilePath(string clsid)
        {
            try
            {
                using (var clsidKey = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}\InprocServer32"))
                {
                    if (clsidKey != null)
                    {
                        string path = clsidKey.GetValue("") as string;
                        if (!string.IsNullOrEmpty(path))
                        {
                            // 解析环境变量（如 %SystemRoot%）
                            path = Environment.ExpandEnvironmentVariables(path);
                            Debug.WriteLine($"CLSID {clsid} 的文件路径: {path}");
                            return path;
                        }
                    }
                }
                Debug.WriteLine($"CLSID {clsid} 未找到 InprocServer32 路径");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取 CLSID {clsid} 的文件路径失败: {ex.Message}");
            }
            return null;
        }

        private void ScanPluginDirectory(string path, string pluginType)
        {
            if (!Directory.Exists(path)) return;

            try
            {
                RecursiveScan(path, pluginType);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"扫描插件目录失败: {path}, 错误: {ex.Message}");
            }
        }

        // 递归扫描
        private void RecursiveScan(string directory, string pluginType)
        {
            string[] files = Array.Empty<string>();
            string[] subDirs = Array.Empty<string>();

            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (UnauthorizedAccessException)
            {
                Debug.WriteLine($"跳过无权限目录（获取文件失败）: {directory}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取文件失败: {directory}, 错误: {ex.Message}");
            }

            foreach (var file in files)
            {
                try
                {
                    if (cancelRequested) return;
                    var pluginExtensions = new[] { ".dll", ".ocx", ".xll", ".8bf", ".plugin" };
                    string ext = Path.GetExtension(file).ToLower();
                    //   Debug.WriteLine($"扫描文件: {file}");
                    if (pluginExtensions.Contains(ext) && IsUnnecessaryPlugin(file))
                    {
                        var fileInfo = new FileInfo(file);

                        Dispatcher.Invoke(() =>
                        {
                            ScannedFiles.Add(new JunkFile
                            {
                                FilePath = file,
                                FileSize = $"{fileInfo.Length / 1024}KB",
                                FileType = pluginType,
                                PluginName = Path.GetFileNameWithoutExtension(file)
                            });
                        });
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    Debug.WriteLine($"跳过无权限文件: {file}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"扫描文件失败: {file}, 错误: {ex.Message}");
                }
            }

            try
            {
                subDirs = Directory.GetDirectories(directory);
            }
            catch (UnauthorizedAccessException)
            {
                Debug.WriteLine($"跳过无权限目录（获取子目录失败）: {directory}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取子目录失败: {directory}, 错误: {ex.Message}");
            }

            foreach (var subDir in subDirs)
            {
                RecursiveScan(subDir, pluginType);
            }
        }

        private bool IsUnnecessaryPlugin(string path)
        {
            return true;
            try
            {
                string lowerPath = path.ToLower();

                // 排除系统关键目录
                string[] protectedDirs = {
                    @"windows\system32",
                    @"windows\syswow64",
                    @"windows\winsxs",
                    @"windows\assembly"
                };
                bool isProtectedDir = protectedDirs.Any(dir => lowerPath.Contains(dir));
                // 系统目录专属检查：仅当满足所有条件才视为可清理
                if (isProtectedDir)
                {
                    // 条件1：无微软数字签名
                    if (!HasMicrosoftSignature(path))
                    {
                        // 条件2：超60天未使用
                        DateTime lastAccess = GetLastAccessTime(path);
                        if ((DateTime.Now - lastAccess).TotalDays > 60)
                        {
                            Debug.WriteLine($"⚠️ 发现可疑系统插件: {path}");
                            return true; // 仅当同时满足2个条件才清理

                        }
                    }
                    return false; // 默认跳过系统目录
                }
                else // 非系统目录保持原逻辑
                {
                    Debug.WriteLine($"扫描非系统插件: {path} 时间 {(DateTime.Now - GetLastAccessTime(path)).TotalDays}");
                    if ((DateTime.Now - GetLastAccessTime(path)).TotalDays > 60)
                    {
                        return true;
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"判断插件是否必要失败: {path}, 错误: {ex.Message}");
                return false;
            }
        }
        private DateTime GetLastAccessTime(string path)
        {
            if (Directory.Exists(path))
                return new DirectoryInfo(path).LastAccessTime;
            else
                return new FileInfo(path).LastAccessTime;
        }
        private bool HasMicrosoftSignature(string path)
        {
            try
            {
                X509Certificate cert = X509Certificate.CreateFromSignedFile(path);
                return cert.Issuer.Contains("Microsoft Corporation"); // 验证微软签名
            }
            catch { return false; } // 无签名文件直接视为可疑
        }
        private long CalculateDirectorySize(string path)
        {
            long size = 0;
            var dirInfo = new DirectoryInfo(path);

            foreach (var file in dirInfo.GetFiles("*.*", SearchOption.AllDirectories))
            {
                size += file.Length;
            }
            return size;
        }

        private void CleanPlugins()
        {
            string recyclePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SpaceCleaner", "PluginRecycle");

            if (!Directory.Exists(recyclePath))
                Directory.CreateDirectory(recyclePath);

            foreach (var plugin in ScannedFiles)
            {
                try
                {
                    if (File.Exists(plugin.FilePath))
                    {
                        string fileName = Path.GetFileName(plugin.FilePath);
                        string destPath = Path.Combine(recyclePath, Guid.NewGuid().ToString() + "_" + fileName);

                        File.Move(plugin.FilePath, destPath);
                        File.WriteAllText(destPath + ".meta", plugin.FilePath);
                        plugin.MovedPath = destPath;
                    }
                    else if (Directory.Exists(plugin.FilePath))
                    {
                        string dirName = new DirectoryInfo(plugin.FilePath).Name;
                        string destPath = Path.Combine(recyclePath, Guid.NewGuid().ToString() + "_" + dirName);

                        Directory.Move(plugin.FilePath, destPath);
                        File.WriteAllText(Path.Combine(destPath, ".meta"), plugin.FilePath);
                        plugin.MovedPath = destPath;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"移动插件失败: {plugin.FilePath}, 错误: {ex.Message}");
                }
            }

            MessageBox.Show($"插件已移动到回收区: {recyclePath}");
        }

        // 检查管理员权限
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
            public string FilePath { get; set; }
            public string FileSize { get; set; }
            public string FileType { get; set; }
            public string PluginName { get; set; }
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
        public event Action<int> ProgressChanged;
        public event Action<bool> ScanStarted;
    }
}