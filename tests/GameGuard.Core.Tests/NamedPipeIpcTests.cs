using System.Text.Json;
using GameGuard.Core.Ipc;
using GameGuard.Infrastructure.Ipc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class NamedPipeIpcTests : IAsyncLifetime
{
    private readonly string _pipeName = $"GameGuard_TestPipe_{Guid.NewGuid():N}";
    private NamedPipeIpcServer? _server;
    private NamedPipeIpcClient? _client;

    public async Task InitializeAsync()
    {
        _server = new NamedPipeIpcServer(_pipeName, NullLogger<NamedPipeIpcServer>.Instance);
        await _server.StartAsync();

        _client = new NamedPipeIpcClient(_pipeName, NullLogger<NamedPipeIpcClient>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
        }
        if (_server != null)
        {
            await _server.DisposeAsync();
        }
    }

    [Fact]
    public async Task Client_ConnectsToServer_AndExchangesGetStatusRequestResponse()
    {
        Assert.NotNull(_server);
        Assert.NotNull(_client);

        _server.RegisterHandler(IpcActions.GetStatus, request =>
        {
            var responseData = new ServiceStatusResponse(
                IsMonitoringActive: true,
                ActiveSessionsCount: 1,
                ActiveSessions: new[]
                {
                    new ActiveSessionInfo(Guid.NewGuid(), Guid.NewGuid(), "Counter-Strike 2", 1234, "cs2.exe", 600)
                });

            return Task.FromResult(IpcMessage.CreateResponse(request.Id, request.Action, JsonSerializer.Serialize(responseData)));
        });

        bool connected = await _client.ConnectAsync(TimeSpan.FromSeconds(5));
        Assert.True(connected);
        Assert.True(_client.IsConnected);

        var status = await _client.SendRequestAsync<object?, ServiceStatusResponse>(
            IpcActions.GetStatus,
            null,
            TimeSpan.FromSeconds(5));

        Assert.NotNull(status);
        Assert.True(status.IsMonitoringActive);
        Assert.Equal(1, status.ActiveSessionsCount);
        Assert.Single(status.ActiveSessions);
        Assert.Equal("Counter-Strike 2", status.ActiveSessions[0].GameName);
        Assert.Equal(1234, status.ActiveSessions[0].ProcessId);
    }

    [Fact]
    public async Task Client_SendsGrantBonusRequest_ServerExecutesAndReturnsSuccess()
    {
        Assert.NotNull(_server);
        Assert.NotNull(_client);

        _server.RegisterHandler(IpcActions.GrantBonus, request =>
        {
            var req = JsonSerializer.Deserialize<GrantBonusRequest>(request.PayloadJson!);
            Assert.NotNull(req);
            Assert.Equal(15, req.BonusMinutes);

            var result = new CommandResultResponse(true);
            return Task.FromResult(IpcMessage.CreateResponse(request.Id, request.Action, JsonSerializer.Serialize(result)));
        });

        bool connected = await _client.ConnectAsync(TimeSpan.FromSeconds(5));
        Assert.True(connected);

        var bonusReq = new GrantBonusRequest(Guid.NewGuid(), 15, "Test Bonus");
        var res = await _client.SendRequestAsync<GrantBonusRequest, CommandResultResponse>(
            IpcActions.GrantBonus,
            bonusReq,
            TimeSpan.FromSeconds(5));

        Assert.NotNull(res);
        Assert.True(res.Success);
        Assert.Null(res.ErrorMessage);
    }

    [Fact]
    public async Task Server_BroadcastsEvent_ClientReceivesEvent()
    {
        Assert.NotNull(_server);
        Assert.NotNull(_client);

        bool connected = await _client.ConnectAsync(TimeSpan.FromSeconds(5));
        Assert.True(connected);

        var eventReceivedTcs = new TaskCompletionSource<NotificationEventPayload>(TaskCreationOptions.RunContinuationsAsynchronously);

        _client.EventReceived += (_, msg) =>
        {
            if (msg.Action == IpcActions.NotificationEvent && msg.PayloadJson != null)
            {
                var payload = JsonSerializer.Deserialize<NotificationEventPayload>(msg.PayloadJson);
                if (payload != null)
                {
                    eventReceivedTcs.TrySetResult(payload);
                }
            }
        };

        // Give connection brief moment to register
        await Task.Delay(100);

        var notif = new NotificationEventPayload("Test Warning", "15 minutes remaining", 1);
        await _server.BroadcastEventAsync(IpcActions.NotificationEvent, notif);

        var completed = await Task.WhenAny(eventReceivedTcs.Task, Task.Delay(5000));
        Assert.Same(eventReceivedTcs.Task, completed);

        var received = await eventReceivedTcs.Task;
        Assert.Equal("Test Warning", received.Title);
        Assert.Equal("15 minutes remaining", received.Message);
    }
}
