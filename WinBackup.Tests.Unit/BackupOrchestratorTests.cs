using WinBackup.Core.Backup;
using WinBackup.Core.Config;
using WinBackup.Core.State;
using WinBackup.Tests.Unit.Fakes;
using Xunit;

namespace WinBackup.Tests.Unit;

public sealed class BackupOrchestratorTests : IDisposable
{
    private readonly string _dir;
    private readonly string _statePath;
    private readonly StateService _state = new();

    public BackupOrchestratorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "winbackup-orch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _statePath = Path.Combine(_dir, "state.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private BackupOrchestrator Build(
        FakeProtonEngine proton,
        FakeSsdEngine ssd,
        FakeClock clock,
        BackupConfig config,
        string? ssdRoot = @"X:\Backups")
    {
        return new BackupOrchestrator(
            proton, ssd,
            _ => Task.FromResult<string?>(ssdRoot),
            _state, _statePath, clock, config);
    }

    private static BackupConfig DefaultConfig() => new()
    {
        Schedule = { ProtonTime = "02:00", SsdTime = "18:00", SsdDayOfMonth = 1 },
    };

    /// <summary>Seeds a successful SSD backup this month so the SSD schedule is satisfied (isolates Proton tests).</summary>
    private void SeedSsdThisMonth(DateTimeOffset when) =>
        _state.AddRecord(_statePath, new BackupRecord
        {
            Target = BackupTarget.Ssd,
            Kind = BackupKind.Full,
            StartedAt = when,
            CompletedAt = when.AddMinutes(5),
            ResultCode = BackupResultCode.Success,
        });

    [Fact]
    public async Task RunProton_StatusTransitions_IdleRunningIdle()
    {
        var proton = new FakeProtonEngine();
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero));
        var sut = Build(proton, new FakeSsdEngine(), clock, DefaultConfig());

        OrchestratorStatus during = OrchestratorStatus.Idle;
        proton.OnRun = () => during = sut.Status;

        Assert.Equal(OrchestratorStatus.Idle, sut.Status);
        bool ran = await sut.RunProtonAsync();

        Assert.True(ran);
        Assert.Equal(OrchestratorStatus.RunningProton, during);
        Assert.Equal(OrchestratorStatus.Idle, sut.Status);
    }

    [Fact]
    public async Task ConcurrentTrigger_IsSilentlySkipped()
    {
        var gate = new TaskCompletionSource<bool>();
        var proton = new FakeProtonEngine { Gate = gate };
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero));
        var sut = Build(proton, new FakeSsdEngine(), clock, DefaultConfig());

        Task<bool> first = sut.RunProtonAsync();   // starts, blocks on gate
        bool second = await sut.RunProtonAsync();  // should be rejected immediately

        gate.SetResult(true);
        bool firstResult = await first;

        Assert.True(firstResult);
        Assert.False(second);
        Assert.Equal(1, proton.Calls); // the second never reached the engine
    }

    [Fact]
    public async Task RunProton_EngineThrows_StatusBecomesError()
    {
        var proton = new FakeProtonEngine { Throw = true };
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero));
        var sut = Build(proton, new FakeSsdEngine(), clock, DefaultConfig());

        bool ran = await sut.RunProtonAsync();

        Assert.False(ran);
        Assert.Equal(OrchestratorStatus.Error, sut.Status);
    }

    [Fact]
    public async Task RunSsd_SsdNotConnected_ReturnsFalse()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero));
        var ssd = new FakeSsdEngine();
        var sut = Build(new FakeProtonEngine(), ssd, clock, DefaultConfig(), ssdRoot: null);

        bool ran = await sut.RunSsdAsync();

        Assert.False(ran);
        Assert.Equal(0, ssd.Calls);
        Assert.Equal(OrchestratorStatus.Idle, sut.Status);
    }

    [Fact]
    public async Task Tick_ProtonDue_RunsProton()
    {
        // SSD already done this month → only Proton is due at 03:00 (proton time 02:00).
        SeedSsdThisMonth(new DateTimeOffset(2026, 5, 1, 18, 0, 0, TimeSpan.Zero));
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 10, 3, 0, 0, TimeSpan.Zero));
        var proton = new FakeProtonEngine();
        var sut = Build(proton, new FakeSsdEngine(), clock, DefaultConfig());

        BackupTarget? ran = await sut.TickAsync();

        Assert.Equal(BackupTarget.Proton, ran);
        Assert.Equal(1, proton.Calls);
    }

    [Fact]
    public async Task Tick_SsdDue_RunsSsd_TakesPriority()
    {
        // SSD day (1st) at 18:30, ssd time 18:00 → SSD due and prioritised over Proton.
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 1, 18, 30, 0, TimeSpan.Zero));
        var ssd = new FakeSsdEngine();
        var proton = new FakeProtonEngine();
        var sut = Build(proton, ssd, clock, DefaultConfig());

        BackupTarget? ran = await sut.TickAsync();

        Assert.Equal(BackupTarget.Ssd, ran);
        Assert.Equal(1, ssd.Calls);
        Assert.Equal(0, proton.Calls);
        Assert.Equal(@"X:\Backups", ssd.LastDestinationRoot);
    }

    [Fact]
    public async Task Tick_NothingDue_RunsNothing()
    {
        // SSD already done this month; 01:00 is before proton time → nothing due.
        SeedSsdThisMonth(new DateTimeOffset(2026, 5, 1, 18, 0, 0, TimeSpan.Zero));
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 10, 1, 0, 0, TimeSpan.Zero));
        var proton = new FakeProtonEngine();
        var ssd = new FakeSsdEngine();
        var sut = Build(proton, ssd, clock, DefaultConfig());

        BackupTarget? ran = await sut.TickAsync();

        Assert.Null(ran);
        Assert.Equal(0, proton.Calls);
        Assert.Equal(0, ssd.Calls);
    }
}
