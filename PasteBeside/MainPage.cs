using Microsoft.UI.Text;
using PasteBeside.ClientFriendlyLog;

namespace PasteBeside;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
		this.DataContext<MainViewModel>((page, viewModel) => page
				.Background(Theme.Brushes.Background.Default)
				.NavigationCacheMode(NavigationCacheMode.Required)
				.Content(
					new Grid()
						.ColumnDefinitions("3*, *")
						.Children(
							new Grid()
								.RowDefinitions("*, 3*")
								.Children(
									new Grid()
										.ColumnDefinitions("*, *")
										.Children(
											BasicBorder(
												new StackPanel().Children(
													new TextBlock().Text("Available handshakes").FontSize(16).FontWeight(FontWeights.SemiBold),
													new ListView()
														.ItemsSource(() => viewModel.AvailableHandshakes)
														.ItemTemplate<string>(handshakeId => 
															new TextBlock().Text(() => handshakeId)
														)
												)
											).Grid(column: 0),
											BasicBorder(
												new AutoLayout()
													.Orientation(Orientation.Vertical)
													.Justify(AutoLayoutJustify.SpaceBetween)
													.Children(
														new StackPanel().Children(
															new TextBlock().Text("Local peer").FontSize(16).FontWeight(FontWeights.SemiBold),
															new TextBlock().Text(() => viewModel.LocalPeer.Id)
														),
														new StackPanel().Children(
															new TextBlock().Text("Handshake").FontSize(16).FontWeight(FontWeights.SemiBold),
															new TextBlock().Text(
																() => viewModel.LocalPeer, 
																peer => $"Handshake ID: {peer.HandshakeId ?? "(none)"}"
															),
															new TextBlock().Text(
																() => viewModel.RemotePeer, 
																peer => $"Remote peer: {peer.Id ?? "(none)"}"
															)
														)
													)
											).Grid(column: 1)
										).Grid(row: 0),
									BasicBorder(new TextBlock().Text("Hello, Uno!")).Grid(row: 1)
								).Grid(column: 0),
							BasicBorder(
								new ListView()
									.Background(Theme.Brushes.Background.Default)
									.ItemsSource(() => viewModel.Messages)
									.ItemTemplate<ClientLogMessage>(LogMessageTemplate)
							).Grid(column: 1)
						)
				)
			);
    }

	private static TextBlock LogMessageTemplate(ClientLogMessage message) => new TextBlock()
		.Text(() => message, message => $"{message.LogTime:HH:mm:ss} {message.Text}")
		.Foreground(() => message.Type, type => GetColor(type))
		.TextWrapping(TextWrapping.Wrap);

	private static Border BasicBorder(UIElement child) => new Border()
		.Background(Theme.Brushes.Surface.Default)
		.BorderBrush(Theme.Brushes.Outline.Variant.Default)
		.BorderThickness(1)
		.CornerRadius(8)
		.Margin(8)
		.Padding(12)
		.Child(child);

	private static string GetColor(ClientLogType type){
		var colorByType = new Dictionary<ClientLogType, string>()
		{
			{ ClientLogType.Info, "Black" },
			{ ClientLogType.Error, "Red" },
			{ ClientLogType.Success, "Green" },
		};

		return colorByType[type];
	}
}
