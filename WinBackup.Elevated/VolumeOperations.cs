using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinBackup.Elevated;

/// <summary>
/// Win32 volume operations used to air-gap the SSD: lock, dismount, eject, and add/remove the drive
/// letter. All require administrator rights (this process runs elevated).
/// </summary>
internal static class VolumeOperations
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;

    private const uint FsctlLockVolume = 0x00090018;
    private const uint FsctlDismountVolume = 0x00090020;
    private const uint IoctlStorageEjectMedia = 0x002D4808;

    /// <summary>Locks then dismounts the filesystem on <paramref name="driveLetter"/> (e.g. "X:") and ejects the media.</summary>
    public static void DismountAndEject(string driveLetter)
    {
        string root = NormalizeRoot(driveLetter);
        using SafeFileHandle handle = OpenVolume(root);

        DeviceIoControlOrThrow(handle, FsctlLockVolume, "lock volume");
        DeviceIoControlOrThrow(handle, FsctlDismountVolume, "dismount volume");
        DeviceIoControlOrThrow(handle, IoctlStorageEjectMedia, "eject media");
    }

    /// <summary>Removes the drive-letter mount point so the volume is no longer visible.</summary>
    public static void RemoveMountPoint(string driveLetter)
    {
        string mountPoint = driveLetter.TrimEnd('\\') + "\\";
        if (!DeleteVolumeMountPoint(mountPoint))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"DeleteVolumeMountPoint('{mountPoint}') failed.");
        }
    }

    /// <summary>Re-attaches a previously removed drive letter to <paramref name="volumeGuidPath"/>.</summary>
    public static void Remount(string driveLetter, string volumeGuidPath)
    {
        string mountPoint = driveLetter.TrimEnd('\\') + "\\";
        if (!SetVolumeMountPoint(mountPoint, volumeGuidPath))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"SetVolumeMountPoint('{mountPoint}') failed.");
        }
    }

    private static SafeFileHandle OpenVolume(string root)
    {
        // Volume device path form: \\.\X:
        string device = $@"\\.\{root.TrimEnd('\\')}";
        SafeFileHandle handle = CreateFile(device, GenericRead | GenericWrite, FileShareReadWrite,
            IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateFile('{device}') failed.");
        }

        return handle;
    }

    private static void DeviceIoControlOrThrow(SafeFileHandle handle, uint controlCode, string what)
    {
        if (!DeviceIoControl(handle, controlCode, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"DeviceIoControl ({what}) failed.");
        }
    }

    private static string NormalizeRoot(string driveLetter)
    {
        string trimmed = driveLetter.TrimEnd('\\', '/');
        if (trimmed.Length == 1)
        {
            trimmed += ":";
        }

        return trimmed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteVolumeMountPoint(string lpszVolumeMountPoint);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetVolumeMountPoint(string lpszVolumeMountPoint, string lpszVolumeName);
}
