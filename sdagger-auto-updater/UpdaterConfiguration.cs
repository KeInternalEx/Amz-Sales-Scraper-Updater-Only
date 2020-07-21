using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sdagger_auto_updater
{
    class UpdaterConfiguration
    {
        public string CompanyId;
        public string ExtensionId;
        public string ExtensionPassword;

        public string StoreName;
        public string MerchantToken;

        public int UpdateInterval;
        public int WatchDogInterval;

        public string ProxyAddress;
        public string ProxyUsername;
        public string ProxyPassword;

        public string UpdaterApiUrl;
        public string ExtensionApiUrl;
    }
}
