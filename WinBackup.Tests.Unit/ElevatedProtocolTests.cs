using WinBackup.Core.Pipes;
using Xunit;

namespace WinBackup.Tests.Unit;

public sealed class ElevatedProtocolTests
{
    [Fact]
    public async Task Request_RoundTrips()
    {
        var request = new HelperRequest { Command = HelperCommand.VssSnapshot, Volume = "X:" };
        using var stream = new MemoryStream();

        await ElevatedProtocol.WriteMessageAsync(stream, request);
        stream.Position = 0;
        HelperRequest? back = await ElevatedProtocol.ReadMessageAsync<HelperRequest>(stream);

        Assert.NotNull(back);
        Assert.Equal(HelperCommand.VssSnapshot, back!.Command);
        Assert.Equal("X:", back.Volume);
    }

    [Fact]
    public async Task Response_HappyPath_RoundTrips()
    {
        var response = HelperResponse.Ok(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1");
        using var stream = new MemoryStream();

        await ElevatedProtocol.WriteMessageAsync(stream, response);
        stream.Position = 0;
        HelperResponse? back = await ElevatedProtocol.ReadMessageAsync<HelperResponse>(stream);

        Assert.NotNull(back);
        Assert.True(back!.Success);
        Assert.Equal(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1", back.Data);
        Assert.Null(back.Error);
    }

    [Fact]
    public async Task Response_ErrorPath_RoundTrips()
    {
        var response = HelperResponse.Fail("access denied");
        using var stream = new MemoryStream();

        await ElevatedProtocol.WriteMessageAsync(stream, response);
        stream.Position = 0;
        HelperResponse? back = await ElevatedProtocol.ReadMessageAsync<HelperResponse>(stream);

        Assert.NotNull(back);
        Assert.False(back!.Success);
        Assert.Equal("access denied", back.Error);
    }

    [Fact]
    public async Task Client_SendsRequest_AndParsesResponse()
    {
        // Pre-seed the "read" stream with the helper's response; capture the request on "write".
        using var responseStream = new MemoryStream();
        await ElevatedProtocol.WriteMessageAsync(responseStream, HelperResponse.Ok("snap-1"));
        responseStream.Position = 0;

        using var requestStream = new MemoryStream();
        var client = new ElevatedHelperClient(responseStream, requestStream);

        HelperResponse response = await client.SendCommandAsync(
            new HelperRequest { Command = HelperCommand.VssSnapshot, Volume = "C:" });

        Assert.True(response.Success);
        Assert.Equal("snap-1", response.Data);

        // The request was actually written out.
        requestStream.Position = 0;
        HelperRequest? sent = await ElevatedProtocol.ReadMessageAsync<HelperRequest>(requestStream);
        Assert.Equal(HelperCommand.VssSnapshot, sent!.Command);
        Assert.Equal("C:", sent.Volume);
    }

    [Fact]
    public async Task ReadMessage_EmptyStream_ReturnsDefault()
    {
        using var stream = new MemoryStream();
        HelperRequest? result = await ElevatedProtocol.ReadMessageAsync<HelperRequest>(stream);
        Assert.Null(result);
    }
}
