using System.Management;

namespace WinBackup.Elevated;

/// <summary>
/// Creates and deletes Volume Shadow Copies via WMI (<c>Win32_ShadowCopy</c>). This is materially
/// simpler than hand-rolling the full <c>IVssBackupComponents</c> COM dance and is sufficient for
/// reading locked files from a point-in-time snapshot.
/// </summary>
internal static class VssOperations
{
    public sealed record Snapshot(string ShadowId, string DeviceObject);

    /// <summary>Creates a client-accessible shadow copy of <paramref name="volume"/> ("C:\") and returns its ids.</summary>
    public static Snapshot Create(string volume)
    {
        string root = Path.GetPathRoot(volume) ?? volume;
        if (!root.EndsWith('\\'))
        {
            root += "\\";
        }

        using var shadowClass = new ManagementClass("Win32_ShadowCopy");
        using ManagementBaseObject inParams = shadowClass.GetMethodParameters("Create");
        inParams["Volume"] = root;
        inParams["Context"] = "ClientAccessible";

        using ManagementBaseObject outParams = shadowClass.InvokeMethod("Create", inParams, null);
        uint returnValue = (uint)outParams["ReturnValue"];
        if (returnValue != 0)
        {
            throw new InvalidOperationException($"Win32_ShadowCopy.Create failed for '{root}' (code {returnValue}).");
        }

        string shadowId = (string)outParams["ShadowID"];
        string deviceObject = QueryDeviceObject(shadowId);
        return new Snapshot(shadowId, deviceObject);
    }

    /// <summary>Deletes the shadow copy with the given <paramref name="shadowId"/>.</summary>
    public static void Delete(string shadowId)
    {
        using var searcher = new ManagementObjectSearcher(
            $"SELECT * FROM Win32_ShadowCopy WHERE ID='{Escape(shadowId)}'");
        foreach (ManagementBaseObject obj in searcher.Get())
        {
            using var mo = (ManagementObject)obj;
            mo.Delete();
        }
    }

    private static string QueryDeviceObject(string shadowId)
    {
        using var searcher = new ManagementObjectSearcher(
            $"SELECT DeviceObject FROM Win32_ShadowCopy WHERE ID='{Escape(shadowId)}'");
        foreach (ManagementBaseObject obj in searcher.Get())
        {
            using (obj)
            {
                return (string)obj["DeviceObject"];
            }
        }

        throw new InvalidOperationException($"Shadow copy {shadowId} not found after creation.");
    }

    private static string Escape(string id) => id.Replace("'", "''");
}
