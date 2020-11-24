using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using MHLab.Patch.Core.IO;
using MHLab.Patch.Core.Serializing;

namespace MHLab.Patch.Core.Client.IO
{
    public class FileDownloader : IDownloader
    {
        public const int DownloadBlockSize = 1024 * 16;
        public const int MaxDownloadRetries = 10;
        public const int DelayForRetryMilliseconds = 50;

        public NetworkCredential Credentials { get; set; }

        private bool canceled = false;

        private string downloadingTo;

        public event DownloadProgressHandler ProgressChanged;

        private IWebProxy proxy = null;

        public event EventHandler DownloadComplete;

        private void OnDownloadComplete()
        {
            if (this.DownloadComplete != null)
                this.DownloadComplete(this, new EventArgs());
        }

        public void Download(string url)
        {
            Download(url, "");
        }

        public virtual void Download(List<DownloadEntry> entries, Action<DownloadEntry> onEntryCompleted)
        {
            foreach (var downloadEntry in entries)
            {
                Download(downloadEntry.RemoteUrl, downloadEntry.DestinationFolder);
                onEntryCompleted?.Invoke(downloadEntry);
            }
        }

        public virtual void Download(string url, string destFolder)
        {
            this.canceled = false;
       
            string destFileName = Path.GetFileName(url);

            destFolder = destFolder.Replace("file:///", "").Replace("file://", "");
            this.downloadingTo = Path.Combine(destFolder, destFileName);

            DirectoriesManager.Create(destFolder);

            if (!File.Exists(downloadingTo))
            {
                using (FileStream fs = File.Create(downloadingTo))
                {
                    fs.Dispose();
                    fs.Close();
                }
            }

            byte[] buffer = new byte[DownloadBlockSize];

            var gotCanceled = false;

            using (FileStream fs = File.Open(downloadingTo, FileMode.Append, FileAccess.Write,
                FileShare.Write | FileShare.Delete))
            {
                var currentRetries = 0;

                while (currentRetries < MaxDownloadRetries)
                {
                    DownloadData data = null;

                    try
                    {
                        data = DownloadData.Create(url, destFolder, this.proxy, Credentials);
                        var totalDownloaded = data.StartPoint;
                        int readCount;

                        try
                        {
                            while ((int) (readCount = data.DownloadStream.Read(buffer, 0, DownloadBlockSize)) > 0)
                            {
                                if (canceled)
                                {
                                    gotCanceled = true;
                                    data.Close();
                                    break;
                                }

                                totalDownloaded += readCount;

                                SaveToFile(buffer, readCount, fs);

                                if (data.IsProgressKnown)
                                    RaiseProgressChanged(totalDownloaded, data.FileSize);

                                if (canceled)
                                {
                                    gotCanceled = true;
                                    data.Close();
                                    break;
                                }
                            }

                            currentRetries = MaxDownloadRetries;
                        }
                        catch
                        {
                            currentRetries++;

                            if (currentRetries >= MaxDownloadRetries)
                            {
                                fs.Dispose();
                                fs.Close();

                                throw new WebException($"All retries have been tried for {url}.");
                            }

                            Thread.Sleep(DelayForRetryMilliseconds + (DelayForRetryMilliseconds * currentRetries));
                        }
                    }
                    catch (WebException webException)
                    {
                        throw new WebException($"The URL {url} generated an exception.", webException);
                    }
                    catch (UriFormatException e)
                    {
                        throw new ArgumentException(
                            string.Format(
                                "Could not parse the URL \"{0}\" - it's either malformed or is an unknown protocol.",
                                url), e);
                    }
                    finally
                    {
                        if (data != null)
                            data.Close();
                    }
                }

                fs.Dispose();
                fs.Close();
            }

            if (!gotCanceled)
                OnDownloadComplete();
        }

        public virtual T DownloadJson<T>(DownloadEntry entry, ISerializer serializer)
        {
            var content = DownloadString(entry);
            return serializer.Deserialize<T>(content);
        }

        public virtual string DownloadString(DownloadEntry entry)
        {
            ServicePointManager.ServerCertificateValidationCallback = RemoteCertificateValidationCallback;
            using (WebClient client = new WebClient())
            {
                client.Credentials = Credentials;
                try
                {
                    return Encoding.UTF8.GetString(client.DownloadData(entry.RemoteUrl));
                }
                catch (WebException webException)
                {
                    throw new WebException($"The URL {entry.RemoteUrl} generated an exception.", webException);
                }
            }
        }

        private static bool RemoteCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            bool isOk = true;
            // If there are errors in the certificate chain, look at each error to determine the cause.
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                for (int i = 0; i < chain.ChainStatus.Length; i++)
                {
                    if (chain.ChainStatus[i].Status != X509ChainStatusFlags.RevocationStatusUnknown)
                    {
                        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                        chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 1, 0);
                        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
                        bool chainIsValid = chain.Build((X509Certificate2)certificate);
                        if (!chainIsValid)
                        {
                            isOk = false;
                        }
                    }
                }
            }
            return isOk;
        }

        private void SaveToFile(byte[] buffer, int count, FileStream file)
        {
            try
            {
                file.Write(buffer, 0, count);
            }
            catch(Exception e)
            {
                throw new Exception(
                    string.Format("Error trying to save file \"{0}\": {1}", file.Name, e.Message), e);
            }
        }

        private void RaiseProgressChanged(long current, long target)
        {
            if (this.ProgressChanged != null)
                this.ProgressChanged(this, new DownloadEventArgs(target, current));
        }
    }

    class DownloadData
    {
        private WebResponse response;

        private Stream stream;
        private long size;
        private long start;

        private IWebProxy proxy = null;

        public static DownloadData Create(string url, string destFolder, NetworkCredential credentials)
        {
            return Create(url, destFolder, null, credentials);
        }

        public static DownloadData Create(string url, string destFolder, IWebProxy proxy, NetworkCredential credentials)
        {
            DownloadData downloadData = new DownloadData();
            downloadData.proxy = proxy;

            long urlSize = downloadData.GetFileSize(url, credentials);
            downloadData.size = urlSize;

            WebRequest req = downloadData.GetRequest(url, credentials);
            try
            {
                downloadData.response = (WebResponse)req.GetResponse();
            }
            catch (Exception e)
            {
                throw new ArgumentException(String.Format(
                    "Error downloading \"{0}\": {1}", url, e.Message), e);
            }

            ValidateResponse(downloadData.response, url);

            String fileName = System.IO.Path.GetFileName(downloadData.response.ResponseUri.ToString());

            String downloadTo = Path.Combine(destFolder, fileName);

            if (!downloadData.IsProgressKnown && File.Exists(downloadTo))
                File.Delete(downloadTo);

            if (downloadData.IsProgressKnown && File.Exists(downloadTo))
            {
                if (!(downloadData.Response is HttpWebResponse))
                {
                    File.Delete(downloadTo);
                }
                else
                {
                    downloadData.start = new FileInfo(downloadTo).Length;

                    if (downloadData.start > urlSize)
                        File.Delete(downloadTo);
                    else if (downloadData.start < urlSize)
                    {
                        downloadData.response.Close();
                        req = downloadData.GetRequest(url, credentials);
                        ((HttpWebRequest)req).AddRange((int)downloadData.start);
                        downloadData.response = req.GetResponse();

                        if (((HttpWebResponse)downloadData.Response).StatusCode != HttpStatusCode.PartialContent)
                        {
                            File.Delete(downloadTo);
                            downloadData.start = 0;
                        }
                    }
                }
            }
            return downloadData;
        }

        private DownloadData()
        {
        }

        private DownloadData(WebResponse response, long size, long start)
        {
            this.response = response;
            this.size = size;
            this.start = start;
            this.stream = null;
        }

        private static void ValidateResponse(WebResponse response, string url)
        {
            if (response is HttpWebResponse)
            {
                HttpWebResponse httpResponse = (HttpWebResponse)response;
                // If it's an HTML page, it's probably an error page. Comment this
                // out to enable downloading of HTML pages.
                if (httpResponse.ContentType != null)
                {
                    if (/*httpResponse.ContentType.Contains("text/html") ||*/ httpResponse.StatusCode == HttpStatusCode.NotFound)
                    {
                        /*throw new ArgumentException(
                            String.Format("Could not download \"{0}\" - a web page was returned from the web server.",
                            url));*/
                        return;
                    }
                }
            }
            else if (response is FtpWebResponse)
            {
                FtpWebResponse ftpResponse = (FtpWebResponse)response;
                if (ftpResponse.StatusCode == FtpStatusCode.ConnectionClosed)
                    throw new ArgumentException(
                        String.Format("Could not download \"{0}\" - FTP server closed the connection.", url));
            }
        }

        private long GetFileSize(string url, NetworkCredential credentials)
        {
            WebResponse response = null;
            long size = -1;
            try
            {
                var request = GetRequest(url, credentials);

                if (request is FtpWebRequest)
                {
                    var ftpRequest = (FtpWebRequest)request;
                    ftpRequest.Method = WebRequestMethods.Ftp.GetFileSize;
                    request.Proxy = null;
                    response = (FtpWebResponse)ftpRequest.GetResponse();
                    size = response.ContentLength;
                }
                else
                {
                    response = request.GetResponse();
                    size = response.ContentLength;
                }
            }
            finally
            {
                if (response != null)
                    response.Close();
            }

            return size;
        }

        private bool RemoteCertificateValidationCallback(System.Object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            bool isOk = true;
            // If there are errors in the certificate chain, look at each error to determine the cause.
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                for (int i = 0; i < chain.ChainStatus.Length; i++)
                {
                    if (chain.ChainStatus[i].Status != X509ChainStatusFlags.RevocationStatusUnknown)
                    {
                        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                        chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 1, 0);
                        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
                        bool chainIsValid = chain.Build((X509Certificate2)certificate);
                        if (!chainIsValid)
                        {
                            isOk = false;
                        }
                    }
                }
            }
            return isOk;
        }
        
        private WebRequest GetRequest(string url, NetworkCredential credentials)
        {
            if(url == null) throw new ArgumentException("The URL parameter for this WebRequest is empty!", nameof(url));
            ServicePointManager.ServerCertificateValidationCallback = RemoteCertificateValidationCallback;
            WebRequest request = WebRequest.Create(url);
            if (request is HttpWebRequest)
            {
                request.Credentials = credentials;
            }
            if(request is FtpWebRequest)
            {
                request.Credentials = credentials;
            }

            if (this.proxy != null)
            {
                request.Proxy = this.proxy;
            }

            return request;
        }

        public void Close()
        {
            this.response.Close();
        }

#region Properties
        public WebResponse Response
        {
            get { return response; }
            set { response = value; }
        }
        public Stream DownloadStream
        {
            get
            {
                if (this.start == this.size)
                    return Stream.Null;
                if (this.stream == null)
                    this.stream = this.response.GetResponseStream();
                return this.stream;
            }
        }
        public long FileSize
        {
            get
            {
                return this.size;
            }
        }
        public long StartPoint
        {
            get
            {
                return this.start;
            }
        }
        public bool IsProgressKnown
        {
            get
            {
                // If the size of the remote url is -1, that means we
                // couldn't determine it, and so we don't know
                // progress information.
                return this.size > -1;
            }
        }
#endregion
    }

    public class DownloadEventArgs : EventArgs
    {
        private int percentDone;
        private string downloadState;
        private long totalFileSize;

        private TimeSpan timeDiff;
        private long sizeDiff;

        public uint DownloadSpeed
        {
            get
            {
                return (uint)Math.Floor((double)(sizeDiff) / timeDiff.TotalSeconds);
            }
        }

        public long TotalFileSize
        {
            get { return totalFileSize; }
            set { totalFileSize = value; }
        }
        private long currentFileSize;

        public long CurrentFileSize
        {
            get { return currentFileSize; }
            set { currentFileSize = value; }
        }

        public DownloadEventArgs(long totalFileSize, long currentFileSize/*, TimeSpan timeDiff, long sizeDiff*/)
        {
            this.totalFileSize = totalFileSize;
            this.currentFileSize = currentFileSize;

            this.percentDone = (int)((((double)currentFileSize) / totalFileSize) * 100);

            this.sizeDiff = 0;
            /*this.timeDiff = timeDiff;
            this.sizeDiff = sizeDiff;*/
        }

        public DownloadEventArgs(string state)
        {
            this.downloadState = state;
        }

        public DownloadEventArgs(int percentDone, string state)
        {
            this.percentDone = percentDone;
            this.downloadState = state;
        }

        public int PercentDone
        {
            get
            {
                return this.percentDone;
            }
        }

        public string DownloadState
        {
            get
            {
                return this.downloadState;
            }
        }
    }
    public delegate void DownloadProgressHandler(object sender, DownloadEventArgs e);
}