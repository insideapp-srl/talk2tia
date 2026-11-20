using System;
using System.IO;

namespace Talk2Tia.Core.Openness;

/// <summary>Temporary working directory for export/import round-trips. Deleted on dispose.</summary>
internal sealed class Workspace : IDisposable
{
    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Talk2Tia", "work");

    public DirectoryInfo Directory { get; }

    public Workspace()
    {
        Directory = new DirectoryInfo(Path.Combine(Root, Guid.NewGuid().ToString("N")));
        Directory.Create();
    }

    public string PathOf(string fileName) => Path.Combine(Directory.FullName, fileName);

    public FileInfo FileOf(string fileName) => new(PathOf(fileName));

    public void Dispose()
    {
        try
        {
            Directory.Refresh();
            if (Directory.Exists)
            {
                Directory.Delete(recursive: true);
            }
        }
        catch (IOException ex)
        {
            // A file may still be locked by TIA Portal; the folder is reused-safe and cleaned on next start.
            Console.Error.WriteLine($"[talk2tia] could not delete workspace {Directory.FullName}: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"[talk2tia] could not delete workspace {Directory.FullName}: {ex.Message}");
        }
    }

    /// <summary>Removes leftovers from previous runs.</summary>
    public static void CleanupStale()
    {
        var root = new DirectoryInfo(Root);
        if (!root.Exists)
        {
            return;
        }

        foreach (var dir in root.GetDirectories())
        {
            try
            {
                if (dir.CreationTimeUtc < DateTime.UtcNow.AddHours(-12))
                {
                    dir.Delete(recursive: true);
                }
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"[talk2tia] stale workspace cleanup skipped {dir.Name}: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"[talk2tia] stale workspace cleanup skipped {dir.Name}: {ex.Message}");
            }
        }
    }
}
