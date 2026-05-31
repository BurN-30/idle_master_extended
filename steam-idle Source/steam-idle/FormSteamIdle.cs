using System;
using System.Windows.Forms;

namespace steam_idle
{
    public partial class FormSteamIdle : Form
    {
        public FormSteamIdle(long appid)
        {
            InitializeComponent();
            try
            {
                picApp.Load($"https://cdn.akamai.steamstatic.com/steam/apps/{appid}/header_292x136.jpg");
            }
            catch
            {
                // A 404 or network error on the banner must not crash the idle process.
                picApp.Visible = false;
            }
        }

        private void FormSteamIdle_Load(object sender, EventArgs e)
        {

        }
    }
}
