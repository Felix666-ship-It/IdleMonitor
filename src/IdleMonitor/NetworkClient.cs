using System;
using System.Net;
using System.Text;
using System.Threading;

namespace IdleMonitor
{
    public sealed class NetworkClient : IDisposable
    {
        private readonly ManualResetEvent _stopEvent = new ManualResetEvent(false);
        private readonly string _apiToken;
        private const int TimeoutMilliseconds = 10000;
        private const int MaxAttempts = 3;

        public NetworkClient(string apiToken = null)
        {
            _apiToken = apiToken ?? string.Empty;
        }

        public string PostJson(string url, string json)
        {
            return Execute(url, (client, normalized) => client.UploadString(normalized, "POST", json));
        }

        public string Get(string url)
        {
            return Execute(url, (client, normalized) => client.DownloadString(normalized));
        }

        private string Execute(string url, Func<WebClient, string, string> operation)
        {
            string normalized;
            string error;
            if (!ApiUrlValidator.TryValidate(url, out normalized, out error))
                throw new InvalidOperationException(error);

            Exception last = null;
            for (int attempt = 1; attempt <= MaxAttempts && !_stopEvent.WaitOne(0); attempt++)
            {
                try
                {
                    using (var client = new TimeoutWebClient(TimeoutMilliseconds))
                    {
                        client.Headers[HttpRequestHeader.UserAgent] = "IdleMonitor/1.1";
                        client.Headers[HttpRequestHeader.ContentType] = "application/json";
                        if (!string.IsNullOrWhiteSpace(_apiToken))
                            client.Headers[HttpRequestHeader.Authorization] = "Bearer " + _apiToken;
                        client.Encoding = Encoding.UTF8;
                        return operation(client, normalized);
                    }
                }
                catch (WebException ex)
                {
                    last = ex;
                    HttpWebResponse response = ex.Response as HttpWebResponse;
                    if (response != null && ((int)response.StatusCode < 500 && response.StatusCode != HttpStatusCode.RequestTimeout && response.StatusCode != (HttpStatusCode)429))
                        break;
                    if (attempt < MaxAttempts) _stopEvent.WaitOne((int)Math.Pow(2, attempt - 1) * 1000);
                }
                catch (Exception ex)
                {
                    last = ex;
                    break;
                }
            }
            throw last ?? new OperationCanceledException("网络请求已取消");
        }

        public void Dispose() { _stopEvent.Set(); _stopEvent.Dispose(); }

        private sealed class TimeoutWebClient : WebClient
        {
            private readonly int _timeout;
            public TimeoutWebClient(int timeout) { _timeout = timeout; }
            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                request.Timeout = _timeout;
                HttpWebRequest http = request as HttpWebRequest;
                if (http != null) http.ReadWriteTimeout = _timeout;
                return request;
            }
        }
    }
}
