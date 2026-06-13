using Digimezzo.Foundation.Core.Utils;
using Digimezzo.Foundation.Core.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Dopamine.Core.IO
{
    public sealed class FileOperations
    {
        public static List<FolderPathInfo> GetValidFolderPaths(long folderId, string directory, string[] validExtensions)
        {
            var folderPaths = new List<FolderPathInfo>();

            try
            {
                var exceptions = new ConcurrentQueue<Exception>();
                var validExtensionSet = new HashSet<string>(validExtensions, StringComparer.OrdinalIgnoreCase);

                TryDirectoryRecursiveGetFolderPaths(folderId, directory, validExtensionSet, folderPaths, exceptions);

                foreach (Exception ex in exceptions)
                {
                    LogClient.Error("Error occurred while getting files recursively. Exception: {0}", ex.Message);
                }
            }
            catch (Exception ex)
            {
                LogClient.Error("Unexpected error occurred while getting folder paths. Exception: {0}", ex.Message);
            }

            return folderPaths;
        }

        private static void TryDirectoryRecursiveGetFolderPaths(long folderId, string path, ISet<string> validExtensions, List<FolderPathInfo> folderPaths, ConcurrentQueue<Exception> exceptions)
        {
            try
            {
                // Process the list of files found in the directory.
                string[] fileEntries = null;

                try
                {
                    fileEntries = Directory.GetFiles(path);
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }

                if (fileEntries != null && fileEntries.Length > 0)
                {
                    foreach (string fileName in fileEntries)
                    {
                        try
                        {
                            if (validExtensions.Contains(Path.GetExtension(fileName)))
                            {
                                folderPaths.Add(new FolderPathInfo(folderId, fileName, FileUtils.DateModifiedTicks(fileName)));
                            }
                        }
                        catch (Exception ex)
                        {
                            exceptions.Enqueue(ex);
                        }
                    }
                }

                // Recurse into subdirectories of this directory. 
                string[] subdirectoryEntries = null;

                try
                {
                    subdirectoryEntries = Directory.GetDirectories(path);
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }

                if (subdirectoryEntries != null && subdirectoryEntries.Length > 0)
                {

                    foreach (string subdirectory in subdirectoryEntries)
                    {
                        try
                        {
                            TryDirectoryRecursiveGetFolderPaths(folderId, subdirectory, validExtensions, folderPaths, exceptions);
                        }
                        catch (Exception ex)
                        {
                            exceptions.Enqueue(ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                exceptions.Enqueue(ex);
            }
        }

        public static bool IsDirectoryContentAccessible(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                return false;
            }

            try
            {
                var watcher = new FileSystemWatcher(directoryPath) { EnableRaisingEvents = true, IncludeSubdirectories = true };
                watcher.Dispose();
                watcher = null;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
