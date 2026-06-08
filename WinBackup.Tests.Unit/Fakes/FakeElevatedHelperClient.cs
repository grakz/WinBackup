using WinBackup.Core.Pipes;

namespace WinBackup.Tests.Unit.Fakes;

public sealed class FakeElevatedHelperClient : IElevatedHelperClient
{
    public List<HelperRequest> Requests { get; } = new();

    /// <summary>Shadow device root returned for VssSnapshot; null → snapshot fails.</summary>
    public string? SnapshotRoot { get; set; } = @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1";

    public int SnapshotCalls { get; private set; }

    public Task<HelperResponse> SendCommandAsync(HelperRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);

        HelperResponse response = request.Command switch
        {
            HelperCommand.VssSnapshot => Snapshot(),
            _ => HelperResponse.Ok(),
        };

        return Task.FromResult(response);
    }

    private HelperResponse Snapshot()
    {
        SnapshotCalls++;
        return SnapshotRoot is null ? HelperResponse.Fail("snapshot failed") : HelperResponse.Ok(SnapshotRoot);
    }
}

public sealed class FakeVssCoordinator : WinBackup.Core.Volume.IVssCoordinator
{
    public string? ShadowRoot { get; set; }
    public bool DeleteAllCalled { get; private set; }

    public Task<string?> GetShadowRootAsync(string volume, CancellationToken ct = default) =>
        Task.FromResult(ShadowRoot);

    public Task DeleteAllAsync(CancellationToken ct = default)
    {
        DeleteAllCalled = true;
        return Task.CompletedTask;
    }
}
