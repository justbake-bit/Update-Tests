using System;
using System.Collections.Generic;
using System.IO;
using MHLab.Patch.Core.Admin.Exceptions;
using MHLab.Patch.Core.IO;
using MHLab.Patch.Core.Utilities;
using MHLab.Patch.Core.Versioning;

namespace MHLab.Patch.Core.Admin
{
    public sealed class BuildBuilder
    {
        private readonly AdminBuildContext _context;

        public BuildBuilder(AdminBuildContext context)
        {
            _context = context;
        }

        public void Build()
        {
            if (_context.BuildVersion == null) throw new ArgumentNullException(nameof(_context.BuildVersion));
            if (BuildExists()) throw new BuildAlreadyExistsException();
            if (ApplicationFolderIsEmpty()) throw new ApplicationFolderIsEmptyException();

            _context.LogProgress(string.Format(_context.LocalizedMessages.NewVersionBuilding, _context.BuildVersion));
            CopyFiles(_context.Settings.GetApplicationFolderPath(), _context.Settings.GetGameFolderPath(_context.BuildVersion));
            _context.ReportProgress(string.Format(_context.LocalizedMessages.NewVersionBuilt, _context.BuildVersion));

            _context.LogProgress(string.Format(_context.LocalizedMessages.VersionFileBuilding));
            BuildVersionFile();
            _context.ReportProgress(string.Format(_context.LocalizedMessages.VersionFileBuilt));

            _context.LogProgress(string.Format(_context.LocalizedMessages.BuildDefinitionBuilding));
            BuildDefinition();
            _context.ReportProgress(string.Format(_context.LocalizedMessages.BuildDefinitionBuilt));

            _context.LogProgress(string.Format(_context.LocalizedMessages.BuildIndexBuilding));
            UpdateBuildIndex();
            _context.ReportProgress(string.Format(_context.LocalizedMessages.BuildCompletedSuccessfully, _context.BuildVersion));
        }

        public int GetCurrentFilesToProcessAmount()
        {
            return FilesManager.GetFiles(_context.Settings.GetApplicationFolderPath()).Length;
        }

        public string GetCurrentFilesToProcessSize()
        {
            var files = FilesManager.GetFilesInfo(_context.Settings.GetApplicationFolderPath());
            long size = 0;

            foreach (var fileInfo in files)
            {
                size += fileInfo.Size;
            }

            return FormatUtility.FormatSizeDecimal(size, 2);
        }

        private bool BuildExists()
        {
            return FilesManager.Exists(_context.Settings.GetBuildDefinitionPath(_context.BuildVersion));
        }

        private bool ApplicationFolderIsEmpty()
        {
            return DirectoriesManager.IsEmpty(_context.Settings.GetApplicationFolderPath());
        }

        private void CopyFiles(string sourceFolder, string destinationFolder)
        {
            var files = FilesManager.GetFiles(sourceFolder);

            foreach (var file in files)
            {
                var newFile = file.Replace(sourceFolder, destinationFolder);

                FilesManager.Copy(file, newFile);

                _context.ReportProgress(string.Format(_context.LocalizedMessages.BuildFileProcessed, PathsManager.GetFilename(file)));
            }
        }

        private void BuildVersionFile()
        {
            var encoded = _context.Serializer.Serialize(_context.BuildVersion);
            var encrypted = Rijndael.Encrypt(encoded, _context.Settings.EncryptionKeyphrase);

            File.WriteAllText(_context.Settings.GetVersionFilePath(_context.BuildVersion), encrypted);
        }

        private void UpdateBuildIndex()
        {
            BuildsIndex index;

            if (FilesManager.Exists(_context.Settings.GetBuildsIndexPath()))
            { 
                index = _context.Serializer.Deserialize<BuildsIndex>(File.ReadAllText(_context.Settings.GetBuildsIndexPath()));
            }
            else
            {
                index = new BuildsIndex();
                index.AvailableBuilds = new List<IVersion>();
            }

            index.AvailableBuilds.Add(_context.BuildVersion);

            File.WriteAllText(_context.Settings.GetBuildsIndexPath(), _context.Serializer.Serialize(index));
        }

        private void BuildDefinition()
        {
            var files = FilesManager.GetFilesInfo(_context.Settings.GetApplicationFolderPath());
            var definitions = new BuildDefinition();
            definitions.Entries = new BuildDefinitionEntry[files.Length + 1];

            for (var i = 0; i < files.Length; i++)
            {
                var file = files[i];
                definitions.Entries[i] = new BuildDefinitionEntry()
                {
                    Attributes = file.Attributes,
                    Hash = Hashing.GetFileHash(PathsManager.Combine(_context.Settings.GetApplicationFolderPath(), file.RelativePath)),
                    LastWriting = file.LastWriting,
                    RelativePath = file.RelativePath,
                    Size = file.Size
                };

                _context.ReportProgress(string.Format(_context.LocalizedMessages.BuildDefinitionProcessed, PathsManager.GetFilename(file.RelativePath)));
            }

            var versionFile = FilesManager.GetFileInfo(_context.Settings.GetVersionFilePath(_context.BuildVersion));
            definitions.Entries[files.Length] = new BuildDefinitionEntry()
            {
                Attributes = versionFile.Attributes,
                Hash = Hashing.GetFileHash(_context.Settings.GetVersionFilePath(_context.BuildVersion)),
                LastWriting = versionFile.LastWriting,
                RelativePath = versionFile.RelativePath,
                Size = versionFile.Size
            };
            _context.ReportProgress(string.Format(_context.LocalizedMessages.BuildDefinitionProcessed, PathsManager.GetFilename(versionFile.RelativePath)));

            File.WriteAllText(_context.Settings.GetBuildDefinitionPath(_context.BuildVersion), _context.Serializer.Serialize(definitions));
        }
    }
}
