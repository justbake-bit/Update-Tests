using MHLab.Patch.Core.Client.Exceptions;
using MHLab.Patch.Core.Client.IO;
using MHLab.Patch.Core.Compressing;
using MHLab.Patch.Core.IO;
using MHLab.Patch.Core.Utilities;
using System.IO;
using System.Linq;
using MHLab.Patch.Core.Octodiff;

namespace MHLab.Patch.Core.Client
{
    public sealed class Updater : IUpdater
    {
        public IDownloader Downloader;

        private readonly UpdatingContext _context;

        public Updater(UpdatingContext context)
        {
            _context = context;

            Downloader = new FileDownloader();
        }

        public void Update()
        {
            _context.Logger.Info("Update process started.");
            var performedOperations = 0;

            foreach (var patchDefinition in _context.PatchesPath)
            {
                _context.Logger.Info("Applying update {UpdateName} [{UpdateHash}]", $"{patchDefinition.From}_{patchDefinition.To}", patchDefinition.Hash);
                PerformUpdate(patchDefinition);
                performedOperations += patchDefinition.Entries.Count;
            }

            _context.Logger.Info("Update process completed. Applied {AppliedPatches} patches with {PerformedOperations} operations.",
                _context.PatchesPath.Count,
                performedOperations);
        }

        public int ProgressRangeAmount()
        {
            var accumulator = 0;

            foreach (var patchDefinition in _context.PatchesPath)
            {
                accumulator += patchDefinition.Entries.Count;
            }

            return accumulator;
        }

        private void PerformUpdate(PatchDefinition definition)
        {
            _context.LogProgress(string.Format(_context.LocalizedMessages.UpdateDownloadingArchive, definition.From, definition.To));
            DownloadPatch(definition);
            _context.ReportProgress(string.Format(_context.LocalizedMessages.UpdateDownloadedArchive, definition.From, definition.To));

            _context.LogProgress(string.Format(_context.LocalizedMessages.UpdateDecompressingArchive, definition.From, definition.To));
            DecompressPatch(definition);
            _context.ReportProgress(string.Format(_context.LocalizedMessages.UpdateDecompressedArchive, definition.From, definition.To));

            foreach (var definitionEntry in definition.Entries)
            {
                ProcessFile(definition, definitionEntry);
            }

            DirectoriesManager.Delete(_context.Settings.GetTempPath());
        }

        private void DownloadPatch(PatchDefinition definition)
        {
            DirectoriesManager.Create(_context.Settings.GetTempPath());

            var archivePath = _context.Settings.GetDownloadedPatchArchivePath(definition.From, definition.To);
            var leftAttempts = _context.Settings.PatchDownloadAttempts;
            var success = false;

            do
            {
                try
                {
                    Downloader.Download(_context.Settings.GetRemotePatchArchiveUrl(definition.From, definition.To), _context.Settings.GetTempPath());
                    var downloadedArchiveHash = Hashing.GetFileHash(archivePath);
                    if (downloadedArchiveHash == definition.Hash)
                    {
                        success = true;
                        break;
                    }
                }
                catch
                {
                    // ignored
                }

                FilesManager.Delete(archivePath);
                leftAttempts--;
            } while (leftAttempts > 0);

            if (!success) throw new PatchCannotBeDownloadedException();
        }

        private void DecompressPatch(PatchDefinition definition)
        {
            var path = _context.Settings.GetUncompressedPatchArchivePath(definition.From, definition.To);
            DirectoriesManager.Create(path);

            Compressor.Decompress(path, _context.Settings.GetDownloadedPatchArchivePath(definition.From, definition.To), null);
        }

        private void ProcessFile(PatchDefinition definition, PatchDefinitionEntry entry)
        {
            switch (entry.Operation)
            {
                case PatchOperation.Added:
                    _context.LogProgress(string.Format(_context.LocalizedMessages.UpdateProcessingNewFile, entry.RelativePath));
                    HandleAddedFile(definition, entry);
                    _context.ReportProgress(string.Format(_context.LocalizedMessages.UpdateProcessedNewFile, entry.RelativePath));
                    break;
                case PatchOperation.Deleted:
                    _context.LogProgress(string.Format(_context.LocalizedMessages.UpdateProcessingDeletedFile, entry.RelativePath));
                    HandleDeletedFile(definition, entry);
                    _context.ReportProgress(string.Format(_context.LocalizedMessages.UpdateProcessedDeletedFile, entry.RelativePath));
                    break;
                case PatchOperation.Updated:
                    _context.LogProgress(string.Format(_context.LocalizedMessages.UpdateProcessingUpdatedFile, entry.RelativePath));
                    HandleUpdatedFile(definition, entry);
                    _context.ReportProgress(string.Format(_context.LocalizedMessages.UpdateProcessedUpdatedFile, entry.RelativePath));
                    break;
                case PatchOperation.ChangedAttributes:
                    _context.LogProgress(string.Format(_context.LocalizedMessages.UpdateProcessingChangedAttributesFile, entry.RelativePath));
                    HandleChangedAttributesFile(definition, entry);
                    _context.ReportProgress(string.Format(_context.LocalizedMessages.UpdateProcessedChangedAttributesFile, entry.RelativePath));
                    break;
            }
        }

        private void HandleAddedFile(PatchDefinition definition, PatchDefinitionEntry entry)
        {
            var sourcePath = PathsManager.Combine(_context.Settings.GetUncompressedPatchArchivePath(definition.From, definition.To), entry.RelativePath);
            var destinationPath = PathsManager.Combine(_context.Settings.GetGamePath(), entry.RelativePath);
            FilesManager.Delete(destinationPath);
            FilesManager.Move(sourcePath, destinationPath);

            EnsureDefinition(destinationPath, entry);
        }

        private void HandleDeletedFile(PatchDefinition definition, PatchDefinitionEntry entry)
        {
            var path = PathsManager.Combine(_context.Settings.GetGamePath(), entry.RelativePath);
            FilesManager.Delete(path);
        }

        private void HandleUpdatedFile(PatchDefinition definition, PatchDefinitionEntry entry)
        {
            var filePath = PathsManager.Combine(_context.Settings.GetGamePath(), entry.RelativePath);
            var fileBackupPath = filePath + ".bak";
            var patchPath = PathsManager.Combine(_context.Settings.GetUncompressedPatchArchivePath(definition.From, definition.To), entry.RelativePath + ".patch");

            try
            {
                FilesManager.Rename(filePath, fileBackupPath);

                DeltaFileApplier.Apply(fileBackupPath, patchPath, filePath);

                EnsureDefinition(filePath, entry);
            }
            catch
            {

            }
            finally
            {
                FilesManager.Delete(fileBackupPath);
            }
        }

        private void HandleChangedAttributesFile(PatchDefinition definition, PatchDefinitionEntry entry)
        {
            var path = PathsManager.Combine(_context.Settings.GetGamePath(), entry.RelativePath);

            EnsureDefinition(path, entry);
        }

        private void EnsureDefinition(string filePath, PatchDefinitionEntry entry)
        {
            File.SetAttributes(filePath, entry.Attributes);
            File.SetLastWriteTimeUtc(filePath, entry.LastWriting);
        }

        public bool IsUpdateAvailable()
        {
            return _context.PatchesIndex.Patches.Any(p => p.From.Equals(_context.CurrentVersion));
        }
    }
}
