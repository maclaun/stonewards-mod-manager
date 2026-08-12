using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace StoneWardsModManager
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<ModItem> Mods { get; set; } = new ObservableCollection<ModItem>();
        private static readonly HttpClient http = new HttpClient();

        public MainWindow()
        {
            InitializeComponent();
            http.DefaultRequestHeaders.Add("User-Agent", "StoneWardsModManager");
            GridMods.ItemsSource = Mods;

            AutoDetectGamePath();
            LoadModsList();
        }

        private void AutoDetectGamePath()
        {
            string defaultPath = @"C:\Stonewards";
            if (Directory.Exists(defaultPath))
            {
                TxtGamePath.Text = defaultPath;
                TxtStatus.Text = $"Статус: Папка с игрой найдена ({defaultPath})";
            }
            else
            {
                TxtStatus.Text = "Статус: Выберите папку с игрой StoneWards";
            }
        }

        private void BrowsePath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                TxtGamePath.Text = dialog.FolderName;
                TxtStatus.Text = $"Статус: Выбрана папка {dialog.FolderName}";
            }
        }

        private async void InstallBepInEx_Click(object sender, RoutedEventArgs e)
        {
            string gamePath = TxtGamePath.Text;
            if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            {
                MessageBox.Show("Пожалуйста, сначала укажите корректную папку с игрой!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Удаляем старые инжекторы BepInEx 5 (winhttp.dll / doorstop), так как Unity 6 вылетает от BepInEx 5
                string winhttp = Path.Combine(gamePath, "winhttp.dll");
                string winhttpBak = Path.Combine(gamePath, "winhttp.dll.bak");
                if (File.Exists(winhttp)) File.Delete(winhttp);
                if (File.Exists(winhttpBak)) File.Delete(winhttpBak);

                TxtStatus.Text = "Загрузка BepInEx 6 (Совместим с Unity 6 / 6000.x)...";
                string zipUrl = "https://github.com/BepInEx/BepInEx/releases/download/v6.0.0-pre.1/BepInEx_UnityMono_x64_6.0.0-pre.1.zip";
                
                byte[] zipBytes = await http.GetByteArrayAsync(zipUrl);

                string tempZip = Path.Combine(Path.GetTempPath(), "BepInEx6_temp.zip");
                File.WriteAllBytes(tempZip, zipBytes);

                ZipFile.ExtractToDirectory(tempZip, gamePath, overwriteFiles: true);
                File.Delete(tempZip);

                Directory.CreateDirectory(Path.Combine(gamePath, "BepInEx", "plugins"));

                TxtStatus.Text = "Статус: BepInEx 6 (Unity 6) успешно установлен!";
                MessageBox.Show("BepInEx 6 (для Unity 6) успешно установлен в папку StoneWards!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при установке BepInEx: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtStatus.Text = "Ошибка установки BepInEx";
            }
        }

        private async void LoadModsList()
        {
            Mods.Clear();
            string gamePlugins = Path.Combine(TxtGamePath.Text, "BepInEx", "plugins");

            try
            {
                TxtStatus.Text = "Загрузка списка модов с GitHub...";
                string json = await http.GetStringAsync("https://api.github.com/repos/maclaun/stonewards-addons/releases");
                JArray releases = JArray.Parse(json);

                foreach (JObject rel in releases)
                {
                    string tagName = rel["tag_name"]?.ToString() ?? "v1.0.0";
                    string body = rel["notes"]?.ToString() ?? rel["body"]?.ToString() ?? "Официальный мод StoneWards";
                    JArray assets = rel["assets"] as JArray ?? new JArray();

                    foreach (JObject asset in assets)
                    {
                        string fileName = asset["name"]?.ToString() ?? "";
                        if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            string downloadUrl = asset["browser_download_url"]?.ToString() ?? "";
                            bool installed = File.Exists(Path.Combine(gamePlugins, fileName));

                            Mods.Add(new ModItem
                            {
                                Name = Path.GetFileNameWithoutExtension(fileName),
                                Version = tagName,
                                Author = "StoneWards Team",
                                Description = body,
                                IsEnabled = installed,
                                DownloadUrl = downloadUrl,
                                FileName = fileName
                            });
                        }
                    }
                }

                TxtStatus.Text = $"Загружено доступных модов с GitHub: {Mods.Count}";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Ошибка получения релизов с GitHub: {ex.Message}";
            }
        }

        private void RefreshMods_Click(object sender, RoutedEventArgs e)
        {
            LoadModsList();
        }

        private async void DownloadOrToggleMod_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            var mod = btn?.DataContext as ModItem;
            if (mod == null) return;

            string pluginsDir = Path.Combine(TxtGamePath.Text, "BepInEx", "plugins");
            Directory.CreateDirectory(pluginsDir);
            string targetPath = Path.Combine(pluginsDir, mod.FileName);
            string disabledPath = targetPath + ".disabled";

            if (!File.Exists(targetPath) && !File.Exists(disabledPath))
            {
                try
                {
                    TxtStatus.Text = $"Загрузка мода {mod.Name} с GitHub Releases...";
                    byte[] data = await http.GetByteArrayAsync(mod.DownloadUrl);
                    File.WriteAllBytes(targetPath, data);
                    mod.IsEnabled = true;
                    TxtStatus.Text = $"Мод {mod.Name} успешно скачан и включен!";
                    MessageBox.Show($"Мод {mod.Name} успешно установлен в BepInEx/plugins!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка загрузки мода: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                // Включение / Выключение мода (переименование в .disabled)
                if (mod.IsEnabled)
                {
                    if (File.Exists(disabledPath)) File.Delete(disabledPath);
                    if (File.Exists(targetPath)) File.Move(targetPath, disabledPath);
                    mod.IsEnabled = false;
                    TxtStatus.Text = $"Мод {mod.Name} отключен.";
                }
                else
                {
                    if (File.Exists(disabledPath))
                    {
                        if (File.Exists(targetPath)) File.Delete(targetPath);
                        File.Move(disabledPath, targetPath);
                    }
                    mod.IsEnabled = true;
                    TxtStatus.Text = $"Мод {mod.Name} включен.";
                }
            }
        }

        private void LaunchGame_Click(object sender, RoutedEventArgs e)
        {
            string exePath = Path.Combine(TxtGamePath.Text, "Stonewards.exe");
            if (!File.Exists(exePath))
            {
                MessageBox.Show("Файл Stonewards.exe не найден в указанной папке!", "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = TxtGamePath.Text,
                    UseShellExecute = true
                });
                TxtStatus.Text = "Игра StoneWards запущена!";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при запуске игры: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class ModItem : INotifyPropertyChanged
    {
        private bool _isEnabled;
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string FileName { get; set; } = "";

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(ActionText));
            }
        }

        public string ActionText => IsEnabled ? "Выключить" : "Скачать / Включить";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
