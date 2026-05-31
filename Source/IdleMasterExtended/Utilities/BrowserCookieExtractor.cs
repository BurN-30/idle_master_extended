using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace IdleMasterExtended.Utilities
{
    /// <summary>Chromium-based browsers we can read the Steam web session from.</summary>
    public enum BrowserType
    {
        Chrome,
        Edge
    }

    /// <summary>A browser cookie, limited to the fields we need.</summary>
    public class BrowserCookie
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Domain { get; set; }
    }

    /// <summary>
    /// Reads the stored Steam session cookies (sessionid + steamLoginSecure) from a
    /// Chromium-based browser by launching it headlessly with the DevTools
    /// remote-debugging endpoint and querying it over the Chrome DevTools Protocol.
    ///
    /// Everything stays local: the cookies are only used to fill the login form, they
    /// are never sent anywhere. The browser must not already be running on the target
    /// profile (Chromium would attach to the existing instance instead of enabling the
    /// debug port), hence <see cref="IsRunning"/> / <see cref="Close"/> below.
    /// </summary>
    public static class BrowserCookieExtractor
    {
        private const int TimeoutSeconds = 30;

        public static string DisplayName(BrowserType browser)
        {
            switch (browser)
            {
                case BrowserType.Chrome: return "Chrome";
                case BrowserType.Edge: return "Edge";
                default: return browser.ToString();
            }
        }

        private static string ProcessName(BrowserType browser)
        {
            switch (browser)
            {
                case BrowserType.Chrome: return "chrome";
                case BrowserType.Edge: return "msedge";
                default: return null;
            }
        }

        /// <summary>Path to the browser executable, or null if it is not installed.</summary>
        public static string GetExecutablePath(BrowserType browser)
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string[] candidates;
            switch (browser)
            {
                case BrowserType.Chrome:
                    candidates = new[]
                    {
                        Path.Combine(programFiles, @"Google\Chrome\Application\chrome.exe"),
                        Path.Combine(programFilesX86, @"Google\Chrome\Application\chrome.exe"),
                        Path.Combine(localAppData, @"Google\Chrome\Application\chrome.exe")
                    };
                    break;
                case BrowserType.Edge:
                    candidates = new[]
                    {
                        Path.Combine(programFilesX86, @"Microsoft\Edge\Application\msedge.exe"),
                        Path.Combine(programFiles, @"Microsoft\Edge\Application\msedge.exe")
                    };
                    break;
                default:
                    return null;
            }

            return candidates.FirstOrDefault(File.Exists);
        }

        private static string GetUserDataDir(BrowserType browser)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            switch (browser)
            {
                case BrowserType.Chrome: return Path.Combine(localAppData, @"Google\Chrome\User Data");
                case BrowserType.Edge: return Path.Combine(localAppData, @"Microsoft\Edge\User Data");
                default: return null;
            }
        }

        /// <summary>Profile directories (e.g. "Default", "Profile 1") that hold a cookie store.</summary>
        public static List<string> GetProfiles(BrowserType browser)
        {
            var profiles = new List<string>();
            var userDataDir = GetUserDataDir(browser);
            if (string.IsNullOrEmpty(userDataDir) || !Directory.Exists(userDataDir))
            {
                return profiles;
            }

            foreach (var dir in Directory.GetDirectories(userDataDir))
            {
                if (File.Exists(Path.Combine(dir, "Network", "Cookies")) ||
                    File.Exists(Path.Combine(dir, "Cookies")))
                {
                    profiles.Add(dir);
                }
            }
            return profiles;
        }

        /// <summary>True when the browser is installed and has at least one profile with cookies.</summary>
        public static bool IsAvailable(BrowserType browser)
        {
            return GetExecutablePath(browser) != null && GetProfiles(browser).Count > 0;
        }

        public static bool IsRunning(BrowserType browser)
        {
            return Process.GetProcessesByName(ProcessName(browser)).Length > 0;
        }

        /// <summary>Closes every window of the given browser (asking nicely first, then forcing).</summary>
        public static void Close(BrowserType browser)
        {
            foreach (var process in Process.GetProcessesByName(ProcessName(browser)))
            {
                try
                {
                    if (!process.CloseMainWindow() || !process.WaitForExit(2000))
                    {
                        process.Kill();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex, "BrowserCookieExtractor.Close");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        /// <summary>
        /// Launches the given profile headlessly and returns its cookies via the DevTools
        /// protocol. Returns an empty list on any failure; the browser process is always
        /// terminated before returning.
        /// </summary>
        public static async Task<List<BrowserCookie>> GetCookiesAsync(BrowserType browser, string profileDirectory, CancellationToken cancellationToken = default(CancellationToken))
        {
            var executable = GetExecutablePath(browser);
            if (executable == null)
            {
                return new List<BrowserCookie>();
            }

            var port = GetFreeTcpPort();
            Process process = null;
            try
            {
                process = StartHeadless(executable, profileDirectory, port);

                var debuggerUrl = await GetWebSocketDebuggerUrlAsync(port, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(debuggerUrl))
                {
                    return new List<BrowserCookie>();
                }

                var response = await SendDevToolsCommandAsync(debuggerUrl, "{\"id\":1,\"method\":\"Storage.getCookies\"}", cancellationToken).ConfigureAwait(false);
                return ParseCookies(response);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "BrowserCookieExtractor.GetCookiesAsync");
                return new List<BrowserCookie>();
            }
            finally
            {
                TryKill(process);
            }
        }

        private static Process StartHeadless(string executable, string profileDirectory, int port)
        {
            var userDataDir = Directory.GetParent(profileDirectory).FullName;
            var profileName = Path.GetFileName(profileDirectory);

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = string.Join(" ", new[]
                {
                    "--headless=new",
                    "--disable-gpu",
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--remote-debugging-port=" + port,
                    "--remote-allow-origins=*",
                    "--user-data-dir=\"" + userDataDir + "\"",
                    "--profile-directory=\"" + profileName + "\"",
                    "about:blank"
                })
            };

            return Process.Start(startInfo);
        }

        /// <summary>Polls the DevTools HTTP endpoint until it returns the browser WebSocket URL.</summary>
        private static async Task<string> GetWebSocketDebuggerUrlAsync(int port, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddSeconds(TimeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using (var client = new WebClient())
                    {
                        // Use 127.0.0.1 explicitly: "localhost" can resolve to IPv6 (::1) while the
                        // DevTools endpoint only listens on IPv4, which silently fails to connect.
                        var json = await client.DownloadStringTaskAsync("http://127.0.0.1:" + port + "/json/version").ConfigureAwait(false);
                        var url = (string)JObject.Parse(json)["webSocketDebuggerUrl"];
                        if (!string.IsNullOrEmpty(url))
                        {
                            return url;
                        }
                    }
                }
                catch
                {
                    // Endpoint not ready yet; wait and retry.
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
            }
            return null;
        }

        /// <summary>Sends a single CDP command and returns the matching response (by id) as JSON.</summary>
        private static async Task<string> SendDevToolsCommandAsync(string debuggerUrl, string command, CancellationToken cancellationToken)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
                using (var socket = new ClientWebSocket())
                {
                    await socket.ConnectAsync(new Uri(debuggerUrl), timeout.Token).ConfigureAwait(false);
                    var payload = Encoding.UTF8.GetBytes(command);
                    await socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);

                    var buffer = new byte[16384];
                    var message = new StringBuilder();
                    while (socket.State == WebSocketState.Open)
                    {
                        var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            break;
                        }

                        message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        if (!result.EndOfMessage)
                        {
                            continue;
                        }

                        var text = message.ToString();
                        message.Clear();

                        // CDP may push events before our reply; keep only the response to id 1.
                        try
                        {
                            var json = JObject.Parse(text);
                            if (json["id"] != null && (int)json["id"] == 1)
                            {
                                await CloseSocketAsync(socket).ConfigureAwait(false);
                                return text;
                            }
                        }
                        catch
                        {
                            // Not the JSON we expect; ignore and keep reading.
                        }
                    }
                    return null;
                }
            }
        }

        private static async Task CloseSocketAsync(ClientWebSocket socket)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Closing is best-effort.
            }
        }

        private static List<BrowserCookie> ParseCookies(string responseJson)
        {
            var cookies = new List<BrowserCookie>();
            if (string.IsNullOrEmpty(responseJson))
            {
                return cookies;
            }

            var array = JObject.Parse(responseJson)["result"]?["cookies"] as JArray;
            if (array == null)
            {
                return cookies;
            }

            foreach (var item in array)
            {
                cookies.Add(new BrowserCookie
                {
                    Name = (string)item["name"],
                    Value = (string)item["value"],
                    Domain = (string)item["domain"]
                });
            }
            return cookies;
        }

        private static int GetFreeTcpPort()
        {
            var usedPorts = new HashSet<int>(
                IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpListeners()
                    .Select(endpoint => endpoint.Port));

            var random = new Random();
            int port;
            do
            {
                port = random.Next(10000, 60000);
            } while (usedPorts.Contains(port));
            return port;
        }

        private static void TryKill(Process process)
        {
            if (process == null)
            {
                return;
            }
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "BrowserCookieExtractor.TryKill");
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}
