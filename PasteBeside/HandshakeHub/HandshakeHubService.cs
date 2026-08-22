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
        _connection.On(HandshakeHubEvent.AvailableHandshakesFound, async (IList<string> handshakeIds) =>
        {
            _availableHandshakes = handshakeIds;
        });

        _connection.On(HandshakeHubEvent.ReadyForHandshake, async (string handshakeId, Participant participant) =>
        {
            HandshakeId = handshakeId;
            _availableHandshakes.Add(handshakeId);
            LocalParticipant = participant;
            _clientLog.Success($"Primed handshake ${HandshakeId}!");
        });

        _connection.On(HandshakeHubEvent.JoiningHandshake, async (string handshakeId, Participant peer) =>
        {
            HandshakeId = handshakeId;
            _clientLog.Success($"Joining handshake ${handshakeId} with peer ${peer.DisplayName}!");
            //TODO: process RTC offer, send answer.
        });

        _connection.On(HandshakeHubEvent.PeerJoiningHandshake, async (Participant peer) =>
        {
            _clientLog.Success($"Peer ${peer.DisplayName} joined handshake!");
            //TODO: send RTC offer.
        });

        _connection.On<string>(HandshakeHubEvent.AbandonedHandshake, ProcessAbandonedHandshake);
        _connection.On<string>(HandshakeHubEvent.PeerAbandonedHandshake, ProcessAbandonedHandshake);
    }

    public async Task InitializeHandshake()
    {
        _clientLog.Info("Invoking InitializeHandshake...");
        await (await GuaranteeConnection()).InvokeAsync(HandshakeHubEndpoint.InitializeHandshake, HandshakeId, LocalParticipant.Id, LocalParticipant.DisplayName);
    }

    public async Task JoinHandshake()
    {
        _clientLog.Info("Invoking JoinHandshake...");
        await (await GuaranteeConnection()).InvokeAsync(HandshakeHubEndpoint.JoinHandshake, HandshakeId, LocalParticipant.Id, LocalParticipant.DisplayName);
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