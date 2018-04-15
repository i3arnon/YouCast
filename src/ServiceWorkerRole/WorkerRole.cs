using Microsoft.WindowsAzure.ServiceRuntime;
using Service;
using System;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Web;

namespace ServiceWorkerRole
{
    public class WorkerRole : RoleEntryPoint
    {
        private WebServiceHost _webServiceHost;

        public override bool OnStart()
        {
            // Set the maximum number of concurrent connections
            ServicePointManager.DefaultConnectionLimit = 12;

            // For information on handling configuration changes
            // see the MSDN topic at http://go.microsoft.com/fwlink/?LinkId=166357.

            _webServiceHost = new WebServiceHost(typeof(YoutubeFeed));
            _webServiceHost.AddServiceEndpoint(
                typeof(IYoutubeFeed),
                new WebHttpBinding(),
                new Uri($"http://{RoleEnvironment.CurrentRoleInstance.InstanceEndpoints["SyndicationEndpoint"].IPEndpoint}/FeedService"));
            _webServiceHost.Open();

            return base.OnStart();
        }

        public override void OnStop()
        {
            _webServiceHost.Close();

            base.OnStop();
        }
    }
}