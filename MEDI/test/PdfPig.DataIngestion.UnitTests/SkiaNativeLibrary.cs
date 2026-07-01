#if NET8_0_OR_GREATER
#pragma warning disable CS1591
using System;
using System.IO;
using System.Linq;

namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion.UnitTests;

// Ensures SkiaSharp's linux-x64 native lib is available before tests exercise page rendering.
internal static class SkiaNativeLibrary
{
    private const string LibName = "libSkiaSharp.so";

    public static void EnsureAvailable()
    {
        var target = Path.Combine(AppContext.BaseDirectory, LibName);
        if (File.Exists(target))
        {
            return;
        }

        var nugetRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages", "skiasharp.nativeassets.linux.nodependencies");

        if (!Directory.Exists(nugetRoot))
        {
            return;
        }

        var source = Directory
            .EnumerateFiles(nugetRoot, LibName, SearchOption.AllDirectories)
            .FirstOrDefault(p => p.Contains("linux-x64", StringComparison.Ordinal));

        if (source is not null)
        {
            File.Copy(source, target, overwrite: true);
        }
    }
}
#endif
