using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Permissions;
using System.Threading.Tasks;
using System.Timers;
using System.Web;
using Newtonsoft.Json;

namespace sdagger_auto_updater
{
    class UpdaterService
    {
        private readonly Timer UpdateTimer;
        private readonly Timer WatchDogTimer;
        private UpdaterConfiguration Config;
        private ServiceLogger Logger;

        private readonly string ChromeDirectory;
        private readonly string MasterDirectory;
        private readonly string RunningDirectory;
        private readonly string ProxyConfigFile;
        private readonly string VersionFile;

        private readonly string ApiBase;
        private readonly string ApiCheckVersion;
        private readonly string ApiGetExtension;


        private string CurrentVersion;

        private FileSystemWatcher CfgWatcher;
        private FileSystemSync ExtensionSync;

        private HttpClient HttpChannel;


        private void StoreVersion() =>
            File.WriteAllText(this.VersionFile, this.CurrentVersion.Trim());

        private string ReadVersion() =>
            File.ReadAllText(this.VersionFile).Trim();

        private void StoreExtension(byte[] Extension)
        {
            string TempFile = Path.GetTempFileName();

            File.WriteAllBytes(
                TempFile,
                Extension);

            ZipArchive Archive = ZipFile.Open(TempFile, ZipArchiveMode.Read);
            Archive.Entries[0].ExtractToFile(
                this.MasterDirectory,
                true);

            File.Delete(TempFile);
        }

        private string InitRoute(string Api)
        {
            UriBuilder Builder = new UriBuilder(this.ApiBase + '/' + Api);
            NameValueCollection QueryCollection = HttpUtility.ParseQueryString(Builder.Query);

            QueryCollection["eId"] = this.Config.ExtensionId;
            QueryCollection["eAuth"] = this.Config.ExtensionPassword;

            Builder.Query = QueryCollection.ToString();

            return Builder.ToString();
        }

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        public UpdaterService(UpdaterConfiguration Config, ServiceLogger Logger)
        {
            string CurrentDirectory = Directory.GetCurrentDirectory();

            this.Config = Config;
            this.Logger = Logger;


            this.HttpChannel = new HttpClient();

            this.ApiBase = Config.UpdaterApiUrl;
            this.ApiCheckVersion = this.InitRoute("last-updated");
            this.ApiGetExtension = this.InitRoute("extension");


            this.ChromeDirectory = CurrentDirectory + "/chrome";
            this.MasterDirectory = CurrentDirectory + "/master-extension";
            this.RunningDirectory = CurrentDirectory + "/running-extension";
            this.ProxyConfigFile = CurrentDirectory + "/proxy.json";
            this.VersionFile = CurrentDirectory + "/version.dat";


            this.CurrentVersion = File.Exists(this.VersionFile) ? ReadVersion() : "0";

            this.ExtensionSync = new FileSystemSync(
                this.MasterDirectory,
                this.RunningDirectory);
            
            this.ExtensionSync.AddBinding("ProxyAddress", "$PROXY_ADDRESS", "/?");
            this.ExtensionSync.AddBinding("ProxyUsername", "$PROXY_USERNAME", "/?");
            this.ExtensionSync.AddBinding("ProxyPassword", "$PROXY_PASSWORD", "/?");
            
            this.ExtensionSync.AddBinding("ExtensionId", "$EXTENSION_ID", "/?");
            this.ExtensionSync.AddBinding("ExtensionPassword", "$EXTENSION_PW", "/?");
            
            this.ExtensionSync.AddBinding("MerchantToken", "$MERCHANT_TOKEN", "/?");
            this.ExtensionSync.AddBinding("ExtensionApi", "$API_BASE", "/?");


            this.UpdateTimer = new Timer(Config.UpdateInterval) { AutoReset = true };
            this.WatchDogTimer = new Timer(Config.WatchDogInterval) { AutoReset = true };


            this.UpdateTimer.Elapsed += ExtensionUpdate;
            this.WatchDogTimer.Elapsed += WatchDogUpdate;

            if (!File.Exists(this.ProxyConfigFile)) {
                string ProxyConfig =
                    "{\n\t\"Address\":\"" + Config.ProxyAddress + "\",\n" +
                    "\t\"Username\":\"" + Config.ProxyUsername + "\",\n" +
                    "\t\"Password\":\"" + Config.ProxyPassword + "\"\n}";

                File.WriteAllText(this.ProxyConfigFile, ProxyConfig);
            }

            this.CfgWatcher = new FileSystemWatcher(Directory.GetCurrentDirectory(), ".json");
            this.CfgWatcher.NotifyFilter |= NotifyFilters.LastWrite;
            this.CfgWatcher.Changed += (s, e) => this.CfgChanged(s, e);
            this.CfgWatcher.EnableRaisingEvents = true;
        }

        private void ReloadExtension()
        {
            try
            {
                this.Logger.WriteLog("Reloading chromium...");

                string ExecutableName = "chrome_" + this.Config.ExtensionId;
                Process[] SDaggerProcesses = Process.GetProcessesByName(ExecutableName);

                foreach (Process SDaggerProcess in SDaggerProcesses)
                    SDaggerProcess.Kill();


            // Sync potential configuration changes to the running directory before reloading
                this.ExtensionSync.SyncChanges(
                    ("ProxyAddress", this.Config.ProxyAddress),
                    ("ProxyUsername", this.Config.ProxyUsername),
                    ("ProxyPassword", this.Config.ProxyPassword),
                    ("ExtensionId", this.Config.ExtensionId),
                    ("ExtensionPassword", this.Config.ExtensionPassword),
                    ("MerchantToken", this.Config.MerchantToken),
                    ("ExtensionApi", this.Config.ExtensionApiUrl));

                Process.Start(
                    this.ChromeDirectory + '/' + ExecutableName + ".exe",
                    "--disable-extension-http-throttling --load-extension=\"" + this.RunningDirectory + "\"");
            }
            catch (Exception Ex)
            {
                this.Logger.WriteLog($"Exception occured while trying to reload chromium:\n{Ex.Message}");
            }
        }

        private async Task<string> GetVersionRemote()
        {
            HttpResponseMessage Response = await this.HttpChannel.GetAsync(this.ApiCheckVersion);
            if (!Response.IsSuccessStatusCode)
                return null;

            return await Response.Content.ReadAsStringAsync();
        }

        private async Task<byte[]> GetExtensionZipRemote()
        {
            HttpResponseMessage Response = await this.HttpChannel.GetAsync(this.ApiGetExtension);
            if (!Response.IsSuccessStatusCode)
                return null;

            return await Response.Content.ReadAsByteArrayAsync();
        }


        private string CheckForUpdate() // Returns null if failure, returns the remote version if success
        {
            try
            {
                string RemoteVersion = this.GetVersionRemote().Result;
                bool UpdateRequired = RemoteVersion == null ? false : RemoteVersion != this.CurrentVersion;

                if (UpdateRequired)
                {
                    this.Logger.WriteLog($"New version detected, upgrading: {this.CurrentVersion} -> {RemoteVersion}");

                    return RemoteVersion;
                }
            }
            catch (Exception Ex)
            {
                this.Logger.WriteLog($"Error occured while fetching remote version number:\n{Ex.Message}");
            }

            return null;
        }

        private bool DownloadExtension()
        {
            try
            {
                byte[] RemoteExtension = this.GetExtensionZipRemote().Result;
                if (RemoteExtension == null)
                    return false;

                this.Logger.WriteLog("Successfully downloaded extension");
                this.StoreExtension(RemoteExtension);

                return true;
            }
            catch (Exception Ex)
            {
                this.Logger.WriteLog($"An error occured while fetching the remote extension:\n{Ex.Message}");
            }

            return false;
        }


        private void ExtensionUpdate(object Sender, ElapsedEventArgs Event)
        {
            this.Logger.WriteLog($"Checking for update, current version: {this.CurrentVersion}");

            string NewVersion = this.CheckForUpdate();
            if (NewVersion != null)
            {
                if (this.DownloadExtension())
                {
                    this.CurrentVersion = NewVersion;
                    this.StoreVersion();

                    this.Logger.WriteLog($"Successfully updated to version {this.CurrentVersion}");
                    this.ReloadExtension();
                }
            }
        }

        private void WatchDogUpdate(object Sender, ElapsedEventArgs Event)
        {
            Process[] RunningChromeProcesses = Process.GetProcessesByName("chrome_" + this.Config.ExtensionId);
            if (RunningChromeProcesses.Length == 0 && Directory.Exists(this.MasterDirectory))
                this.ReloadExtension();
        }

        private void CfgChanged(object Sender, FileSystemEventArgs Event)
        {
            try
            {
                if (Event.Name == "proxy.json")
                {
                    dynamic NewCfg = JsonConvert.DeserializeObject<dynamic>(File.ReadAllText(Event.FullPath));

                    this.Config.ProxyAddress = NewCfg.ProxyAddress;
                    this.Config.ProxyUsername = NewCfg.ProxyUsername;
                    this.Config.ProxyPassword = NewCfg.ProxyPassword;

                    this.Logger.WriteLog("Switched to new proxy: \"" + this.Config.ProxyAddress + "\"");

                    this.ReloadExtension();
                }
            }
            catch (Exception Ex)
            {
                this.Logger.WriteLog($"Exception occured while reloading proxy settings from disk:\n{Ex.Message}");
            }
        }


        public void RunTimers()
        {
            this.UpdateTimer.Start();
            this.WatchDogTimer.Start();
        }

        public void StopTimers()
        {
            this.WatchDogTimer.Stop();
            this.UpdateTimer.Stop();
        }
    }
}
