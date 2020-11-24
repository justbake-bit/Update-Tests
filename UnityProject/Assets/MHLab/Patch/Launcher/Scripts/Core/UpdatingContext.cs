using MHLab.Patch.Core.Client.Exceptions;
using MHLab.Patch.Core.Client.IO;
using MHLab.Patch.Core.Client.Localization;
using MHLab.Patch.Core.Client.Progresses;
using MHLab.Patch.Core.Client.Runners;
using MHLab.Patch.Core.IO;
using MHLab.Patch.Core.Logging;
using MHLab.Patch.Core.Serializing;
using MHLab.Patch.Core.Utilities;
using MHLab.Patch.Core.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MHLab.Patch.Core.Client
{
    public sealed class UpdatingContext
    {
        public BuildsIndex BuildsIndex { get; set; }
        public PatchIndex PatchesIndex { get; set; }
        public LocalFileInfo[] ExistingFiles { get; set; }
        public IVersion CurrentVersion { get; set; }
        public BuildDefinition CurrentBuildDefinition { get; set; }
        public List<PatchDefinition> PatchesPath { get; set; }
        public UpdaterDefinition CurrentUpdaterDefinition { get; set; }

        public IUpdateRunner Runner { get; set; }

        public readonly ILauncherSettings Settings;

        public IUpdaterLocalizedMessages LocalizedMessages { get; set; }

        public ILogger Logger { get; set; }
        public ISerializer Serializer { get; set; }

        public IDownloader Downloader { get; set; }

        private readonly IProgress<UpdateProgress> _progressReporter;
        private UpdateProgress _progress;

        private bool _isDirty;
        private List<string> _dirtyReasons;
        private bool _isRepairNeeded;

        public UpdatingContext(ILauncherSettings settings, IProgress<UpdateProgress> progress)
        {
            _isDirty = false;
            _dirtyReasons = new List<string>();
            _isRepairNeeded = false;
            Settings = settings;
            _progressReporter = progress;
            PatchesPath = new List<PatchDefinition>();

            Runner = new UpdateRunner();
            Downloader = new FileDownloader();
        }

        public void Initialize()
        {
            Logger.Info("Update context initializing...");
            Logger.Info("Update context points to {RemoteUrl}", Settings.RemoteUrl);
            _progress = new UpdateProgress();

            SetCurrentVersion();

            var cleanedFiles = CleanWorkspace();
            Logger.Info("Workspace cleaned. Removed {CleanedFiles} files", cleanedFiles);

            Task.WaitAll(
                GetUpdaterDefinition(),
                GetBuildsIndex(),
                GetPatchesIndex()
            );

            Task.WaitAll(
                GetLocalFiles(),
                GetBuildDefinition()
            );

            Task.WaitAll(GetPatchesShortestPath());
            
            _progress.TotalSteps = Runner.GetProgressAmount();
            Logger.Info("Update context completed initialization.");
        }

        public IVersion GetLocalVersion()
        {
            if (FilesManager.Exists(Settings.GetVersionFilePath()))
            {
                var encryptedVersion = File.ReadAllText(Settings.GetVersionFilePath());
                var decryptedVersion = Rijndael.Decrypt(encryptedVersion, Settings.EncryptionKeyphrase);
                return Serializer.Deserialize<IVersion>(decryptedVersion);
            }
            else
            {
                return null;
            }
        }

        public void Update()
        {
            Runner.Update();
        }

        public void RegisterUpdateStep(IUpdater updater)
        {
            Runner.RegisterStep(updater);
        }

        private void SetCurrentVersion()
        {
            if (FilesManager.Exists(Settings.GetVersionFilePath()))
            {
                var encryptedVersion = File.ReadAllText(Settings.GetVersionFilePath());
                var decryptedVersion = Rijndael.Decrypt(encryptedVersion, Settings.EncryptionKeyphrase);
                CurrentVersion = Serializer.Deserialize<IVersion>(decryptedVersion);
                Logger.Info("Retrieved current version: {CurrentVersion}", CurrentVersion);
            }
            else
            {
                CurrentVersion = null;
                Logger.Warning("No current version found. A full repair may be required.");
            }
        }

        private int CleanWorkspace()
        {
            return FilesManager.DeleteMultiple(Settings.RootPath, "*.delete_tmp");
        }

        private Task GetUpdaterDefinition()
        {
            return Task.Run(() =>
            {
                try
                {
                    var downloadEntry = new DownloadEntry(
                        Settings.GetRemoteUpdaterIndexUrl(), 
                        Settings.GetRemoteUpdaterIndexUrl().Replace(Settings.RemoteUrl, string.Empty), 
                        null, 
                        null, 
                        null
                    );
                    CurrentUpdaterDefinition =
                        Downloader.DownloadJson<UpdaterDefinition>(downloadEntry, Serializer);
                }
                catch (Exception e)
                {
                    CurrentUpdaterDefinition = null;
                    Logger.Warning("No updater definition found. Problem: {UpdaterDefinitionException}", e);
                }
            });
        }

        private Task GetBuildsIndex()
        {
            return Task.Run(() =>
            {
                try
                {
                    var downloadEntry = new DownloadEntry(
                        Settings.GetRemoteBuildsIndexUrl(),
                        Settings.GetRemoteBuildsIndexUrl().Replace(Settings.RemoteUrl, string.Empty),
                        null,
                        null,
                        null
                    );
                    BuildsIndex = Downloader.DownloadJson<BuildsIndex>(downloadEntry, Serializer);
                }
                catch (Exception e)
                {
                    BuildsIndex = new BuildsIndex()
                    {
                        AvailableBuilds = new List<IVersion>()
                    };
                    Logger.Warning("No builds index found. Problem: {BuildsIndexException}", e);
                }
                
            });
        }

        private Task GetPatchesIndex()
        {
            return Task.Run(() =>
            {
                try
                {
                    var downloadEntry = new DownloadEntry(
                        Settings.GetRemotePatchesIndexUrl(),
                        Settings.GetRemotePatchesIndexUrl().Replace(Settings.RemoteUrl, string.Empty),
                        null,
                        null,
                        null
                    );
                    PatchesIndex = Downloader.DownloadJson<PatchIndex>(downloadEntry, Serializer);
                }
                catch
                {
                    PatchesIndex = new PatchIndex()
                    {
                        Patches = new List<PatchIndexEntry>()
                    };
                    Logger.Warning("No patches index found.");
                }
            });
        }

        private Task GetLocalFiles()
        {
            return Task.Run(() =>
            {
                ExistingFiles = FilesManager.GetFilesInfo(Settings.RootPath);
                Logger.Info("Collected {ExistingFilesAmount} local files.", ExistingFiles.Length);
            });
        }

        private Task GetBuildDefinition()
        {
            return Task.Run(() =>
            {
                if (CurrentVersion == null)
                {
                    CurrentVersion = BuildsIndex.GetLast();
                }

                if (CurrentVersion == null)
                {
                    Logger.Error(null, "Cannot retrieve any new version...");
                    throw new NoAvailableBuildsException();
                }

                if (!BuildsIndex.Contains(CurrentVersion) && CurrentVersion.IsLower(BuildsIndex.GetFirst()))
                {
                    CurrentVersion = BuildsIndex.GetLast();
                    SetRepairNeeded();
                }

                try
                {
                    var downloadEntry = new DownloadEntry(
                        Settings.GetRemoteBuildDefinitionUrl(CurrentVersion),
                        Settings.GetRemoteBuildDefinitionUrl(CurrentVersion).Replace(Settings.RemoteUrl, string.Empty),
                        null,
                        null,
                        null
                    );
                    CurrentBuildDefinition =
                        Downloader.DownloadJson<BuildDefinition>(downloadEntry, Serializer);
                    Logger.Info("Retrieved definition for {CurrentVersion}", CurrentVersion);
                }
                catch
                {
                    CurrentBuildDefinition = new BuildDefinition()
                    {
                        Entries = new BuildDefinitionEntry[0]
                    };
                    Logger.Warning("Cannot retrieve the build definition for {CurrentVersion}", CurrentVersion);
                }
            });
        }

        private Task GetPatchesShortestPath()
        {
            return Task.Run(() =>
            {
                var currentVersion = CurrentVersion;
                List<PatchIndexEntry> compatiblePatches = null;
                do
                {
                    var version = currentVersion;
                    compatiblePatches = PatchesIndex.Patches.Where(p => p.From.Equals(version)).ToList();
                    if (compatiblePatches.Count == 0) continue;

                    var longestJumpPatch = compatiblePatches.OrderBy(p => p.To).Last();
                    var downloadEntry = new DownloadEntry(
                        Settings.GetRemotePatchDefinitionUrl(longestJumpPatch.From, longestJumpPatch.To),
                        Settings.GetRemotePatchDefinitionUrl(longestJumpPatch.From, longestJumpPatch.To).Replace(Settings.RemoteUrl, string.Empty),
                        null,
                        null,
                        null
                    );
                    PatchesPath.Add(Downloader.DownloadJson<PatchDefinition>(downloadEntry, Serializer));
                    currentVersion = longestJumpPatch.To;
                } while (compatiblePatches.Count > 0);

                Logger.Info("Found {ApplicablePatchesAmount} applicable updates.", PatchesPath.Count);
            });
        }

        public void ReportProgress(string log)
        {
            _progress.IncrementStep();

            _progressReporter.Report(new UpdateProgress()
            {
                CurrentSteps = _progress.CurrentSteps,
                StepMessage = log,
                TotalSteps = _progress.TotalSteps
            });
        }

        public void LogProgress(string log)
        {
            _progressReporter.Report(new UpdateProgress()
            {
                CurrentSteps = _progress.CurrentSteps,
                StepMessage = log,
                TotalSteps = _progress.TotalSteps
            });
        }

        public void SetDirtyFlag(string reason)
        {
            _dirtyReasons.Add(reason);
            _isDirty = true;
        }

        public bool IsDirty(out List<string> reasons)
        {
            reasons = _dirtyReasons;
            return _isDirty;
        }

        public void SetRepairNeeded()
        {
            _isRepairNeeded = true;
        }

        public bool IsRepairNeeded()
        {
            return _isRepairNeeded;
        }
    }
}
