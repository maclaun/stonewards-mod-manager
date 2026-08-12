using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
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

        private async void LoadModsList()
        {
            Mods.Clear();
            string gameModsDir = Path.Combine(TxtGamePath.Text, "Mods");
            Directory.CreateDirectory(gameModsDir);

            try
            {
                TxtStatus.Text = "Fetching point releases and commit versions from GitHub...";
                
                // 1. Fetch list of mod files in releases/ directory from public repository
                string contentsJson = await http.GetStringAsync("https://api.github.com/repos/maclaun/stonewards-releases/contents/releases");
                JArray filesArray = JArray.Parse(contentsJson);

                var latestModsDict = new Dictionary<string, ModItem>(StringComparer.OrdinalIgnoreCase);

                foreach (JObject fileObj in filesArray)
                {
                    string fileName = fileObj["name"]?.ToString() ?? "";
                    if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        string modName = Path.GetFileNameWithoutExtension(fileName);
                        string rawDownloadUrl = fileObj["download_url"]?.ToString() 
                            ?? $"https://raw.githubusercontent.com/maclaun/stonewards-releases/main/releases/{fileName}";

                        // Fetch point commit version for THIS SPECIFIC MOD FILE
                        string latestVersion = "v1.0.0";
                        string commitMessage = "";
                        try
                        {
                            string commitsJson = await http.GetStringAsync($"https://api.github.com/repos/maclaun/stonewards-releases/commits?path=releases/{fileName}&per_page=1");
                            JArray commitsArray = JArray.Parse(commitsJson);
                            if (commitsArray.Count > 0)
                            {
                                commitMessage = commitsArray[0]["commit"]?["message"]?.ToString() ?? "";
                                Match match = Regex.Match(commitMessage, @"v?\d+\.\d+\.\d+(\.\d+)?", RegexOptions.IgnoreCase);
                                if (match.Success)
                                {
                                    latestVersion = match.Value.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? match.Value : "v" + match.Value;
                                }
                            }
                        }
                        catch { }

                        bool isCore = modName.Equals("StoneWardsITGCore", StringComparison.OrdinalIgnoreCase);
                        string targetPath = Path.Combine(gameModsDir, fileName);
                        string disabledPath = targetPath + ".disabled";
                        bool installed = File.Exists(targetPath) || File.Exists(disabledPath);

                        string installedVersion = "";
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

                        string cleanTag = latestVersion.TrimStart('v');
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

                        string description = string.IsNullOrEmpty(commitMessage) ? "Official StoneWards Mod" : commitMessage;
                        if (isCore)
                        {
                            description = "Mandatory ITG Core System Mod. Manages all in-game ESC mod settings.";
                        }
                        else if (modName.Equals("StoneWardsHD", StringComparison.OrdinalIgnoreCase))
                        {
                            description = "HD graphics, SMAA/TAA anti-aliasing, and anisotropic texture filtering for StoneWards (Unity 6).";
                        }
                        else if (modName.Equals("StoneWardsBetterInfo", StringComparison.OrdinalIgnoreCase))
                        {
                            description = "Enhanced in-game stats and player info mod by Alan Kertanov.";
                        }

                        latestModsDict[modName] = new ModItem
                        {
                            Name = modName,
                            Version = latestVersion,
                            InstalledVersion = string.IsNullOrEmpty(installedVersion) ? "Not Installed" : installedVersion,
                            Author = isCore ? "StoneWards Team" : (modName.Contains("BetterInfo") ? "Alan Kertanov" : "de7ault & Alan"),
                            Description = description,
                            IsEnabled = isCore || File.Exists(targetPath),
                            IsCoreMod = isCore,
                            NeedsUpdate = needsUpdate,
                            DownloadUrl = rawDownloadUrl,
                            FileName = fileName
                        };
                    }
                }

                // Ensure ITGCore mod appears first in list
                if (latestModsDict.TryGetValue("StoneWardsITGCore", out var coreMod))
                {
                    Mods.Add(coreMod);
                    latestModsDict.Remove("StoneWardsITGCore");
                    
                    // Auto-install or update ITGCore if missing or outdated
                    string corePath = Path.Combine(gameModsDir, coreMod.FileName);
                    if (!File.Exists(corePath) || coreMod.NeedsUpdate)
                    {
                        try
                        {
                            byte[] coreBytes = await http.GetByteArrayAsync(coreMod.DownloadUrl);
                            File.WriteAllBytes(corePath, coreBytes);
                            coreMod.NeedsUpdate = false;
                            coreMod.InstalledVersion = coreMod.Version;
                        }
                        catch { }
                    }
                }

                foreach (var mod in latestModsDict.Values)
                {
                    Mods.Add(mod);
                }

                TxtStatus.Text = $"Point releases checked from GitHub commits: {Mods.Count}";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error checking point releases: {ex.Message}";
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
