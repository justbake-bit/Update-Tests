using System;
using System.IO;
using MHLab.Patch.Core.Admin.Localization;
using MHLab.Patch.Core.Admin.Progresses;
using MHLab.Patch.Core.IO;
using MHLab.Patch.Core.Logging;
using MHLab.Patch.Core.Serializing;
using MHLab.Patch.Core.Versioning;

namespace MHLab.Patch.Core.Admin
{
    public sealed class AdminBuildContext
    {
        public IVersion BuildVersion { get; set; }

        public readonly IAdminSettings Settings;

        public IAdminLocalizedMessages LocalizedMessages { get; set; }

        public ILogger Logger { get; set; }
        public ISerializer Serializer { get; set; }
        public IVersionFactory VersionFactory { get; set; }

        private readonly IProgress<BuilderProgress> _progressReporter;
        private BuilderProgress _progress;

        public AdminBuildContext(IAdminSettings settings, IProgress<BuilderProgress> progress)
        {
            Settings = settings;
            _progressReporter = progress;
            VersionFactory = new VersionFactory();
        }

        public void Initialize()
        {
            _progress = new BuilderProgress();

            InitializeDirectories();

            _progress.TotalSteps = 4 + ((FilesManager.GetFiles(Settings.GetApplicationFolderPath()).Length * 2) + 1);
        }


        private void InitializeDirectories()
        {
            DirectoriesManager.Create(Settings.GetApplicationFolderPath());
            DirectoriesManager.Create(Settings.GetBuildsFolderPath());
            DirectoriesManager.Create(Settings.GetPatchesFolderPath());
        }

        public IVersion GetLastVersion()
        {
            if (FilesManager.Exists(Settings.GetBuildsIndexPath()))
            {
                var index = Serializer.Deserialize<BuildsIndex>(File.ReadAllText(Settings.GetBuildsIndexPath()));
                return index.GetLast();
            }

            return null;
        }

        public void ReportProgress(string log)
        {
            _progress.CurrentSteps++;

            _progressReporter.Report(new BuilderProgress()
            {
                CurrentSteps = _progress.CurrentSteps,
                StepMessage = log,
                TotalSteps = _progress.TotalSteps
            });
        }

        public void LogProgress(string log)
        {
            _progressReporter.Report(new BuilderProgress()
            {
                CurrentSteps = _progress.CurrentSteps,
                StepMessage = log,
                TotalSteps = _progress.TotalSteps
            });
        }
    }
}
