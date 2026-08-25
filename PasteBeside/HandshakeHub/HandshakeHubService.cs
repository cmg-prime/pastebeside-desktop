using BoundaryModels;
using Microsoft.AspNetCore.SignalR.Client;
using PasteBeside.ClientFriendlyLog;

namespace PasteBeside.HandshakeHub;

public class HandshakeHubService
{
    private readonly ClientLog _clientLog;
    private readonly string _hubUrl;
    private readonly HubConnection _connection;
    private IList<string> _availableHandshakes;

    public HandshakeHubService(string apiUrl, ClientLog clientLog)
    {
        _availableHandshakes = [];
        _clientLog = clientLog;
        _hubUrl = $"{apiUrl}/handshakeHub";
        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect()
            .ConfigureLogging(builder => builder.SetMinimumLevel(LogLevel.Error))
            .Build();

        LocalParticipant = new Participant("TODO: pseudorandomly generate participant identifier");

        RegisterSignalHandlers();
    }

    public string? HandshakeId { get; private set; }
    public Participant LocalParticipant { get; private set; }
    public IReadOnlyList<string> AvailableHandshakes { get { return [.._availableHandshakes]; } }

    //TODO: add hub events & handlers for RTC negotiation steps.
    private void RegisterSignalHandlers()
    {
        _connection.On(nameof(HandshakeClient.AvailableHandshakesFound), async (IList<string> handshakeIds) =>
        {
            _availableHandshakes = handshakeIds;
        });

        _connection.On(nameof(HandshakeClient.PeerInitialized), async (string handshakeId, Participant participant) =>
        {
            HandshakeId = handshakeId;
            _availableHandshakes.Add(handshakeId);
            LocalParticipant = participant;
            _clientLog.Success($"Primed handshake ${HandshakeId}!");
        });

        _connection.On(nameof(HandshakeClient.JoiningHandshake), async (string handshakeId, Participant peer) =>
        {
            HandshakeId = handshakeId;
            _clientLog.Success($"Joining handshake ${handshakeId} with peer ${peer.DisplayName}!");
            //TODO: process RTC offer, send answer.
        });

        _connection.On(nameof(HandshakeClient.PeerJoiningHandshake), async (Participant peer) =>
        {
            _clientLog.Success($"Peer ${peer.DisplayName} joined handshake!");
            //TODO: send RTC offer.
        });

        _connection.On<string>(nameof(HandshakeClient.AbandonedHandshake), ProcessAbandonedHandshake);
        _connection.On<string>(nameof(HandshakeClient.PeerAbandonedHandshake), ProcessAbandonedHandshake);
    }

    public async Task InitializePeer()
    {
        _clientLog.Info("Invoking InitializePeer...");
        await (await GuaranteeConnection()).InvokeAsync(HandshakeHubEndpoint.InitializePeer, LocalParticipant.Id, LocalParticipant.DisplayName);
    }

    public async Task JoinHandshake()
    {
        _clientLog.Info("Invoking JoinHandshake...");
        await (await GuaranteeConnection()).InvokeAsync(HandshakeHubEndpoint.JoinHandshake, HandshakeId, LocalParticipant.Id);
    }

    public async Task AbandonHandshake()
    {
        _clientLog.Info("Invoking AbandonHandshake...");
        await (await GuaranteeConnection()).InvokeAsync(HandshakeHubEndpoint.AbandonHandshake, HandshakeId, LocalParticipant.Id);
    }

    private void ProcessAbandonedHandshake(string handshakeId)
    {
        HandshakeId = handshakeId;
        _availableHandshakes.Remove(handshakeId);
        _clientLog.Info($"Handshake ${HandshakeId} abandoned.");
    }

    private async Task<HubConnection> GuaranteeConnection()
    {
        if(_connection.State == HubConnectionState.Disconnected)
            await _connection.StartAsync();

        return _connection;
    }
}