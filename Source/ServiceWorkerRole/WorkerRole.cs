using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Service;

namespace ServiceWorkerRole
{
    public class WorkerRole : RoleEntryPoint
    {
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly ManualResetEvent runCompleteEvent = new ManualResetEvent(false);

        private WebServiceHost _webServiceHost;
        public override void Run()
        {
            Trace.TraceInformation("ServiceWorkerRole is running");

            try
            {
                this.RunAsync(this.cancellationTokenSource.Token).Wait();
            }
            finally
            {
                this.runCompleteEvent.Set();
            }
        }

        public override bool OnStart()
        {
            // Set the maximum number of concurrent connections
            ServicePointManager.DefaultConnectionLimit = 12;

            // For information on handling configuration changes
            // see the MSDN topic at http://go.microsoft.com/fwlink/?LinkId=166357.

            var externalEndPoint = RoleEnvironment.CurrentRoleInstance.InstanceEndpoints["SyndicationEndpoint"];

            _webServiceHost = new WebServiceHost(typeof(YoutubeFeed));
            _webServiceHost.AddServiceEndpoint(typeof(IYoutubeFeed), new WebHttpBinding(), new Uri(String.Format("http://{0}/FeedService",
                externalEndPoint.IPEndpoint)));
            _webServiceHost.Open();

            bool result = base.OnStart();

            Trace.TraceInformation("ServiceWorkerRole has been started");

            return result;
        }

        public override void OnStop()
        {
            Trace.TraceInformation("ServiceWorkerRole is stopping");

            this.cancellationTokenSource.Cancel();
            this.runCompleteEvent.WaitOne();

            _webServiceHost.Close();

            base.OnStop();

            Trace.TraceInformation("ServiceWorkerRole has stopped");
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            // TODO: Replace the following with your own logic.
            while (!cancellationToken.IsCancellationRequested)
            {
                Trace.TraceInformation("Working");
                await Task.Delay(1000);
            }
        }
    }
}
