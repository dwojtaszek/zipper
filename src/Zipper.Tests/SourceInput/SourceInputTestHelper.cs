using System.Diagnostics;

namespace Zipper.Tests;

internal static class SourceInputTestHelper
{
    public static void RunChmod(string path, string mode)
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = "chmod",
            Arguments = $"{mode} {path}",
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Failed to start chmod.");
        p.WaitForExit();
        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"chmod exited with code {p.ExitCode}.");
        }
    }

    public static bool RunningAsRoot()
    {
        if (Environment.OSVersion.Platform == PlatformID.Unix)
        {
            return Environment.GetEnvironmentVariable("HOME") == "/root" || Environment.UserName == "root";
        }

        return false;
    }
}
