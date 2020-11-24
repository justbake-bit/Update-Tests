using System;
using System.Collections.Generic;
using System.IO;
using MHLab.Patch.Core.Admin.Localization;
using MHLab.Patch.Core.Admin.Progresses;
using MHLab.Patch.Core.IO;
using MHLab.Patch.Core.Logging;
using MHLab.Patch.Core.Serializing;
using MHLab.Patch.Core.Versioning;
using Version = System.Version;

namespace MHLab.Patch.Core.Admin
{
    public sealed class AdminPatchContext
    {
        public IVersion VersionFrom { get; set; }
        public IVersion VersionTo { get; set; }

        public string PatchName { get; set; }

        public readonly IAdminSettings Settings;

        private int _compressionLevel;
        public int CompressionLevel
        {
            get => _compressionLevel;

            set
            {
                if (value < 0) _compressionLevel = 0;
                else if (value > 9) _compressionLevel = 9;
                else _compressionLevel = value;
            }
        }

        public IAdminLocalizedMessages LocalizedMessages { get; set; }

        public ILogger Logger { get; set; }
        public ISerializer Serializer { get; set; }
        public IVersionFactory VersionFactory { get; set; }

        private readonly IProgress<BuilderProgress> _progressReporter;
        private BuilderProgress _progress;

        public AdminPatchContext(IAdminSettings settings, IProgress<BuilderProgress> progress)
        {
            Settings = settings;
            _progressReporter = progress;
            VersionFactory = new VersionFactory();
        }

        public void Initialize()
        {
            _progress = new BuilderProgress();

            InitializeDirectories();

            PatchName = string.Format(Settings.PatchFileName, VersionFrom, VersionTo);

            var fromDefinition = GetBuildDefinition(VersionFrom);
            var toDefinition = GetBuildDefinition(VersionTo);

            _progress.TotalSteps = 4 + fromDefinition.Entries.Length + toDefinition.Entries.Length + Math.Max(fromDefinition.Entries.Length, toDefinition.Entries.Length);
        }

        private void InitializeDirectories()
        {
            DirectoriesManager.Create(Settings.GetApplicationFolderPath());
            DirectoriesManager.Create(Settings.GetBuildsFolderPath());
            DirectoriesManager.Create(Settings.GetPatchesFolderPath());
        }

        private BuildDefinition GetBuildDefinition(IVersion version)
        {
            var content = File.ReadAllText(Settings.GetBuildDefinitionPath(version));
            return Serializer.Deserialize<BuildDefinition>(content);
        }

        public List<IVersion> GetVersions()
        {
            if (FilesManager.Exists(Settings.GetBuildsIndexPath()))
            {
                var index = Serializer.Deserialize<BuildsIndex>(File.ReadAllText(Settings.GetBuildsIndexPath()));
                return index.AvailableBuilds;
            }

            return new List<IVersion>();
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
