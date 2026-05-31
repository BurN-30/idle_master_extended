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
    /// <summary>Chromium-based browsers we can drive for the login window.</summary>
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
    /// Opens a dedicated, visible Chromium window (Chrome or Edge) on the Steam login page so
    /// the user can sign in, then reads the resulting session cookies over the Chrome DevTools
    /// Protocol.
    ///
    /// Why a dedicated profile rather than reading the user's everyday browser profile:
    /// recent Chrome refuses remote-debugging on the default profile AND encrypts its cookies
    /// with App-Bound Encryption, so an existing profile cannot be read by a third-party tool.
    /// A separate --user-data-dir is not subject to the remote-debugging restriction, and
    /// because the sign-in happens live in that session the cookies are readable. The profile
    /// is reused between runs, so the user stays signed in and later logins are instant.
    ///
    /// Everything stays local: the cookies only fill the login form, they are never sent anywhere.
    /// </summary>
    public sealed class SteamLoginSession : IDisposable
    {
        private const string SteamLoginUrl = "https://steamcommunity.com/login/home/?goto=";
        private const int PortTimeoutSeconds = 20;

        private readonly Process _process;
        private readonly string _debuggerUrl;

        private SteamLoginSession(Process process, string debuggerUrl)
        {
            _process = process;
            _debuggerUrl = debuggerUrl;
        }

        /// <summary>The dedicated, reused browser profile (kept so the user stays signed in).</summary>
        private static string ProfileDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IdleMasterExtended", "LoginBrowser");
            }
        }

        public static bool IsBrowserAvailable()
        {
            return GetExecutablePath(BrowserType.Chrome) != null || GetExecutablePath(BrowserType.Edge) != null;
        }

        /// <summary>
        /// Launches the Steam login page in a dedicated browser window and waits for the
        /// DevTools endpoint. Returns null if no supported browser is installed or the
        /// endpoint never comes up.
        /// </summary>
        public static async Task<SteamLoginSession> StartAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            var executable = GetExecutablePath(BrowserType.Chrome) ?? GetExecutablePath(BrowserType.Edge);
            if (executable == null)
            {
                return null;
            }

            var port = GetFreeTcpPort();
            Process process = null;
            try
            {
                Directory.CreateDirectory(ProfileDirectory);
                process = LaunchVisible(executable, ProfileDirectory, port);

                var debuggerUrl = await GetWebSocketDebuggerUrlAsync(port, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(debuggerUrl))
                {
                    TryKill(process);
                    return null;
                }
                return new SteamLoginSession(process, debuggerUrl);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "SteamLoginSession.StartAsync");
                TryKill(process);
                return null;
            }
        }

        /// <summary>Reads the current cookies of the login session.</summary>
        public async Task<List<BrowserCookie>> GetCookiesAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var json = await SendDevToolsCommandAsync(_debuggerUrl, "{\"id\":1,\"method\":\"Storage.getCookies\"}", cancellationToken).ConfigureAwait(false);
                return ParseCookies(json);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "SteamLoginSession.GetCookiesAsync");
                return new List<BrowserCookie>();
            }
        }

        public void Dispose()
        {
            TryKill(_process);
        }

        public static string DisplayName(BrowserType browser)
        {
            switch (browser)
            {
                case BrowserType.Chrome: return "Chrome";
                case BrowserType.Edge: return "Edge";
                default: return browser.ToString();
            }
        }

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

        private static Process LaunchVisible(string executable, string profileDirectory, int port)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                Arguments = string.Join(" ", new[]
                {
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--remote-debugging-port=" + port,
                    "--remote-allow-origins=*",
                    "--user-data-dir=\"" + profileDirectory + "\"",
                    "--new-window",
                    "\"" + SteamLoginUrl + "\""
                })
            };
            return Process.Start(startInfo);
        }

        /// <summary>Polls the DevTools HTTP endpoint (on 127.0.0.1, not "localhost", to avoid IPv6) until it returns the WebSocket URL.</summary>
        private static async Task<string> GetWebSocketDebuggerUrlAsync(int port, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddSeconds(PortTimeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using (var client = new WebClient())
                    {
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
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
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
                            // Not the response we want (CDP event); keep reading.
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
                // Best-effort.
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
                Logger.Exception(ex, "SteamLoginSession.TryKill");
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}
