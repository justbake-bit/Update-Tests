using System;
using System.Collections.Generic;
using System.Net;
using MHLab.Patch.Core.Serializing;

namespace MHLab.Patch.Core.Client.IO
{
    public class DownloadEntry
    {
        public string RemoteUrl;
        public string PartialRemoteUrl;
        public string DestinationFolder;
        public string DestinationFile;
        public BuildDefinitionEntry Definition;

        public DownloadEntry(string remoteUrl, string partialRemoteUrl, string destinationFolder, string destinationFile, BuildDefinitionEntry definition)
        {
            RemoteUrl = remoteUrl;
            PartialRemoteUrl = partialRemoteUrl;
            DestinationFolder = destinationFolder;
            DestinationFile = destinationFile;
            Definition = definition;
        }
    }

    public interface IDownloader
    {
        event DownloadProgressHandler ProgressChanged;
        event EventHandler DownloadComplete;

        NetworkCredential Credentials { get; set; }

        void Download(List<DownloadEntry> entries, Action<DownloadEntry> onEntryCompleted);
        void Download(string url, string destinationFolder);
        T DownloadJson<T>(DownloadEntry entry, ISerializer serializer);
        string DownloadString(DownloadEntry entry);
    }
}
