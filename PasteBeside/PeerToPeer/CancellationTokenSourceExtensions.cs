namespace PasteBeside.PeerToPeer;

public static class CancellationTokenSourceExtensions
{
	public static CancellationTokenSource Recycle(this CancellationTokenSource source)
	{
		TearDown(source);
		return new CancellationTokenSource();
	}

	public static void TearDown(this CancellationTokenSource source)
	{
		source.Cancel();
		source.Dispose();
	}
}