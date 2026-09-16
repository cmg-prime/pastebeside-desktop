using System.Net.Sockets;
using System.Text;
using PasteBeside.ClientFriendlyLog;

namespace PasteBeside.PeerToPeer;

public class HandshakeService
{
	private const string _handshakePrefix = "pastebeside-handshake-v1:";

	private readonly IdentityService _identityService;
	private readonly ClientLogger _logger;
	private readonly StreamService _streamService; 

	public HandshakeService(IdentityService identityService, ClientLogger logger, StreamService streamService)
	{
		_identityService = identityService;
		_logger = logger;
		_streamService = streamService;
	}

	public async Task<string?> PerformHandshake(TcpClient client, CancellationToken cancelToken)
	{
		var stream = client.GetStream();
		await _streamService.Write(stream, $"{_handshakePrefix}{_identityService.Identifier}", cancelToken);
		
		var response = await _streamService.TryRead(stream, cancelToken);
		if(!response.IsSucess || !response.Message!.StartsWith(_handshakePrefix, StringComparison.Ordinal))
			return null;

		var remotePeerId = response.Message![_handshakePrefix.Length..];
		if(remotePeerId == _identityService.Identifier)
			return null;

		await _logger.Success($"Handshake succeeded with peer {remotePeerId}!");
		return remotePeerId;
	}
}