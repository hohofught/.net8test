using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GeminiWebTranslator
{
    public enum BrowserType { Edge, Chrome }

    public class BrowserProfile
    {
        public string Name { get; set; } = "";
        public string DirectoryName { get; set; } = "";
        public override string ToString() => Name;
    }

    public static class BrowserHelper
    {
        private const int DebugPort = 9222;

        public static string GetPath(BrowserType type)
        {
            if (type == BrowserType.Edge)
            {
                const string suffix = @"Microsoft\Edge\Application\msedge.exe";
                var paths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), suffix),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), suffix),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), suffix)
                };
                return paths.FirstOrDefault(File.Exists) ?? "";
            }
            else // Chrome
            {
                const string suffix = @"Google\Chrome\Application\chrome.exe";
                var paths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), suffix),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), suffix),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), suffix)
                };
                return paths.FirstOrDefault(File.Exists) ?? "";
            }
        }

        public static string GetUserDataDir(BrowserType type)
        {
            if (type == BrowserType.Edge)
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\User Data");
            else
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data");
        }

        public static string GetTempUserDataDir(BrowserType type)
        {
            var folderName = type == BrowserType.Edge ? "GeminiTranslator_DevProfile_Edge" : "GeminiTranslator_DevProfile_Chrome";
            var tempPath = Path.Combine(Path.GetTempPath(), folderName);
            if (!Directory.Exists(tempPath)) Directory.CreateDirectory(tempPath);
            return tempPath;
        }

        public static List<BrowserProfile> GetProfiles(BrowserType type)
        {
            var profiles = new List<BrowserProfile>();
            var userDataDir = GetUserDataDir(type);

            if (!Directory.Exists(userDataDir)) return profiles;

            // 1. "Default" Profile
            var defaultPath = Path.Combine(userDataDir, "Default");
            if (Directory.Exists(defaultPath))
            {
                var name = GetProfileName(userDataDir, "Default");
                profiles.Add(new BrowserProfile { Name = name, DirectoryName = "Default" });
            }

            // 2. "Profile *" Folders
            var profileDirs = Directory.GetDirectories(userDataDir, "Profile *");
            foreach (var dir in profileDirs)
            {
                var dirName = Path.GetFileName(dir);
                var name = GetProfileName(userDataDir, dirName);
                profiles.Add(new BrowserProfile { Name = name, DirectoryName = dirName });
            }

            return profiles;
        }

        private static string GetProfileName(string userDataDir, string profileDirName)
        {
            try
            {
                // Try reading from Local State
                var localStatePath = Path.Combine(userDataDir, "Local State");
                if (File.Exists(localStatePath))
                {
                    var json = File.ReadAllText(localStatePath);
                    var obj = JObject.Parse(json);
                    var name = obj["profile"]?["info_cache"]?[profileDirName]?["name"]?.ToString();
                    if (!string.IsNullOrEmpty(name)) return name;
                }

                // Fallback: Preferences
                var prefPath = Path.Combine(userDataDir, profileDirName, "Preferences");
                if (File.Exists(prefPath))
                {
                    var json = File.ReadAllText(prefPath);
                    var obj = JObject.Parse(json);
                    
                    var accountName = obj["account_info"]?[0]?["full_name"]?.ToString();
                    if (!string.IsNullOrEmpty(accountName)) return accountName;

                    var email = obj["account_info"]?[0]?["email"]?.ToString();
                    if (!string.IsNullOrEmpty(email)) return email;

                    var name = obj["profile"]?["name"]?.ToString();
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            catch { }

            return profileDirName;
        }

        /// <summary>
        /// Check if CDP debug port is accessible (browser running with debug mode)
        /// </summary>
        public static async Task<bool> IsDebugPortAvailableAsync(int port = DebugPort)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(1); // 1초 타임아웃
                var response = await client.GetAsync($"http://localhost:{port}/json/version");
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        /// <summary>
        /// 디버그 포트가 활성화될 때까지 대기
        /// </summary>
        public static async Task<bool> WaitForDebugPortAsync(int timeoutSeconds = 10, int port = DebugPort)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < timeoutSeconds)
            {
                if (await IsDebugPortAvailableAsync(port)) return true;
                await Task.Delay(500);
            }
            return false;
        }

        /// <summary>
        /// Kill all instances of a browser to allow clean restart with debug port
        /// </summary>
        public static void KillBrowser(BrowserType type)
        {
            var processName = type == BrowserType.Edge ? "msedge" : "chrome";
            foreach (var proc in Process.GetProcessesByName(processName))
            {
                try { proc.Kill(); } catch { }
            }
        }

        public static void OpenBrowser(BrowserType type, string? profileDirName, string? customUserDataDir = null, bool incognito = false, int debugPort = DebugPort)
        {
            var path = GetPath(type);
            if (string.IsNullOrEmpty(path)) throw new FileNotFoundException($"{(type == BrowserType.Edge ? "Edge" : "Chrome")}를 찾을 수 없습니다.");

            var userDataDir = customUserDataDir ?? GetUserDataDir(type);
            
            var args = $"--remote-debugging-port={debugPort} --user-data-dir=\"{userDataDir}\" --no-first-run --no-default-browser-check https://gemini.google.com";

            if (!string.IsNullOrEmpty(profileDirName))
            {
                args += $" --profile-directory=\"{profileDirName}\"";
            }
            
            if (incognito)
            {
                args += (type == BrowserType.Edge) ? " -inprivate" : " --incognito";
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = args,
                UseShellExecute = false
            });
        }
    }
}
