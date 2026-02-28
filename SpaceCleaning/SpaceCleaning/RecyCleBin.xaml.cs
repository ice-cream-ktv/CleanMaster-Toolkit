using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SpaceCleaning
{
    /// <summary>
    /// RecyCleBin.xaml 的交互逻辑
    /// </summary>
    public partial class RecyCleBin : UserControl
    {
        public ObservableCollection<JunkFile> AllJunkFiles { get; set; } = new ObservableCollection<JunkFile>();


        public RecyCleBin()
        {
            InitializeComponent();
            this.DataContext = this;
            this.Loaded += (s, e) => LoadRecycleBin();
        }

        private void LoadRecycleBin()
        {
            string recyclePath = System.IO.Path.Combine(
               Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
               "SpaceCleaner", "RecycleBin");

            if (!Directory.Exists(recyclePath)) return;

            var files = Directory.GetFiles(recyclePath)
                       .Where(f => !f.EndsWith(".meta"))
                       .ToList();

            foreach (var file in files)
            {
                string metaFile = file + ".meta";
                if (!File.Exists(metaFile)) continue;

                string originalPath = File.ReadAllText(metaFile);

                var info = new FileInfo(file);
                AllJunkFiles.Add(new JunkFile
                {
                    FilePath = file,
                    OriginalPath = originalPath,
                    FileSize = $"{info.Length / 1024.0:F2}KB",
                    FileType = info.Extension
                });
            }
        }

        private void EmptyButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var junk in AllJunkFiles.ToList())
            {
                try
                {
                    File.Delete(junk.FilePath);
                    string meta = junk.FilePath + ".meta";
                    if (File.Exists(meta))
                        File.Delete(meta);

                    AllJunkFiles.Remove(junk);
                }
                catch
                {
                    MessageBox.Show($"删除失败：{junk.FilePath}");
                }
            }

            MessageBox.Show("已清空回收站！");
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var junk in AllJunkFiles.ToList())
            {
                try
                {
                    string directory = System.IO.Path.GetDirectoryName(junk.OriginalPath);

                    if (!Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    File.Move(junk.FilePath, junk.OriginalPath);

                    string meta = junk.FilePath + ".meta";
                    if (File.Exists(meta))
                        File.Delete(meta);

                    AllJunkFiles.Remove(junk);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"恢复失败：{junk.OriginalPath}\n{ex.Message}");
                }
            }

            MessageBox.Show("文件已全部恢复！");
        }


        public class JunkFile
        {
            public string FilePath { get; set; }       // 当前在回收站的路径
            public string OriginalPath { get; set; }   // 原来的路径
            public string FileSize { get; set; }
            public string FileType { get; set; }
        }
    }
}
