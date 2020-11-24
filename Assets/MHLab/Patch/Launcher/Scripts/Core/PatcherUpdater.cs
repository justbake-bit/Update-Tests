using System;
using MHLab.Patch.Core.Client.IO;
using MHLab.Patch.Core.IO;
using System.IO;

namespace MHLab.Patch.Core.Client
{
    public sealed class PatcherUpdater : IUpdater
    {
        [Flags]
        private enum FileValidityDifference
        {
            None = 0,
            Size = 1,
            LastWriting = 1 << 1,
            Attributes = 1 << 2
        }

        public IDownloader Downloader;

        private readonly UpdatingContext _context;

        public PatcherUpdater(UpdatingContext context)
        {
            _context = context;
            Downloader = new FileDownloader();
        }

        public void Update()
        {
            if (_context.CurrentUpdaterDefinition == null)
            {
                _context.Logger.Warning("No updater definition found. The Launcher cannot be validated or updated.");
                return;
            }

            _context.Logger.Info("Launcher update started. The update contains {UpdateOperations} operations.", _context.CurrentUpdaterDefinition.Entries.Length);

            FilesManager.DeleteTemporaryDeletingFiles(_context.Settings.RootPath);

            foreach (var updaterDefinitionEntry in _context.CurrentUpdaterDefinition.Entries)
            {
                switch (updaterDefinitionEntry.Operation)
                {
                    case PatchOperation.Added:
                        _context.LogProgress(string.Format(_context.LocalizedMessages.UpdateProcessingNewFile, updaterDefinitionEntry.RelativePath));
                        HandleAddedFile(updaterDefinitionEntry);
                        _context.ReportProgress(string.Format(_context.LocalizedMessages.UpdateProcessedNewFile, updaterDefinitionEntry.RelativePath));
                        continue;
                    case PatchOperation.Deleted:
                        _context.LogProgress(string.Format(_context.LocalizedMessages.UpdateProcessingDeletedFile, updaterDefinitionEntry.RelativePath));
                        HandleDeletedFile(updaterDefinitionEntry);
                        _context.ReportProgress(string.Format(_context.LocalizedMessages.UpdateProcessedDeletedFile, updaterDefinitionEntry.RelativePath));
                        continue;
                    case PatchOperation.ChangedAttributes:
                        _context.LogProgress(string.Format(_context.LocalizedMessages.UpdateProcessingChangedAttributesFile, updaterDefinitionEntry.RelativePath));
                        HandleChangedAttributesFile(updaterDefinitionEntry);
                        _context.ReportProgress(string.Format(_context.LocalizedMessages.UpdateProcessedChangedAttributesFile, updaterDefinitionEntry.RelativePath));
                        continue;
                    case PatchOperation.Updated:
                        _context.LogProgress(string.Format(_context.LocalizedMessages.UpdateProcessingUpdatedFile, updaterDefinitionEntry.RelativePath));
                        HandleUpdatedFile(updaterDefinitionEntry);
                        _context.ReportProgress(string.Format(_context.LocalizedMessages.UpdateProcessedUpdatedFile, updaterDefinitionEntry.RelativePath));
                        continue;
                    case PatchOperation.Unchanged:
                        HandleUnchangedFile(updaterDefinitionEntry);
                        _context.ReportProgress(string.Format(_context.LocalizedMessages.UpdateUnchangedFile, updaterDefinitionEntry.RelativePath));
                        continue;
                }
            }

            _context.Logger.Info("Launcher update completed.");
        }

        public int ProgressRangeAmount()
        {
            return (_context.CurrentUpdaterDefinition != null) ? _context.CurrentUpdaterDefinition.Entries.Length : 0;
        }

        private bool IsValid(UpdaterDefinitionEntry entry, out FileValidityDifference difference)
        {
            var filePath = PathsManager.Combine(_context.Settings.RootPath, entry.RelativePath);
            
            var info = FilesManager.GetFileInfo(filePath);
            difference = FileValidityDifference.None;

            if (info.Size != entry.Size) difference |= FileValidityDifference.Size;
            if (!AreLastWritingsEqual(info.LastWriting, entry.LastWriting)) difference |= FileValidityDifference.LastWriting;
            if (info.Attributes != entry.Attributes) difference |= FileValidityDifference.Attributes;

            return difference == FileValidityDifference.None;
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

        private void HandleAddedFile(UpdaterDefinitionEntry entry)
        {
            var filePath = PathsManager.Combine(_context.Settings.RootPath, entry.RelativePath);

            var difference = FileValidityDifference.None;
            var alreadyExisting = FilesManager.Exists(filePath);
            
            if (alreadyExisting && IsValid(entry, out difference)) return;

            if (difference.HasFlag(FileValidityDifference.Size))
            {
                if (FilesManager.IsFileLocked(filePath))
                {
                    var newFilePath = FilesManager.GetTemporaryDeletingFileName(filePath);
                    FilesManager.Rename(filePath, newFilePath);
                }
                else
                {
                    FilesManager.Delete(filePath);
                }

                Downloader.Download(_context.Settings.GetRemoteUpdaterFileUrl(entry.RelativePath),
                    PathsManager.GetDirectoryPath(filePath));

                EnsureDefinition(entry);

                _context.SetDirtyFlag(entry.RelativePath);
            }
            else
            {
                if (!alreadyExisting)
                {
                    Downloader.Download(_context.Settings.GetRemoteUpdaterFileUrl(entry.RelativePath),
                        PathsManager.GetDirectoryPath(filePath));
                }
                
                if (FilesManager.IsFileLocked(filePath))
                {
                    var newFilePath = FilesManager.GetTemporaryDeletingFileName(filePath);
                    FilesManager.Rename(filePath, newFilePath);
                    FilesManager.Copy(newFilePath, filePath);
                }

                EnsureDefinition(entry);
            }
        }

        private void HandleDeletedFile(UpdaterDefinitionEntry entry)
        {
            var filePath = PathsManager.Combine(_context.Settings.RootPath, entry.RelativePath);

            if (FilesManager.IsFileLocked(filePath))
            {
                var newFilePath = FilesManager.GetTemporaryDeletingFileName(filePath);
                FilesManager.Rename(filePath, newFilePath);
            }
            else
            {
                FilesManager.Delete(filePath);
            }
        }

        private void HandleChangedAttributesFile(UpdaterDefinitionEntry entry)
        {
            var filePath = PathsManager.Combine(_context.Settings.RootPath, entry.RelativePath);

            if (!FilesManager.Exists(filePath))
            {
                Downloader.Download(_context.Settings.GetRemoteUpdaterFileUrl(entry.RelativePath),
                    PathsManager.GetDirectoryPath(filePath));
            }
            else
            {
                if (FilesManager.IsFileLocked(filePath))
                {
                    var newFilePath = FilesManager.GetTemporaryDeletingFileName(filePath);
                    FilesManager.Rename(filePath, newFilePath);
                    FilesManager.Copy(newFilePath, filePath);
                }
            }

            EnsureDefinition(entry);
        }

        private void HandleUpdatedFile(UpdaterDefinitionEntry entry)
        {
            HandleAddedFile(entry);
        }

        private void HandleUnchangedFile(UpdaterDefinitionEntry entry)
        {
            HandleAddedFile(entry);
        }

        private void EnsureDefinition(UpdaterDefinitionEntry entry)
        {
            var filePath = PathsManager.Combine(_context.Settings.RootPath, entry.RelativePath);
            
            File.SetAttributes(filePath, entry.Attributes);
            File.SetLastWriteTimeUtc(filePath, entry.LastWriting);
        }
    }
}
