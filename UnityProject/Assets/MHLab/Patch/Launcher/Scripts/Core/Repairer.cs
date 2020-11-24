using MHLab.Patch.Core.Client.IO;
using MHLab.Patch.Core.IO;
using MHLab.Patch.Core.Utilities;
using System;
using System.Collections.Generic;
using System.IO;

namespace MHLab.Patch.Core.Client
{
    public sealed class Repairer : IUpdater
    {
        [Flags]
        private enum FileIntegrity
        {
            None,
            Valid,
            NotExisting,
            InvalidSize,
            InvalidLastWriting,
            InvalidAttributes
        }

        public IDownloader Downloader;

        private readonly UpdatingContext _context;

        public Repairer(UpdatingContext context)
        {
            _context = context;

            Downloader = new FileDownloader();
        }

        public void Update()
        {            
            _context.Logger.Info("Repairing process started.");
            var repairedFiles = 0;
            var downloadEntries = new List<DownloadEntry>();

            foreach (var currentEntry in _context.CurrentBuildDefinition.Entries)
            {
                var canSkip = false;
                var integrity = GetFileIntegrity(currentEntry);
                var filePath = PathsManager.Combine(_context.Settings.GetGamePath(), currentEntry.RelativePath);

                if (integrity == FileIntegrity.Valid)
                {
                    canSkip = true;
                }
                else if (integrity == FileIntegrity.InvalidAttributes)
                {
                    HandleInvalidAttributes(currentEntry);
                    canSkip = true;
                }
                else if (integrity == FileIntegrity.InvalidLastWriting || integrity == (FileIntegrity.InvalidLastWriting | FileIntegrity.InvalidAttributes))
                {
                    var isNowValid = HandleInvalidLastWriting(currentEntry);
                    if (isNowValid)
                    {
                        SetDefinition(filePath, currentEntry);
                        canSkip = true;
                    }
                }
                else if (integrity.HasFlag(FileIntegrity.InvalidSize))
                {
                    FilesManager.Delete(currentEntry.RelativePath);
                }

                if (!canSkip)
                {
                    // If I am here, the file cannot be fixed and it does not exist anymore (or never existed)
                    DirectoriesManager.Create(PathsManager.GetDirectoryPath(filePath));

                    var remoteFile = PathsManager.UriCombine(
                        _context.Settings.GetRemoteBuildUrl(_context.CurrentVersion), 
                        _context.Settings.GameFolderName, 
                        currentEntry.RelativePath
                    );
                    var partialRemoteFile = PathsManager.UriCombine(
                        _context.Settings.GetPartialRemoteBuildUrl(_context.CurrentVersion),
                        _context.Settings.GameFolderName,
                        currentEntry.RelativePath
                    );
                    downloadEntries.Add(new DownloadEntry(
                        remoteFile,
                        partialRemoteFile,
                        PathsManager.GetDirectoryPath(filePath), 
                        filePath, 
                        currentEntry)
                    );
                    
                    repairedFiles++;
                }
            }

            Downloader.Download(downloadEntries, (entry) =>
            {
                SetDefinition(entry.DestinationFile, entry.Definition);
                _context.ReportProgress($"Repaired {entry.Definition.RelativePath}");
            });

            _context.Logger.Info("Repairing process completed. Checked {CheckedFiles} files, repaired {RepairedFiles} files, skipped {SkippedFiles} files.",
                _context.CurrentBuildDefinition.Entries.Length,
                repairedFiles,
                _context.CurrentBuildDefinition.Entries.Length - repairedFiles);
        }

        public int ProgressRangeAmount()
        {
            return _context.CurrentBuildDefinition.Entries.Length;
        }

        private FileIntegrity GetFileIntegrity(BuildDefinitionEntry entry)
        {
            foreach (var existingFile in _context.ExistingFiles)
            {
                var existingFilePath = FilesManager.SanitizePath(PathsManager.Combine(_context.Settings.RootPath, existingFile.RelativePath));
                var entryFilePath = FilesManager.SanitizePath(PathsManager.Combine(_context.Settings.GetGamePath(), entry.RelativePath));

                if (existingFilePath == entryFilePath)
                {
                    var integrity = FileIntegrity.None;

                    if (existingFile.Size != entry.Size) integrity |= FileIntegrity.InvalidSize;
                    if (AreLastWritingsEqual(existingFile.LastWriting, entry.LastWriting)) integrity |= FileIntegrity.InvalidLastWriting;
                    if (existingFile.Attributes != entry.Attributes) integrity |= FileIntegrity.InvalidAttributes;

                    if (integrity == FileIntegrity.None) return FileIntegrity.Valid;
                    return integrity;
                }
            }

            return FileIntegrity.NotExisting;
        }

        private FileIntegrity GetRelaxedFileIntegrity(BuildDefinitionEntry entry)
        {
            foreach (var existingFile in _context.ExistingFiles)
            {
                var existingFilePath = FilesManager.SanitizePath(PathsManager.Combine(_context.Settings.RootPath, existingFile.RelativePath));
                var entryFilePath = FilesManager.SanitizePath(PathsManager.Combine(_context.Settings.GetGamePath(), entry.RelativePath));

                if (existingFilePath == entryFilePath)
                {
                    var integrity = FileIntegrity.None;

                    if (existingFile.Size != entry.Size) integrity |= FileIntegrity.InvalidSize;

                    if (integrity == FileIntegrity.None) return FileIntegrity.Valid;
                    return integrity;
                }
            }

            return FileIntegrity.NotExisting;
        }

        private bool AreLastWritingsEqual(DateTime lastWriting1, DateTime lastWriting2)
        {
            if (lastWriting1.Year != lastWriting2.Year) return false;
            if (lastWriting1.Month != lastWriting2.Month) return false;
            if (lastWriting1.Day != lastWriting2.Day) return false;
            if (lastWriting1.Hour != lastWriting2.Hour) return false;
            if (lastWriting1.Minute != lastWriting2.Minute) return false;
            if (lastWriting1.Second != lastWriting2.Second) return false;

            return true;
        }

        private void HandleInvalidAttributes(BuildDefinitionEntry entry)
        {
            var filePath = PathsManager.Combine(_context.Settings.GetGamePath(), entry.RelativePath);
            File.SetAttributes(filePath, entry.Attributes);
        }

        private bool HandleInvalidLastWriting(BuildDefinitionEntry entry)
        {
            var filePath = PathsManager.Combine(_context.Settings.GetGamePath(), entry.RelativePath);
            var hash = Hashing.GetFileHash(filePath);

            if (entry.Hash != hash)
            {
                FilesManager.Delete(filePath);
                return false;
            }
            return true;
        }

        private void SetDefinition(string filePath, BuildDefinitionEntry currentEntry)
        {
            File.SetAttributes(filePath, currentEntry.Attributes);
            File.SetLastWriteTimeUtc(filePath, currentEntry.LastWriting);
        }

        public bool IsRepairNeeded()
        {
            if (_context.IsRepairNeeded()) return true;

            foreach (var currentEntry in _context.CurrentBuildDefinition.Entries)
            {
                var integrity = GetRelaxedFileIntegrity(currentEntry);
                if (integrity != FileIntegrity.Valid)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
