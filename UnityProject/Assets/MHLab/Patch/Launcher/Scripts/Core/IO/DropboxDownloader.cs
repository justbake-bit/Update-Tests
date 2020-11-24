using MHLab.Patch.Core.Client.IO;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Dropbox.Api;
using MHLab.Patch.Core.IO;

namespace MHLab.Patch.Core.Client.Advanced.IO
{
    public class DropboxDownloader : FileDownloader
    {
        public override void Download(List<DownloadEntry> entries, Action<DownloadEntry> onEntryCompleted)
        {
            entries.Sort((entry1, entry2) =>
            {
                return entry1.Definition.Size.CompareTo(entry2.Definition.Size);
            });

            var queue = new ConcurrentQueue<DownloadEntry>(entries);

            var tasksAmount = Environment.ProcessorCount - 1;
            var tasks = new Task[tasksAmount];

            for (var i = 0; i < tasksAmount; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    using (var client = new DropboxClient(Credentials.Password))
                    {
                        while (queue.TryDequeue(out var entry))
                        {
                            int retriesCount = 0;

                            while (retriesCount < MaxDownloadRetries)
                            {
                                try
                                {
                                    var url = entry.PartialRemoteUrl;
                                    if (!url.StartsWith("/"))
                                        url = "/" + url;

                                    using (var response = client.Files.DownloadAsync(url).Result)
                                    {
                                        FilesManager.Delete(entry.DestinationFile);
                                        File.WriteAllBytes(entry.DestinationFile, response.GetContentAsByteArrayAsync().Result);
                                    }

                                    retriesCount = MaxDownloadRetries;
                                }
                                catch
                                {
                                    retriesCount++;

                                    if (retriesCount >= MaxDownloadRetries)
                                    {
                                        throw new WebException($"All retries have been tried for {entry.RemoteUrl}.");
                                    }

                                    Thread.Sleep(DelayForRetryMilliseconds + (DelayForRetryMilliseconds * retriesCount));
                                }
                            }

                            onEntryCompleted?.Invoke(entry);
                        }
                    }
                });
            }

            Task.WaitAll(tasks);
        }

        public override string DownloadString(DownloadEntry entry)
        {
            using (var client = new DropboxClient(Credentials.Password))
            {
                var url = entry.PartialRemoteUrl;
                if (!url.StartsWith("/"))
                    url = "/" + url;
                using (var response = client.Files.DownloadAsync(url).Result)
                {
                    return response.GetContentAsStringAsync().Result;
                }
            }
        }
    }
}
