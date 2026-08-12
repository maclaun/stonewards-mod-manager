using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StoneWardsModManager
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<ModItem> Mods { get; set; } = new ObservableCollection<ModItem>();
        private static readonly HttpClient http = new HttpClient();
        private Dictionary<string, string> localVersionCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
                TxtStatus.Text = $"Status: Game path detected ({defaultPath})";
            }
            else
            {
                TxtStatus.Text = "Status: Please select your StoneWards game folder";
            }
        }

        private void BrowsePath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                TxtGamePath.Text = dialog.FolderName;
                TxtStatus.Text = $"Status: Selected path {dialog.FolderName}";
            }
        }

        private async void InstallBepInEx_Click(object sender, RoutedEventArgs e)
        {
            string gamePath = TxtGamePath.Text;
            if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            {
                MessageBox.Show("Please select a valid game folder first!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Remove legacy Doorstop winhttp.dll if present to prevent Unity 6 startup crashes
                string winhttp = Path.Combine(gamePath, "winhttp.dll");
                string winhttpBak = Path.Combine(gamePath, "winhttp.dll.bak");
                if (File.Exists(winhttp)) File.Delete(winhttp);
                if (File.Exists(winhttpBak)) File.Delete(winhttpBak);

                TxtStatus.Text = "Downloading MelonLoader 0.6.5 (Unity 6 Compatible)...";
                string zipUrl = "https://github.com/LavaGang/MelonLoader/releases/download/v0.6.5/MelonLoader.x64.zip";
                
                byte[] zipBytes = await http.GetByteArrayAsync(zipUrl);

                string tempZip = Path.Combine(Path.GetTempPath(), "MelonLoader_temp.zip");
                File.WriteAllBytes(tempZip, zipBytes);

                ZipFile.ExtractToDirectory(tempZip, gamePath, overwriteFiles: true);
                File.Delete(tempZip);

                Directory.CreateDirectory(Path.Combine(gamePath, "Mods"));

                TxtStatus.Text = "Status: Mod Loader (MelonLoader 0.6.5) installed successfully!";
                MessageBox.Show("Mod Loader for Unity 6 installed successfully into StoneWards!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error installing Mod Loader: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtStatus.Text = "Mod Loader installation error";
            }
        }

        private void LoadLocalVersionCache(string gameModsDir)
        {
            localVersionCache.Clear();
            string cacheFile = Path.Combine(gameModsDir, "installed_versions.json");
            if (File.Exists(cacheFile))
            {
                try
                {
                    string content = File.ReadAllText(cacheFile);
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(content);
                    if (dict != null)
                    {
                        foreach (var kv in dict) localVersionCache[kv.Key] = kv.Value;
                    }
                }
                catch { }
            }
        }

        private void SaveLocalVersionCache(string gameModsDir, string modName, string version)
        {
            localVersionCache[modName] = version;
            string cacheFile = Path.Combine(gameModsDir, "installed_versions.json");
            try
            {
                string json = JsonConvert.SerializeObject(localVersionCache, Formatting.Indented);
                File.WriteAllText(cacheFile, json);
            }
            catch { }
        }

        private async void LoadModsList()
        {
            Mods.Clear();
            string gameModsDir = Path.Combine(TxtGamePath.Text, "Mods");
            Directory.CreateDirectory(gameModsDir);
            LoadLocalVersionCache(gameModsDir);

            try
            {
                TxtStatus.Text = "Loading mods manifest from CDN (Zero API Limits)...";
                string json = await http.GetStringAsync("https://raw.githubusercontent.com/maclaun/stonewards-releases/main/mods.json");
                JObject root = JObject.Parse(json);
                JArray modsArray = root["mods"] as JArray ?? new JArray();

                foreach (JObject modObj in modsArray)
                {
                    string name = modObj["name"]?.ToString() ?? "";
                    string version = modObj["version"]?.ToString() ?? "v1.0.0";
                    string author = modObj["author"]?.ToString() ?? "StoneWards Team";
                    string description = modObj["description"]?.ToString() ?? "";
                    bool isCore = modObj["isCore"]?.ToObject<bool>() ?? false;
                    string fileName = modObj["fileName"]?.ToString() ?? (name + ".dll");
                    string downloadUrl = modObj["downloadUrl"]?.ToString() 
                        ?? $"https://raw.githubusercontent.com/maclaun/stonewards-releases/main/releases/{fileName}";

                    if (!version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    {
                        version = "v" + version;
                    }

                    string targetPath = Path.Combine(gameModsDir, fileName);
                    string disabledPath = targetPath + ".disabled";
                    bool installed = File.Exists(targetPath) || File.Exists(disabledPath);

                    string installedVersion = "";
                    if (installed)
                    {
                        if (localVersionCache.TryGetValue(name, out var cachedVer) && !string.IsNullOrEmpty(cachedVer))
                        {
                            installedVersion = cachedVer.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? cachedVer : "v" + cachedVer;
                        }
                        else
                        {
                            string actualFile = File.Exists(targetPath) ? targetPath : (File.Exists(disabledPath) ? disabledPath : null);
                            if (actualFile != null)
                            {
                                try
                                {
                                    var vInfo = FileVersionInfo.GetVersionInfo(actualFile);
                                    installedVersion = vInfo.FileVersion ?? vInfo.ProductVersion ?? "";
                                    if (!string.IsNullOrEmpty(installedVersion))
                                    {
                                        int plusIdx = installedVersion.IndexOf('+');
                                        if (plusIdx > 0) installedVersion = installedVersion.Substring(0, plusIdx);
                                        if (!installedVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                                        {
                                            installedVersion = "v" + installedVersion;
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    string cleanTag = version.TrimStart('v');
                    string cleanInstalled = installedVersion.TrimStart('v');

                    bool needsUpdate = false;
                    if (installed && Version.TryParse(cleanInstalled, out Version? vLocal) && Version.TryParse(cleanTag, out Version? vRemote))
                    {
                        needsUpdate = vLocal < vRemote;
                    }
                    else if (installed && !string.IsNullOrEmpty(cleanInstalled) && !string.IsNullOrEmpty(cleanTag))
                    {
                        needsUpdate = !cleanInstalled.StartsWith(cleanTag, StringComparison.OrdinalIgnoreCase) 
                                   && !cleanTag.StartsWith(cleanInstalled, StringComparison.OrdinalIgnoreCase);
                    }

                    var modItem = new ModItem
                    {
                        Name = name,
                        Version = version,
                        InstalledVersion = string.IsNullOrEmpty(installedVersion) ? "Not Installed" : installedVersion,
                        Author = author,
                        Description = description,
                        IsEnabled = isCore || File.Exists(targetPath),
                        IsCoreMod = isCore,
                        NeedsUpdate = needsUpdate,
                        DownloadUrl = downloadUrl,
                        FileName = fileName
                    };

                    // Auto-install ITGCore if missing or outdated
                    if (isCore)
                    {
                        if (!File.Exists(targetPath) || needsUpdate)
                        {
                            try
                            {
                                byte[] coreBytes = await http.GetByteArrayAsync(downloadUrl);
                                File.WriteAllBytes(targetPath, coreBytes);
                                SaveLocalVersionCache(gameModsDir, name, version);
                                modItem.NeedsUpdate = false;
                                modItem.InstalledVersion = version;
                                modItem.IsEnabled = true;
                            }
                            catch { }
                        }
                    }

                    Mods.Add(modItem);
                }

                TxtStatus.Text = $"Mods manifest loaded instantly: {Mods.Count} mods available.";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error loading mods manifest: {ex.Message}";
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
            if (mod == null || mod.IsCoreMod) return;

            string modsDir = Path.Combine(TxtGamePath.Text, "Mods");
            Directory.CreateDirectory(modsDir);
            string targetPath = Path.Combine(modsDir, mod.FileName);
            string disabledPath = targetPath + ".disabled";

            // Update scenario
            if (mod.NeedsUpdate)
            {
                try
                {
                    TxtStatus.Text = $"Updating mod {mod.Name} to {mod.Version}...";
                    byte[] data = await http.GetByteArrayAsync(mod.DownloadUrl);
                    if (File.Exists(disabledPath)) File.Delete(disabledPath);
                    File.WriteAllBytes(targetPath, data);
                    
                    SaveLocalVersionCache(modsDir, mod.Name, mod.Version);
                    mod.NeedsUpdate = false;
                    mod.IsEnabled = true;
                    mod.InstalledVersion = mod.Version;
                    TxtStatus.Text = $"Mod {mod.Name} updated successfully to {mod.Version}!";
                    MessageBox.Show($"Mod {mod.Name} updated successfully to {mod.Version}!", "Update Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error updating mod: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // Download scenario
            if (!File.Exists(targetPath) && !File.Exists(disabledPath))
            {
                try
                {
                    TxtStatus.Text = $"Downloading latest mod {mod.Name} ({mod.Version})...";
                    byte[] data = await http.GetByteArrayAsync(mod.DownloadUrl);
                    File.WriteAllBytes(targetPath, data);
                    SaveLocalVersionCache(modsDir, mod.Name, mod.Version);
                    mod.IsEnabled = true;
                    mod.InstalledVersion = mod.Version;
                    TxtStatus.Text = $"Mod {mod.Name} ({mod.Version}) downloaded and enabled successfully!";
                    MessageBox.Show($"Mod {mod.Name} ({mod.Version}) installed successfully into Mods folder!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error downloading mod: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                // Toggle Mod (Rename to .disabled)
                if (mod.IsEnabled)
                {
                    if (File.Exists(disabledPath)) File.Delete(disabledPath);
                    if (File.Exists(targetPath)) File.Move(targetPath, disabledPath);
                    mod.IsEnabled = false;
                    TxtStatus.Text = $"Mod {mod.Name} disabled.";
                }
                else
                {
                    if (File.Exists(disabledPath))
                    {
                        if (File.Exists(targetPath)) File.Delete(targetPath);
                        File.Move(disabledPath, targetPath);
                    }
                    mod.IsEnabled = true;
                    TxtStatus.Text = $"Mod {mod.Name} enabled.";
                }
            }
        }

        private void LaunchGame_Click(object sender, RoutedEventArgs e)
        {
            string exePath = Path.Combine(TxtGamePath.Text, "Stonewards.exe");
            if (!File.Exists(exePath))
            {
                MessageBox.Show("Stonewards.exe not found in the specified game directory!", "Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                TxtStatus.Text = "StoneWards launched!";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error launching game: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class ModItem : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private bool _needsUpdate;
        private string _installedVersion = "";

        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string FileName { get; set; } = "";
        public bool IsCoreMod { get; set; } = false;

        public string InstalledVersion
        {
            get => _installedVersion;
            set
            {
                _installedVersion = value;
                OnPropertyChanged(nameof(InstalledVersion));
            }
        }

        public bool NeedsUpdate
        {
            get => _needsUpdate;
            set
            {
                _needsUpdate = value;
                OnPropertyChanged(nameof(NeedsUpdate));
                OnPropertyChanged(nameof(ActionText));
                OnPropertyChanged(nameof(ButtonColor));
            }
        }

        public bool IsNotCoreMod => !IsCoreMod;

        public bool IsEnabled
        {
            get => IsCoreMod || _isEnabled;
            set
            {
                if (IsCoreMod) return;
                _isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(ActionText));
                OnPropertyChanged(nameof(ButtonColor));
            }
        }

        public string ActionText
        {
            get
            {
                if (IsCoreMod) return "System Core Mod";
                if (NeedsUpdate) return $"🔄 Update ({Version})";
                return IsEnabled ? "Disable" : "Download / Enable";
            }
        }

        public string ButtonColor
        {
            get
            {
                if (IsCoreMod) return "#4B5563";
                if (NeedsUpdate) return "#F59E0B"; // Bright Amber for Updates!
                return IsEnabled ? "#EF4444" : "#0EA5E9";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
