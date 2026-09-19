using System.Net;
using System.Net.Sockets;
using PasteBeside.ClientFriendlyLog;
using PasteBeside.Eventing;

namespace PasteBeside.PeerToPeer;

public abstract class ConnectionCommand
{
	private readonly ClientLogger _logger;
	private readonly EventBus _eventBus;
	private readonly HandshakeService _handshakeService;
	private readonly LastConnectedPeerRepository _lastConnectedPeerRepo;
	private readonly DiscoveredPeerRepository _discoveryRepo;

	public ConnectionCommand(ClientLogger logger, EventBus eventBus, HandshakeService handshakeService, LastConnectedPeerRepository lastConnectedPeerRepo, DiscoveredPeerRepository discoveryRepo)
	{
		_logger = logger;
		_eventBus = eventBus;
		_handshakeService = handshakeService;
		_lastConnectedPeerRepo = lastConnectedPeerRepo;
		_discoveryRepo = discoveryRepo;
	}

	// TODO: if this is the only place we perform a handshake, this is a HandshakeCommand, and we don't need a 
	// HandshakeService.
	protected async Task<bool> Execute(string connectionName, TcpClient client, CancellationToken cancelToken)
	{
		if (client.Connected == true) 
			return false;

		// NB: we need a handshake for better identity resolution than address/port - determining a canonical
		// address involves a surprising number of edge cases. (E.g. ephemeral ports, multiple network 
		// interfaces - switching between wifi, ethernet, VPN, or some other niche network interface
		// during the same session - or what if your DHCP lease expires while your machine sleeps?)
		var remotePeerId = await _handshakeService.PerformHandshake(client, cancelToken);
		if (remotePeerId is null)
		{
			await _logger.Error($"{connectionName} refused.");
			return false;
		}

		_lastConnectedPeerRepo.Set(_discoveryRepo.SearchById(remotePeerId!)!);
		await _logger.Info($"{connectionName} accepted!");
		_eventBus.OnPeerConnected(_lastConnectedPeerRepo.Get()!);
		return true;
	}

}