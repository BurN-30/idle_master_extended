using Microsoft.Win32;
using System;
using System.Configuration;
using System.IO;
using System.Windows.Forms;

namespace IdleMasterExtended
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Set the Browser emulation version for embedded browser control
            try
            {
                RegistryKey ie_root = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION");
                RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION", true);
                String programName = Path.GetFileName(Environment.GetCommandLineArgs()[0]);
                key.SetValue(programName, (int)10001, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Program -> Main -> Registry and environment modifications resulted in an exception.");
            }

            Application.ThreadException += (o, a) => Logger.Exception(a.Exception);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            EnsureUserSettingsReadable();

            Application.Run(new frmMain());
        }

        /// <summary>
        /// A user.config left damaged by a crash or a power cut makes every settings read throw:
        /// the main window then loops on errors and the settings cannot even be opened. Set the
        /// damaged file aside (user.config.corrupt) and start again with default settings.
        /// </summary>
        private static void EnsureUserSettingsReadable()
        {
            // 1. Unreadable XML (e.g. a file full of zeros). Checked on a separate Configuration
            //    object: a failure inside ConfigurationManager itself would stay cached for the
            //    whole process, even once the file is gone.
            try
            {
                ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal);
            }
            catch (ConfigurationErrorsException ex)
            {
                SetAsideUserSettings(ex);
            }

            // 2. Well-formed XML with invalid content in the settings section.
            try
            {
                var unused = Properties.Settings.Default.updateNeeded;
            }
            catch (ConfigurationErrorsException ex)
            {
                SetAsideUserSettings(ex);
                Properties.Settings.Default.Reload();
            }
        }

        private static void SetAsideUserSettings(ConfigurationErrorsException ex)
        {
            var file = ex.Filename ?? (ex.InnerException as ConfigurationErrorsException)?.Filename;
            try
            {
                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                {
                    var backup = file + ".corrupt";
                    if (File.Exists(backup))
                    {
                        File.Delete(backup);
                    }
                    File.Move(file, backup);
                }
                Logger.Exception(ex, "Program -> EnsureUserSettingsReadable -> unreadable settings set aside, defaults restored: " + file);
            }
            catch (Exception moveEx)
            {
                Logger.Exception(moveEx, "Program -> EnsureUserSettingsReadable -> could not set aside " + file);
            }
        }
    }
}
