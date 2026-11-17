using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace DotNetHttpFile.Tests.Support;

/// <summary>
/// Takes away the test's own access to a file or folder until the returned object is disposed.
/// </summary>
internal static class WithheldAccess
{
    /// <summary>
    /// Makes the file at <paramref name="path"/> unreadable. Windows refuses a second open of a file held without
    /// sharing; elsewhere the file loses its read permission.
    /// </summary>
    public static IDisposable ToRead(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        File.SetUnixFileMode(path, UnixFileMode.None);

        return new MemoryStream();
    }

    /// <summary>
    /// Makes the folder at <paramref name="path"/> impossible to list, while what it holds stays reachable. Windows
    /// gets a rule that denies listing; elsewhere the folder keeps only its search permission.
    /// </summary>
    public static IDisposable ToList(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.SkipWhen(Environment.IsPrivilegedProcess, "A privileged process lists a folder whatever its mode.");

            return new WithheldUnixMode(path, UnixFileMode.UserExecute);
        }

        DeniedListing denied = new(path);
        if (CanList(path))
        {
            // A process that holds the backup privilege is not held back by the rule.
            denied.Dispose();
            Assert.Skip("This process lists a folder whatever its access rules.");
        }

        return denied;
    }

    private static bool CanList(string path)
    {
        try
        {
            Directory.GetFileSystemEntries(path);

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    [UnsupportedOSPlatform("windows")]
    private sealed class WithheldUnixMode : IDisposable
    {
        private readonly string _path;
        private readonly UnixFileMode _mode;

        public WithheldUnixMode(string path, UnixFileMode mode)
        {
            _path = path;
            _mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, mode);
        }

        public void Dispose() => File.SetUnixFileMode(_path, _mode);
    }

    [SupportedOSPlatform("windows")]
    private sealed class DeniedListing : IDisposable
    {
        private readonly DirectoryInfo _directory;
        private readonly FileSystemAccessRule _rule;

        public DeniedListing(string path)
        {
            SecurityIdentifier user = WindowsIdentity.GetCurrent().User
                ?? throw new InvalidOperationException("The test process has no user to deny.");
            _directory = new DirectoryInfo(path);
            _rule = new FileSystemAccessRule(user, FileSystemRights.ListDirectory, AccessControlType.Deny);

            DirectorySecurity security = _directory.GetAccessControl();
            security.AddAccessRule(_rule);
            _directory.SetAccessControl(security);
        }

        public void Dispose()
        {
            DirectorySecurity security = _directory.GetAccessControl();
            security.RemoveAccessRule(_rule);
            _directory.SetAccessControl(security);
        }
    }
}
