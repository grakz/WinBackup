using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using WinBackup.Core.Pipes;
using WinBackup.Elevated;

// Elevated helper: serves a single named-pipe session, performs admin-only volume/VSS operations
// for the (non-elevated) main app, then exits. Run-by-the-main-app, not interactively.

const string PipeName = "WinBackupElevated";

// Track shadow copies created this session so we can clean them all up on exit.
var deviceToShadowId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

using NamedPipeServerStream server = CreatePipeServer(PipeName);
Console.Error.WriteLine("WinBackup.Elevated: waiting for connection…");

try
{
    await server.WaitForConnectionAsync().ConfigureAwait(false);

    while (server.IsConnected)
    {
        HelperRequest? request = await ElevatedProtocol.ReadMessageAsync<HelperRequest>(server).ConfigureAwait(false);
        if (request is null)
        {
            break; // client disconnected
        }

        HelperResponse response = Dispatch(request, deviceToShadowId);
        await ElevatedProtocol.WriteMessageAsync(server, response).ConfigureAwait(false);

        if (request.Command == HelperCommand.Exit)
        {
            break;
        }
    }
}
finally
{
    CleanupSnapshots(deviceToShadowId);
}

return 0;

static HelperResponse Dispatch(HelperRequest request, Dictionary<string, string> snapshots)
{
    try
    {
        switch (request.Command)
        {
            case HelperCommand.Lock:
                // Locking is folded into the dismount sequence (single handle); ack here.
                return HelperResponse.Ok();

            case HelperCommand.Dismount:
            case HelperCommand.Eject:
                VolumeOperations.DismountAndEject(RequireVolume(request));
                return HelperResponse.Ok();

            case HelperCommand.RemoveMountPoint:
                VolumeOperations.RemoveMountPoint(RequireVolume(request));
                return HelperResponse.Ok();

            case HelperCommand.Remount:
                VolumeOperations.Remount(RequireVolume(request), request.SnapshotId ?? string.Empty);
                return HelperResponse.Ok();

            case HelperCommand.VssSnapshot:
            {
                VssOperations.Snapshot snap = VssOperations.Create(RequireVolume(request));
                snapshots[snap.DeviceObject] = snap.ShadowId;
                return HelperResponse.Ok(snap.DeviceObject);
            }

            case HelperCommand.VssDeleteSnapshot:
            {
                string device = request.SnapshotId ?? string.Empty;
                if (snapshots.TryGetValue(device, out string? id))
                {
                    VssOperations.Delete(id);
                    snapshots.Remove(device);
                }

                return HelperResponse.Ok();
            }

            case HelperCommand.Exit:
                return HelperResponse.Ok();

            default:
                return HelperResponse.Fail($"Unknown command: {request.Command}");
        }
    }
    catch (Exception ex)
    {
        return HelperResponse.Fail(ex.Message);
    }
}

static string RequireVolume(HelperRequest request) =>
    string.IsNullOrWhiteSpace(request.Volume)
        ? throw new ArgumentException("Command requires a Volume.")
        : request.Volume!;

static void CleanupSnapshots(Dictionary<string, string> snapshots)
{
    foreach (string id in snapshots.Values)
    {
        try { VssOperations.Delete(id); } catch { /* best effort on shutdown */ }
    }

    snapshots.Clear();
}

static NamedPipeServerStream CreatePipeServer(string name)
{
    // Allow the interactive user (the non-elevated main app, same user) to connect to this
    // elevated server's pipe.
    var security = new PipeSecurity();
    var currentUser = WindowsIdentity.GetCurrent().User!;
    security.AddAccessRule(new PipeAccessRule(currentUser, PipeAccessRights.ReadWrite, AccessControlType.Allow));
    security.AddAccessRule(new PipeAccessRule(
        new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
        PipeAccessRights.ReadWrite,
        AccessControlType.Allow));

    return NamedPipeServerStreamAcl.Create(
        name,
        PipeDirection.InOut,
        maxNumberOfServerInstances: 1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous,
        inBufferSize: 0,
        outBufferSize: 0,
        security);
}
