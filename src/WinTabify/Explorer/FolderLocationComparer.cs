using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinTabify.Explorer;

internal sealed class FolderLocationComparer : IEqualityComparer<string>
{
    internal static readonly FolderLocationComparer Instance = new();

    public bool Equals(string? left, string? right)
    {
        if (StringComparer.Ordinal.Equals(left, right)) return true;
        if (left == null || right == null || !Path.IsPathFullyQualified(left) ||
            !StringComparer.OrdinalIgnoreCase.Equals(left, right)) return false;
        return TryGetIdentity(left, out var first) && TryGetIdentity(right, out var second) && first == second;
    }

    public int GetHashCode(string path) => StringComparer.OrdinalIgnoreCase.GetHashCode(path);

    private static bool TryGetIdentity(string path, out FileId id)
    {
        using var handle = CreateFile(path, 0, FileShare.ReadWrite | FileShare.Delete, 0, FileMode.Open, 0x02000000, 0);
        id = default;
        return !handle.IsInvalid && GetFileInformationByHandleEx(handle, 18, out id, (uint)Marshal.SizeOf<FileId>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct FileId(ulong Volume, ulong Low, ulong High);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, FileShare share, nint security,
        FileMode mode, uint flags, nint template);

    [DllImport("kernel32.dll")]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out FileId id, uint size);
}
