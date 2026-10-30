using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace PC_Optimizer.Services;

public interface IDiskCleanupService
{
    long GetTempFolderSize(string folderPath);
    CleanResult GetTempFolderPreview(string folderPath);
    CleanResult CleanTempFolder(string folderPath);
}

public class CleanResult
{
    public int FilesDeleted { get; set; }
    public long BytesFreed { get; set; }
    public int Errors { get; set; }
    public int SkippedFiles { get; set; }
}

public class DiskCleanupService : IDiskCleanupService
{
    public long GetTempFolderSize(string folderPath)
    {
        return GetTempFolderPreview(folderPath).BytesFreed;
    }

    public CleanResult GetTempFolderPreview(string folderPath)
    {
        var result = new CleanResult();
        foreach (var file in EnumerateFilesWithinTarget(folderPath))
        {
            try { result.BytesFreed += new FileInfo(file).Length; result.FilesDeleted++; } catch { }
        }
        return result;
    }

    public CleanResult CleanTempFolder(string folderPath)
    {
        var result = new CleanResult();
        if (!Directory.Exists(folderPath))
            return result;

        var files = EnumerateFilesWithinTarget(folderPath).ToList();
        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);
                var length = info.Length;
                File.Delete(file);
                result.BytesFreed += length;
                result.FilesDeleted++;
            }
            catch { result.SkippedFiles++; /* Archivos en uso o inaccesibles se omiten. */ }
        }

        foreach (var dir in EnumerateDirectoriesWithinTarget(folderPath).OrderByDescending(p => p.Length))
            try { Directory.Delete(dir, recursive: false); } catch { }

        return result;
    }

    private static IEnumerable<string> EnumerateFilesWithinTarget(string folderPath)
    {
        foreach (var (files, _) in EnumerateTarget(folderPath))
            foreach (var file in files) yield return file;
    }

    private static IEnumerable<string> EnumerateDirectoriesWithinTarget(string folderPath)
    {
        foreach (var (_, directories) in EnumerateTarget(folderPath))
            foreach (var directory in directories) yield return directory;
    }

    private static IEnumerable<(List<string> Files, List<string> Directories)> EnumerateTarget(string folderPath)
    {
        string root;
        try { root = Path.GetFullPath(folderPath); }
        catch { yield break; }
        if (!Directory.Exists(root)) yield break;
        bool rootIsReparsePoint;
        try { rootIsReparsePoint = (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0; }
        catch { yield break; }
        if (rootIsReparsePoint) yield break;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            var files = new List<string>();
            var dirs = new List<string>();
            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                    if (IsInside(root, file)) files.Add(file);
                foreach (var dir in Directory.EnumerateDirectories(current))
                {
                    if (!IsInside(root, dir)) continue;
                    try
                    {
                        if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
                        dirs.Add(dir);
                        pending.Push(dir);
                    }
                    catch { }
                }
            }
            catch { }
            yield return (files, dirs);
        }
    }

    private static bool IsInside(string root, string path)
    {
        try
        {
            var relative = Path.GetRelativePath(root, Path.GetFullPath(path));
            return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !Path.IsPathRooted(relative);
        }
        catch { return false; }
    }
}
