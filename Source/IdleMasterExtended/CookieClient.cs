using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using IdleMasterExtended.Properties;

namespace IdleMasterExtended
{
    public class CookieClient : WebClient
    {
        internal CookieContainer Cookie = new CookieContainer();

        internal Uri ResponseUri;

        /// <summary>
        /// Instantiates a new <c>CookieClient</c> object.
        /// </summary>
        public CookieClient()
        {
            Cookie = GenerateCookies();
            Encoding = Encoding.UTF8;
        }

        /// <summary>
        /// Method <c>GetHttpAsync</c> will asynchronosly use a HTTP GET request (via <c>WebClient</c>) to the provided parameter <c>url</c>
        /// </summary>
        /// <param name="url">URL to send a GET request to</param>
        /// <param name="count">Number of retries to send a GET request</param>
        /// <returns>The HTML content of the HTTP GET response</returns>
        public static async Task<string> GetHttpAsync(string url, int count = 3)
        {
            while (true)
            {
                var client = new CookieClient();
                var content = string.Empty;

                try
                {
                    content = await client.DownloadStringTaskAsync(url);
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex, "CookieClient -> GetHttpAsync, for url = " + url);
                }

                if (!string.IsNullOrWhiteSpace(content) || count == 0)
                    return content;

                count--;
            }
        }

        /// <summary>
        /// Method <c>IsLogined</c> will attempt to find out if the user has successfully logged in or not.
        /// </summary>
        /// <returns><c>true</c> if there was information available that confirms the login as successful, otherwise <c>false</c></returns>
        public static async Task<bool> IsLogined()
        {
            var response = string.Empty;

            try
            {
                response = await GetHttpAsync(Settings.Default.myProfileURL);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "CookieClient -> IsLogined, for url = " + Settings.Default.myProfileURL);
            }

            if (string.IsNullOrWhiteSpace(response))
            {
                // Steam did not answer (no network, Steam maintenance, rate limit...): the login
                // state is unknown, so keep the saved cookies and let the caller retry later.
                return false;
            }

            var document = new HtmlDocument();
            document.LoadHtml(response);

            // The 'notification_count' contains the Steam profile notifications count. Every logged in user should have it available.
            var notificationCountNode = document.DocumentNode.SelectSingleNode("//span[@class='notification_count']");
            if (notificationCountNode != null && !string.IsNullOrEmpty(notificationCountNode.InnerHtml))
            {
                return true;
            }

            // Steam pages also expose the signed-in viewer as g_steamID = "<SteamID64>" (g_steamID = false when signed out).
            var viewer = Regex.Match(response, @"\bg_steamID\s*=\s*[""'](\d{17})[""']");
            if (viewer.Success && viewer.Groups[1].Value == SteamProfile.GetSteamId())
            {
                return true;
            }

            // Steam answered but the session is not signed in (expired or invalid cookies).
            ClearCookies();
            return false;
        }

        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = base.GetWebRequest(address);
            
            if (request is HttpWebRequest)
                (request as HttpWebRequest).CookieContainer = Cookie;
            
            return request;
        }

        protected override WebResponse GetWebResponse(WebRequest request, System.IAsyncResult result)
        {
            try
            {
                var baseResponse = base.GetWebResponse(request);

                var cookies = (baseResponse as HttpWebResponse).Cookies;

                // Check, if cookie should be deleted. This means that sessionID is now invalid and user has to log in again.
                // Maybe this shoud be done other way (authenticate exception), but because of shared settings and timers in frmMain...
                Cookie loginCookie = cookies["steamLoginSecure"];
                if (loginCookie != null && loginCookie.Value == "deleted")
                {
                    ClearCookies();
                }

                ResponseUri = baseResponse.ResponseUri;
                
                return baseResponse;
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "CookieClient -> WebResponse = " + request);
            }
            
            return null;
        }

        private static void ClearCookies()
        {
            Settings.Default.sessionid = string.Empty;
            Settings.Default.steamLogin = string.Empty;
            Settings.Default.steamLoginSecure = string.Empty;
            Settings.Default.steamparental = string.Empty;
            Settings.Default.steamMachineAuth = string.Empty;
            Settings.Default.steamRememberLogin = string.Empty;
            Settings.Default.myProfileURL = string.Empty;

            Settings.Default.Save();
        }

        private static CookieContainer GenerateCookies()
        {
            var cookies = new CookieContainer();
            var target = new Uri("https://steamcommunity.com");

            cookies.Add(new Cookie("sessionid", Settings.Default.sessionid) { Domain = target.Host });
            cookies.Add(new Cookie("steamLoginSecure", Settings.Default.steamLoginSecure) { Domain = target.Host });
            cookies.Add(new Cookie("steamparental", Settings.Default.steamparental) { Domain = target.Host });
            cookies.Add(new Cookie("steamRememberLogin", Settings.Default.steamRememberLogin) { Domain = target.Host });
            cookies.Add(new Cookie(GetSteamMachineAuthCookieName(), Settings.Default.steamMachineAuth) { Domain = target.Host });

            return cookies;
        }

        private static string GetSteamMachineAuthCookieName()
        {
            if (Settings.Default.steamLoginSecure != null && Settings.Default.steamLoginSecure.Length > 17)
                return string.Format("steamMachineAuth{0}", Settings.Default.steamLoginSecure.Substring(0, 17));

            return "steamMachineAuth";
        }
    }
}
