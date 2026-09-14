using BoundaryModels;
using PasteBeside.ClientFriendlyLog;
using PasteBeside.Eventing;
using PasteBeside.HandshakeHub;
using PasteBeside.PeerToPeer;

namespace PasteBeside;

internal partial record MainModel
{
	private readonly ClientLogger _logger;
	private readonly PeerConnectionClient _peerClient;
	private readonly EventBus _eventBus;

	public MainModel(ClientLogger clientLogger, PeerDiscoveryService peerService, PeerConnectionClient peerClient, EventBus eventBus)
	{
		_logger = clientLogger;
		_peerClient = peerClient;
		_eventBus = eventBus;
		
		Messages = _logger.Messages;
		LocalPeer = State<Peer>.Empty(this);
		RemotePeer = State<Peer>.Empty(this);
		AvailablePeers = State<IList<AvailablePeerViewModel>>.Value(this, () => []);

		_eventBus.LocalPeerConfigured += async (sender, args) =>
		{
			await LocalPeer!.UpdateAsync(current => args.Peer);
		};
		_eventBus.PeerDiscovered += async (sender, args) =>
		{
			var localPeer = await LocalPeer;
			await AvailablePeers!.UpdateAsync(peers =>
				[.. peers!, new AvailablePeerViewModel(args.Peer, localPeer!.Id)]
			);
		};
		_eventBus.PeerConnected += async (sender, args) =>
		{
			await RemotePeer!.UpdateAsync(current => args.Peer);
			await _logger.Success($"Connection established with peer {args.Peer.Id}!");
		};
		_eventBus.PeerDisconnected += async (sender, args) =>
		{
			await AvailablePeers!.UpdateAsync(existing =>
			{
				var targetModel = existing!.First(model => model.AvailablePeer.Id == args.PeerId);
				existing!.Remove(targetModel);
				return existing;
			});
			await RemotePeer!.UpdateAsync(peer => null);
			await _logger.Error($"Peer {args.PeerId} disconnected.");
		};

		// TODO: can this run in a background service?
#pragma warning disable CS4014
		peerService.BeginDiscovery();
#pragma warning restore CS4014
	}

	public IState<IReadOnlyList<ClientLogMessage>> Messages { get; }
	public IState<Peer> LocalPeer { get; }
	public IState<Peer> RemotePeer { get; }
	public IState<IList<AvailablePeerViewModel>> AvailablePeers { get; }

	public async Task ConnectToPeer(AvailablePeerViewModel viewModel)
	{
		if (viewModel.IsCurrentlyConnectedPeer)
		{
			await _logger.Info($"Already connected to peer '{viewModel.AvailablePeer.Id}'.");
			return;
		}
		
		await _peerClient.MakeOutgoingConnection(viewModel.AvailablePeer.Endpoint, new CancellationToken());
	}

}
