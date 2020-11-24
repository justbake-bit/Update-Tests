using System;
using System.IO;
using System.Linq;
using MHLab.Patch.Core.Admin.Localization;
using MHLab.Patch.Core.Admin.Progresses;
using MHLab.Patch.Core.IO;
using MHLab.Patch.Core.Logging;
using MHLab.Patch.Core.Serializing;
using MHLab.Patch.Core.Versioning;

namespace MHLab.Patch.Core.Admin
{
    public sealed class AdminPatcherUpdateContext
    {
        public readonly IAdminSettings Settings;
        public IAdminLocalizedMessages LocalizedMessages { get; set; }

        public ILogger Logger { get; set; }
        public ISerializer Serializer { get; set; }
        public IVersionFactory VersionFactory { get; set; }

        public int CompressionLevel;
        public string LauncherArchiveName;

        private readonly IProgress<BuilderProgress> _progressReporter;
        private BuilderProgress _progress;

        public AdminPatcherUpdateContext(IAdminSettings settings, IProgress<BuilderProgress> progress)
        {
            Settings = settings;
            _progressReporter = progress;
            VersionFactory = new VersionFactory();
        }

        public void Initialize()
        {
            _progress = new BuilderProgress();

            InitializeDirectories();

            _progress.TotalSteps = GetCurrentFilesToProcessAmount() + GetCurrentDefinitionAmount() + 2;
        }

        private void InitializeDirectories()
        {
            DirectoriesManager.Create(Settings.GetUpdaterFolderPath());
        }

        public int GetCurrentFilesToProcessAmount()
        {
            return FilesManager.GetFiles(Settings.GetUpdaterFolderPath()).Count(f => !f.EndsWith(Settings.UpdaterIndexFileName));
        }

        private int GetCurrentDefinitionAmount()
        {
            return FilesManager.Exists(Settings.GetUpdaterIndexPath()) 
                ? Serializer.Deserialize<UpdaterDefinition>(File.ReadAllText(Settings.GetUpdaterIndexPath())).Entries.Length 
                : 0;
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
