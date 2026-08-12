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
                TxtStatus.Text = "Загрузка BepInEx 5...";
                string zipUrl = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.21/BepInEx_x64_5.4.21.0.zip";
                byte[] zipBytes = await http.GetByteArrayAsync(zipUrl);

                string tempZip = Path.Combine(Path.GetTempPath(), "BepInEx_temp.zip");
                File.WriteAllBytes(tempZip, zipBytes);

                ZipFile.ExtractToDirectory(tempZip, gamePath, overwriteFiles: true);
                File.Delete(tempZip);

                Directory.CreateDirectory(Path.Combine(gamePath, "BepInEx", "plugins"));

                TxtStatus.Text = "Статус: BepInEx 5 успешно установлен в папку с игрой!";
                MessageBox.Show("BepInEx 5 успешно установлен в папку StoneWards!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
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

            // Мод StoneWardsHD (локальный и из GitHub)
            string gamePlugins = Path.Combine(TxtGamePath.Text, "BepInEx", "plugins");
            bool isInstalled = File.Exists(Path.Combine(gamePlugins, "StoneWardsHD.dll"));

            Mods.Add(new ModItem
            {
                Name = "StoneWardsHD",
                Version = "1.0.0",
                Author = "de7ault & Alan Kertanov",
                Description = "Устранение мыла и пикселей, SMAA/TAA сглаживание и анизотропная фильтрация URP",
                IsEnabled = isInstalled,
                DownloadUrl = "https://github.com/maclaun/stonewards-addons/raw/main/StoneWardsHD/bin/Debug/netstandard2.1/StoneWardsHD.dll",
                FileName = "StoneWardsHD.dll"
            });

            // Запрос дополнительных релизов с GitHub API
            try
            {
                string json = await http.GetStringAsync("https://api.github.com/repos/maclaun/stonewards-addons/releases");
                JArray releases = JArray.Parse(json);

                foreach (JObject rel in releases)
                {
                    string tagName = rel["tag_name"]?.ToString() ?? "v1.0";
                    JArray assets = rel["assets"] as JArray ?? new JArray();

                    foreach (JObject asset in assets)
                    {
                        string name = asset["name"]?.ToString() ?? "";
                        if (name.EndsWith(".dll"))
                        {
                            string downloadUrl = asset["browser_download_url"]?.ToString() ?? "";
                            bool installed = File.Exists(Path.Combine(gamePlugins, name));

                            Mods.Add(new ModItem
                            {
                                Name = Path.GetFileNameWithoutExtension(name),
                                Version = tagName,
                                Author = "Community",
                                Description = "Модификация из релиза GitHub",
                                IsEnabled = installed,
                                DownloadUrl = downloadUrl,
                                FileName = name
                            });
                        }
                    }
                }
            }
            catch
            {
                // Игнорируем отсутствие публичных релизов на GitHub на начальном этапе
            }

            TxtStatus.Text = $"Загружено модов в список: {Mods.Count}";
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

            if (!File.Exists(targetPath))
            {
                try
                {
                    TxtStatus.Text = $"Загрузка мода {mod.Name}...";
                    byte[] data = await http.GetByteArrayAsync(mod.DownloadUrl);
                    File.WriteAllBytes(targetPath, data);
                    mod.IsEnabled = true;
                    TxtStatus.Text = $"Мод {mod.Name} успешно скачан и включен!";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка загрузки мода: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                // Включение / Выключение мода (переименование в .disabled)
                string disabledPath = targetPath + ".disabled";
                if (mod.IsEnabled)
                {
                    if (File.Exists(disabledPath)) File.Delete(disabledPath);
                    File.Move(targetPath, disabledPath);
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
