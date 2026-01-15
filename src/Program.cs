using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace WSLServiceAgent
{
    static class Program
    {
        private static Mutex? _mutex;
        private static NotifyIcon? _trayIcon;
        private static Dictionary<string, DistroProcess> _runningProcesses = new();
        private static List<WslDistro> _distributions = new();
        private static SynchronizationContext? _uiContext;

        [STAThread]
        static void Main()
        {
            // Single instance check
            _mutex = new Mutex(true, "WSLServiceAgent_Mutex", out bool isNew);
            if (!isNew)
            {
                MessageBox.Show("WSL Service Agent is already running.", "WSL Service Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Capture UI synchronization context
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(_uiContext);

            // Initialize and run
            InitializeTrayIcon();
            Task.Run(InitializeAsync);

            Application.Run();

            _mutex.ReleaseMutex();
        }

        static void InitializeTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = Assets.AppIcon,
                Text = $"WSL Service Agent v{VersionInfo.Version}",
                Visible = true
            };

            _trayIcon.DoubleClick += (s, e) => LaunchDefaultDistro();
            _trayIcon.ContextMenuStrip = BuildMenu();

            Application.ApplicationExit += (s, e) => Cleanup();
        }

        static Config _config = new();

        static async Task InitializeAsync()
        {
            // Load config first - don't do anything if config doesn't exist
            _config = LoadConfig();
            if (_config.EnabledDistros.Count == 0)
            {
                return;
            }

            _distributions = await DiscoverDistributionsAsync();

            if (_distributions.Count == 0)
            {
                _uiContext?.Post(_ =>
                    MessageBox.Show("No WSL distributions found!", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning),
                    null);
                return;
            }

            // Start only configured distributions
            var toStart = _distributions.Where(d => _config.EnabledDistros.Contains(d.Name)).ToList();

            foreach (var distro in toStart)
            {
                await StartDistroAsync(distro);
            }

            // Rebuild menu on UI thread
            RefreshMenu();
        }

        static ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();

            menu.Items.Add(new ToolStripMenuItem("WSL Service Agent") { Enabled = false, Font = new Font(menu.Font, FontStyle.Bold) });
            menu.Items.Add(new ToolStripSeparator());

            // Add distribution items - only show enabled distros from config
            var enabledDistros = _distributions.Where(d => _config.EnabledDistros.Contains(d.Name)).ToList();
            foreach (var distro in enabledDistros)
            {
                bool isRunning = _runningProcesses.ContainsKey(distro.Name);
                var item = new ToolStripMenuItem(distro.Name);

                var statusItem = new ToolStripMenuItem(isRunning ? "Running" : "Stopped")
                {
                    Image = isRunning ? Assets.RunningBitmap : Assets.StoppedBitmap
                };
                statusItem.Click += (s, e) => { }; // No-op to prevent action
                item.DropDownItems.Add(statusItem);
                item.DropDownItems.Add(new ToolStripSeparator());

                var toggleItem = new ToolStripMenuItem(isRunning ? "Stop Agent" : "Start Agent")
                {
                    Image = isRunning ? Assets.StopBitmap : Assets.StartBitmap
                };
                toggleItem.Click += async (s, e) => await ToggleDistroAsync(distro);
                item.DropDownItems.Add(toggleItem);

                var terminalItem = new ToolStripMenuItem("Launch Terminal")
                {
                    Image = Assets.TerminalBitmap
                };
                terminalItem.Click += (s, e) => LaunchTerminal(distro);
                item.DropDownItems.Add(terminalItem);

                menu.Items.Add(item);
            }

            if (enabledDistros.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());

                var startAllItem = new ToolStripMenuItem("Start All Agents")
                {
                    Image = Assets.StartBitmap
                };
                startAllItem.Click += async (s, e) => await StartAllAsync();
                menu.Items.Add(startAllItem);

                var stopAllItem = new ToolStripMenuItem("Stop All Agents")
                {
                    Image = Assets.StopBitmap
                };
                stopAllItem.Click += async (s, e) => await StopAllAsync();
                menu.Items.Add(stopAllItem);
            }

            menu.Items.Add(new ToolStripSeparator());
            var editConfigItem = new ToolStripMenuItem("Edit Config");
            editConfigItem.Click += (s, e) => EditConfig();
            menu.Items.Add(editConfigItem);

            var exitItem = new ToolStripMenuItem("Exit")
            {
                Image = Assets.ExitBitmap
            };
            exitItem.Click += (s, e) => Application.Exit();
            menu.Items.Add(exitItem);

            return menu;
        }

        static async Task<List<WslDistro>> DiscoverDistributionsAsync()
        {
            var distros = new List<WslDistro>();

            try
            {
                // Read from registry
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
                if (key == null) return distros;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    if (!Guid.TryParse(subKeyName, out var guid)) continue;

                    using var distroKey = key.OpenSubKey(subKeyName);
                    if (distroKey == null) continue;

                    var name = distroKey.GetValue("DistributionName") as string;
                    if (string.IsNullOrEmpty(name)) continue;

                    distros.Add(new WslDistro
                    {
                        Name = name,
                        RegistryGuid = guid,
                        Version = Convert.ToInt32(distroKey.GetValue("Version") ?? 2)
                    });
                }

                // Get Windows Terminal GUIDs
                var wtProfiles = await GetWindowsTerminalProfilesAsync();
                foreach (var distro in distros)
                {
                    if (wtProfiles.TryGetValue(distro.RegistryGuid, out var wtGuid))
                    {
                        distro.WindowsTerminalGuid = wtGuid;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error discovering distributions: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return distros;
        }

        static async Task<Dictionary<Guid, Guid>> GetWindowsTerminalProfilesAsync()
        {
            var profiles = new Dictionary<Guid, Guid>();

            try
            {
                var settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json");

                if (!File.Exists(settingsPath)) return profiles;

                var json = await File.ReadAllTextAsync(settingsPath);
                var settings = JObject.Parse(json);
                var profilesList = settings["profiles"]?["list"] as JArray;

                if (profilesList == null) return profiles;

                foreach (var profile in profilesList)
                {
                    var commandline = profile["commandline"]?.ToString();
                    var guidStr = profile["guid"]?.ToString();

                    if (string.IsNullOrEmpty(commandline) || string.IsNullOrEmpty(guidStr)) continue;
                    if (!Guid.TryParse(guidStr, out var profileGuid)) continue;

                    // Extract WSL GUID from commandline
                    var start = commandline.IndexOf('{');
                    var end = commandline.IndexOf('}');
                    if (start >= 0 && end > start)
                    {
                        var wslGuidStr = commandline.Substring(start, end - start + 1);
                        if (Guid.TryParse(wslGuidStr, out var wslGuid))
                        {
                            profiles[wslGuid] = profileGuid;
                        }
                    }
                }
            }
            catch { /* Ignore errors */ }

            return profiles;
        }

        static async Task StartDistroAsync(WslDistro distro)
        {
            if (_runningProcesses.ContainsKey(distro.Name)) return;

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "wsl",
                        Arguments = $"-d {distro.Name} -e sleep infinity",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    }
                };

                if (!process.Start())
                {
                    MessageBox.Show($"Failed to start process for {distro.Name}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var cts = new CancellationTokenSource();
                _runningProcesses[distro.Name] = new DistroProcess { Process = process, CancellationToken = cts };

                ShowNotification($"{distro.Name} Started", $"Agent for {distro.Name} is now running.", ToolTipIcon.Info);

                // Monitor and auto-restart
                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (!cts.Token.IsCancellationRequested)
                        {
                            await process.WaitForExitAsync(cts.Token);
                            if (cts.Token.IsCancellationRequested) break;

                            // Notify about crash and restart
                            ShowNotification($"{distro.Name} Crashed", $"Agent crashed unexpectedly. Restarting in {_config.RestartDelayMs}ms...", ToolTipIcon.Warning);

                            // Restart after delay
                            await Task.Delay(_config.RestartDelayMs, cts.Token);
                            process = Process.Start(process.StartInfo)!;
                            _runningProcesses[distro.Name].Process = process;
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        _uiContext?.Post(_ =>
                            MessageBox.Show($"Error monitoring {distro.Name}: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error),
                            null);
                    }
                }, cts.Token);

                RefreshMenu();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting {distro.Name}: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static async Task StopDistroAsync(WslDistro distro)
        {
            if (!_runningProcesses.TryGetValue(distro.Name, out var distroProcess)) return;

            distroProcess.CancellationToken.Cancel();
            try { distroProcess.Process.Kill(true); } catch { }

            _runningProcesses.Remove(distro.Name);
            ShowNotification($"{distro.Name} Stopped", $"Agent for {distro.Name} has been stopped.", ToolTipIcon.Info);
            RefreshMenu();
        }

        static async Task ToggleDistroAsync(WslDistro distro)
        {
            if (_runningProcesses.ContainsKey(distro.Name))
                await StopDistroAsync(distro);
            else
                await StartDistroAsync(distro);
        }

        static async Task StartAllAsync()
        {
            foreach (var distro in _distributions)
                await StartDistroAsync(distro);
        }

        static async Task StopAllAsync()
        {
            foreach (var distro in _distributions.ToList())
                await StopDistroAsync(distro);
        }

        static void LaunchTerminal(WslDistro distro)
        {
            try
            {
                if (distro.WindowsTerminalGuid != Guid.Empty)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "wt",
                        Arguments = $"-p {{{distro.WindowsTerminalGuid}}}",
                        UseShellExecute = true
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "wsl",
                        Arguments = $"-d \"{distro.Name}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch { }
        }

        static void LaunchDefaultDistro()
        {
            var defaultDistro = _distributions.FirstOrDefault(d => _config.EnabledDistros.Contains(d.Name));
            if (defaultDistro != null) LaunchTerminal(defaultDistro);
        }

        static void RefreshMenu()
        {
            if (_trayIcon == null || _uiContext == null) return;

            _uiContext.Post(_ =>
            {
                _trayIcon.ContextMenuStrip = BuildMenu();

                // Update tray tooltip based on running state
                _trayIcon.Text = _runningProcesses.Count > 0
                    ? $"WSL Service Agent v{VersionInfo.Version} - {_runningProcesses.Count} agent(s) running"
                    : $"WSL Service Agent v{VersionInfo.Version} - No agents running";
            }, null);
        }

        static Config LoadConfig()
        {
            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var config = JObject.Parse(json);
                    var enabled = config["EnabledDistros"] as JArray;
                    var restartDelay = config["RestartDelayMs"]?.Value<int>() ?? 2000;
                    return new Config
                    {
                        EnabledDistros = enabled?.Select(t => t.ToString()).ToList() ?? new(),
                        RestartDelayMs = restartDelay
                    };
                }
            }
            catch { }

            // Default: auto-start all distributions
            return new Config { EnabledDistros = new List<string>(), RestartDelayMs = 2000 };
        }

        static void SaveConfig(Config config)
        {
            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                var json = new JObject
                {
                    ["EnabledDistros"] = new JArray(config.EnabledDistros),
                    ["RestartDelayMs"] = config.RestartDelayMs
                };
                File.WriteAllText(configPath, json.ToString());
            }
            catch { }
        }

        static void ShowNotification(string title, string text, ToolTipIcon icon)
        {
            _uiContext?.Post(_ =>
            {
                _trayIcon?.ShowBalloonTip(3000, title, text, icon);
            }, null);
        }

        static void EditConfig()
        {
            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{configPath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening config: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static void Cleanup()
        {
            foreach (var dp in _runningProcesses.Values)
            {
                dp.CancellationToken.Cancel();
                try { dp.Process.Kill(true); } catch { }
            }
            _trayIcon?.Dispose();
        }
    }

    class WslDistro
    {
        public string Name { get; set; } = "";
        public Guid RegistryGuid { get; set; }
        public Guid WindowsTerminalGuid { get; set; }
        public int Version { get; set; }
    }

    class DistroProcess
    {
        public Process Process { get; set; } = null!;
        public CancellationTokenSource CancellationToken { get; set; } = null!;
    }

    class Config
    {
        public List<string> EnabledDistros { get; set; } = new();
        public int RestartDelayMs { get; set; } = 2000;
    }
}
