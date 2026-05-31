using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using IdleMasterExtended.Properties;
using IdleMasterExtended.Utilities;

namespace IdleMasterExtended
{
    public partial class frmSettingsAdvanced : Form
    {
        public frmSettingsAdvanced()
        {
            InitializeComponent();
        }

        private void btnView_Click(object sender, EventArgs e)
        {
            txtSessionID.PasswordChar = '\0';
            txtSteamLoginSecure.PasswordChar = '\0';
            txtSteamParental.PasswordChar = '\0';

            txtSessionID.Enabled = true;
            txtSteamLoginSecure.Enabled = true;
            txtSteamParental.Enabled = true;

            btnView.Visible = false;
        }

        private void frmSettingsAdvanced_Load(object sender, EventArgs e)
        {
            // Localize Form
            btnUpdate.Text = localization.strings.update;
            btnQuickLogin.Text = localization.strings.quick_login;
            this.Text = localization.strings.auth_data;
            ttHelp.SetToolTip(btnView, localization.strings.cookie_warning);

            // Read settings
            var customTheme = Settings.Default.customTheme;
            var whiteIcons = Settings.Default.whiteIcons;

            // Define colors
            this.BackColor = customTheme ? Settings.Default.colorBgd : Settings.Default.colorBgdOriginal;
            this.ForeColor = customTheme ? Settings.Default.colorTxt : Settings.Default.colorTxtOriginal;

            // Buttons
            FlatStyle buttonStyle = customTheme ? FlatStyle.Flat : FlatStyle.Standard;
            btnView.FlatStyle = btnUpdate.FlatStyle = btnQuickLogin.FlatStyle = buttonStyle;
            btnView.BackColor = btnUpdate.BackColor = btnQuickLogin.BackColor = this.BackColor;
            btnView.ForeColor = btnUpdate.ForeColor = btnQuickLogin.ForeColor = this.ForeColor;
            btnView.Image = customTheme ? Resources.imgView_w : Resources.imgView;

            // Links
            linkLabelWhatIsThis.LinkColor = customTheme ? Color.GhostWhite : Color.Blue;

            if (!string.IsNullOrWhiteSpace(Settings.Default.sessionid))
            {
                txtSessionID.Text = Settings.Default.sessionid;
                txtSessionID.Enabled = false;
            }
            else
            {
                txtSessionID.PasswordChar = '\0';
            }

            if (!string.IsNullOrWhiteSpace(Settings.Default.steamLoginSecure))
            {
                txtSteamLoginSecure.Text = Settings.Default.steamLoginSecure;
                txtSteamLoginSecure.Enabled = false;
            }
            else
            {
                txtSteamLoginSecure.PasswordChar = '\0';
            }

            if (!string.IsNullOrWhiteSpace(Settings.Default.steamparental))
            {
                txtSteamParental.Text = Settings.Default.steamparental;
                txtSteamParental.Enabled = false;
            }
            else
            {
                txtSteamParental.PasswordChar = '\0';
                txtSteamParental.Text = "(typically not required)";
            }

            if (txtSessionID.Enabled && txtSteamLoginSecure.Enabled && txtSteamParental.Enabled)
            {
                btnView.Visible = false;
            }

            btnUpdate.Enabled = false;
        }

        private void txtSessionID_TextChanged(object sender, EventArgs e)
        {
            btnUpdate.Enabled = true;
        }

        private void txtSteamLogin_TextChanged(object sender, EventArgs e)
        {
            btnUpdate.Enabled = true;
        }

        private void txtSteamParental_TextChanged(object sender, EventArgs e)
        {
            btnUpdate.Enabled = true;
        }

        private async Task CheckAndSave()
        {
            try
            {
                Settings.Default.sessionid = txtSessionID.Text.Trim();
                Settings.Default.steamLoginSecure = txtSteamLoginSecure.Text.Trim();
                Settings.Default.myProfileURL = SteamProfile.GetSteamUrl();
                Settings.Default.steamparental = txtSteamParental.Text.Trim();

                // Test if the cookie data is valid
                if (await CookieClient.IsLogined())
                {
                    Settings.Default.Save();
                    Close();
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "frmSettingsAdvanced -> CheckAndSave");
            }

            // Invalid cookie data, reset the form
            btnUpdate.Text = localization.strings.update;
            txtSessionID.Text = "";
            txtSteamLoginSecure.Text = "";
            txtSteamParental.Text = "";

            txtSessionID.PasswordChar = '\0';
            txtSteamLoginSecure.PasswordChar = '\0';
            txtSteamParental.PasswordChar = '\0';

            txtSessionID.Enabled = true;
            txtSteamLoginSecure.Enabled = true;
            txtSteamParental.Enabled = true;

            txtSessionID.Focus();

            MessageBox.Show(localization.strings.validate_failed);

            btnUpdate.Enabled = true;
        }

        private async void btnUpdate_Click(object sender, EventArgs e)
        {
            btnUpdate.Enabled = false;
            txtSessionID.Enabled = false;
            txtSteamLoginSecure.Enabled = false;
            txtSteamParental.Enabled = false;

            btnUpdate.Text = localization.strings.validating;

            await CheckAndSave();
        }

        private async void btnQuickLogin_Click(object sender, EventArgs e)
        {
            SetControlsEnabled(false);
            var caption = btnQuickLogin.Text;
            btnQuickLogin.Text = "...";

            try
            {
                if (await TryFillFromBrowsersAsync())
                {
                    btnUpdate.Text = localization.strings.validating;
                    await CheckAndSave();
                }
                else
                {
                    MessageBox.Show(localization.strings.quick_login_failed);
                }
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "frmSettingsAdvanced -> btnQuickLogin_Click");
                MessageBox.Show(localization.strings.quick_login_failed);
            }
            finally
            {
                btnQuickLogin.Text = caption;
                SetControlsEnabled(true);
            }
        }

        /// <summary>
        /// Looks through the installed Chromium browsers for a stored Steam web session and,
        /// when found, fills the sessionid / steamLoginSecure fields. Returns true on success.
        /// </summary>
        private async Task<bool> TryFillFromBrowsersAsync()
        {
            foreach (var browser in new[] { BrowserType.Chrome, BrowserType.Edge })
            {
                if (!BrowserCookieExtractor.IsAvailable(browser))
                {
                    continue;
                }

                // The browser cannot be running on the target profile, otherwise the headless
                // instance just attaches to it and the debug port never opens.
                if (BrowserCookieExtractor.IsRunning(browser))
                {
                    var confirm = MessageBox.Show(
                        string.Format(localization.strings.quick_login_close_browser, BrowserCookieExtractor.DisplayName(browser)),
                        "Quick Login",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (confirm != DialogResult.Yes)
                    {
                        continue;
                    }
                    BrowserCookieExtractor.Close(browser);
                }

                foreach (var profile in BrowserCookieExtractor.GetProfiles(browser))
                {
                    var cookies = await BrowserCookieExtractor.GetCookiesAsync(browser, profile);
                    if (ApplySteamCookies(cookies))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Fills the form from the Steam cookies in the list. Returns false if none were found.</summary>
        private bool ApplySteamCookies(List<BrowserCookie> cookies)
        {
            var steam = cookies
                .Where(c => !string.IsNullOrEmpty(c.Domain) &&
                            c.Domain.IndexOf("steamcommunity.com", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            var loginSecure = steam.FirstOrDefault(c => c.Name == "steamLoginSecure" && !string.IsNullOrEmpty(c.Value));
            if (loginSecure == null)
            {
                return false;
            }

            txtSteamLoginSecure.Text = loginSecure.Value;

            var sessionId = steam.FirstOrDefault(c => c.Name == "sessionid" && !string.IsNullOrEmpty(c.Value));
            if (sessionId != null)
            {
                txtSessionID.Text = sessionId.Value;
            }
            return true;
        }

        private void SetControlsEnabled(bool enabled)
        {
            btnUpdate.Enabled = enabled;
            btnView.Enabled = enabled;
            btnQuickLogin.Enabled = enabled;
            txtSessionID.Enabled = enabled;
            txtSteamLoginSecure.Enabled = enabled;
            txtSteamParental.Enabled = enabled;
        }

        private void linkLabelWhatIsThis_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://github.com/JonasNilson/idle_master_extended/wiki/Login-methods");
        }
    }
}
