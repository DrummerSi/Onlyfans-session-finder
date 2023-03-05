using RestSharp.Extensions.MonoHttp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Exceptions;
using Titanium.Web.Proxy.Helpers;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;


using Titanium.Web.Proxy.StreamExtended.Network;
namespace OnlyFansRipper.classes
{
    public class ProxyController : IDisposable
    {

        private readonly ProxyServer proxyServer;

        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        private readonly ConcurrentQueue<Tuple<ConsoleColor?, string>> consoleMessageQueue = new ConcurrentQueue<Tuple<ConsoleColor?, string>>();

        private ExplicitProxyEndPoint explicitEndPoint;

        public OFData ofData = new OFData();

        public delegate void StatusUpdateHandler(object sender, OFData data);
        public event StatusUpdateHandler OnUpdateStatus;



        public ProxyController()
        {
            Task.Run(() => ListenToConsole());

            proxyServer = new ProxyServer();

            proxyServer.ExceptionFunc = async exception =>
            {
                if (exception is ProxyHttpException phex)
                    WriteToConsole(exception.Message + ": " + phex.InnerException?.Message, ConsoleColor.Red);
                else
                    WriteToConsole(exception.Message, ConsoleColor.Red);
            };

            proxyServer.TcpTimeWaitSeconds = 10;
            proxyServer.ConnectionTimeOutSeconds = 15;
            proxyServer.ReuseSocket = false;
            proxyServer.EnableConnectionPool = false;
            proxyServer.ForwardToUpstreamGateway = true;
            proxyServer.CertificateManager.SaveFakeCertificates = true;
        }

        private CancellationToken CancellationToken => cancellationTokenSource.Token;

        public void StartProxy()
        {
            proxyServer.BeforeRequest += OnRequest;
            proxyServer.BeforeResponse += OnResponse;
            proxyServer.AfterResponse += OnAfterResponse;

            proxyServer.ServerCertificateValidationCallback += OnCertificateValidation;
            proxyServer.ClientCertificateSelectionCallback += OnCertificateSelection;

            explicitEndPoint = new ExplicitProxyEndPoint(IPAddress.Any, 8000);

            explicitEndPoint.BeforeTunnelConnectRequest += OnBeforeTunnelConnectRequest;
            explicitEndPoint.BeforeTunnelConnectResponse += OnBeforeTunnelConnectResponse;

            proxyServer.AddEndPoint(explicitEndPoint);
            proxyServer.Start();



            foreach (var endPoint in proxyServer.ProxyEndPoints)
                Console.WriteLine("Listening on '{0}' endpoint at IP {1} and port: {2}",
                    endPoint.GetType().Name, endPoint, endPoint.IpAddress, endPoint.Port);

            //if (RunTime.IsWindows) 
            proxyServer.SetAsSystemProxy(explicitEndPoint, ProxyProtocolType.AllHttp);

        }

        public void Stop()
        {
            explicitEndPoint.BeforeTunnelConnectRequest -= OnBeforeTunnelConnectRequest;
            explicitEndPoint.BeforeTunnelConnectResponse -= OnBeforeTunnelConnectResponse;

            proxyServer.BeforeRequest -= OnRequest;
            proxyServer.BeforeResponse -= OnResponse;

            explicitEndPoint.BeforeTunnelConnectRequest -= OnBeforeTunnelConnectRequest;
            explicitEndPoint.BeforeTunnelConnectResponse -= OnBeforeTunnelConnectResponse;

            proxyServer.Stop();
        }

        public void Dispose()
        {
            cancellationTokenSource.Dispose();
            proxyServer.Dispose();
        }



        public Task OnCertificateValidation(object sender, CertificateValidationEventArgs e)
        {
            if (e.SslPolicyErrors == System.Net.Security.SslPolicyErrors.None) e.IsValid = true;
            return Task.CompletedTask;
        }

        public Task OnCertificateSelection(object sender, CertificateSelectionEventArgs e)
        {
            return Task.CompletedTask;
        }


        //INtercept & cancel redirect or update requests
        private async Task OnRequest(object sender, SessionEventArgs e)
        {
            var clientLocalIp = e.ClientLocalEndPoint.Address;
            if (!clientLocalIp.Equals(IPAddress.Loopback) && !clientLocalIp.Equals(IPAddress.IPv6Loopback))
                e.HttpClient.UpStreamEndPoint = new IPEndPoint(clientLocalIp, 0);

            if (e.HttpClient.Request.Url.Contains("yahoo.com"))
                e.CustomUpStreamProxy = new ExternalProxy("localhost", 8888);

            //WriteToConsole("Active Client Connections:" + ((ProxyServer)sender).ClientConnectionCount);
            //WriteToConsole(e.HttpClient.Request.Url);

            var headers = e.HttpClient.Request.Headers.Headers;

            if (e.HttpClient.Request.RequestUri.AbsolutePath == "/api2/v2/users/list")
            {
                //Debug.WriteLine(headers.
                /*foreach(var myHeader in headers)
                {
                    Console.WriteLine(myHeader.Key + " = " + myHeader.Value.ToString());
                }
                Console.WriteLine("---------------------------------");*/

                if (headers.ContainsKey("x-bc") && ofData.xbc == null)
                {
                    HttpHeader xbc;
                    headers.TryGetValue("x-bc", out xbc);
                    ofData.xbc = xbc.Value;
                    WriteToConsole("XBC: " + ofData.xbc, ConsoleColor.Yellow);
                }

                if (headers.ContainsKey("Cookie") && ofData.sess == null)
                {
                    HttpHeader cookie;
                    headers.TryGetValue("Cookie", out cookie);

                    string pattern = @"sess=(.*?);";
                    Match m = Regex.Match(cookie.Value, pattern, RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        ofData.sess = m.Groups[1].Value;
                        WriteToConsole("sess: " + ofData.sess, ConsoleColor.Yellow);
                    }
                }

                if (headers.ContainsKey("user-id") && ofData.userId == null)
                {
                    HttpHeader userID;
                    headers.TryGetValue("user-id", out userID);
                    ofData.userId = userID.Value;
                    WriteToConsole("user-id: " + ofData.userId, ConsoleColor.Yellow);
                }

                if (headers.ContainsKey("User-Agent") && ofData.userAgent == null)
                {
                    HttpHeader userAgent;
                    headers.TryGetValue("User-Agent", out userAgent);
                    ofData.userAgent = userAgent.Value;
                    WriteToConsole("User-agent: " + ofData.userAgent, ConsoleColor.Yellow);
                }

                Debug.WriteLine(ofData);

                if (ofData.IsComplete)
                {
                    if (OnUpdateStatus != null)
                    {
                        OnUpdateStatus(this, ofData);
                    }
                }
            }

            

        }
               

        private async Task OnResponse(object sender, SessionEventArgs e)
        {

            //WriteToConsole("Active Server Connections:" + ((ProxyServer)sender).ServerConnectionCount);

        }

        private async Task OnAfterResponse(object sender, SessionEventArgs e)
        {
            //Nothing yet
            if (e.HttpClient.Request.RequestUri.Host.Contains("onlyfans"))
            {
                //WriteToConsole("FAN RESPONSE");

                //e.HttpClient.Response.Cookies
                var f = e.HttpClient;
            }
        }

        private async Task OnBeforeTunnelConnectRequest(object sender, TunnelConnectSessionEventArgs e)
        {
            var hostname = e.HttpClient.Request.RequestUri.Host;
            //WriteToConsole("Tunnel to: " + hostname);

            var clientLocalIp = e.ClientLocalEndPoint.Address;
            if (!clientLocalIp.Equals(IPAddress.Loopback) && !clientLocalIp.Equals(IPAddress.IPv6Loopback))
                e.HttpClient.UpStreamEndPoint = new IPEndPoint(clientLocalIp, 0);

        }

        private Task OnBeforeTunnelConnectResponse(object sender, TunnelConnectSessionEventArgs e)
        {
            return Task.CompletedTask;
        }

        private void WriteToConsole(string message, ConsoleColor? consoleColor = null)
        {
            consoleMessageQueue.Enqueue(new Tuple<ConsoleColor?, string>(consoleColor, message));
        }

        private async Task ListenToConsole()
        {
            while (!CancellationToken.IsCancellationRequested)
            {

                while (consoleMessageQueue.TryDequeue(out var item))
                {
                    var consoleColor = item.Item1;
                    var message = item.Item2;

                    if (consoleColor.HasValue)
                    {
                        var existing = Console.ForegroundColor;
                        Console.ForegroundColor = consoleColor.Value;
                        Console.WriteLine(message);
                        Console.ForegroundColor = existing;
                    }
                    else
                    {
                        Console.WriteLine(message);
                    }

                    //Reduce CPU usage
                    await Task.Delay(50);
                }
            }
        }


    }

    

    public class OFData
    {
        public string xbc { get; set; }
        public string userId { get; set; }
        public string sess { get; set; }

        public string userAgent { get; set; }

        public bool IsComplete
        {
            get
            {
                return xbc != null && userId != null && sess != null && userAgent != null;
            }
        }
    }

}
