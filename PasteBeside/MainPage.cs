using Microsoft.UI.Text;
using PasteBeside.ClientFriendlyLog;
using Uno.Client;

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
													SectionTitle("Available handshakes"),
													CursorListView(viewModel)
												)
											).Grid(column: 0),
											BasicBorder(
												new AutoLayout()
													.Orientation(Orientation.Vertical)
													.Justify(AutoLayoutJustify.SpaceBetween)
													.Children(
														new StackPanel().Children(
															SectionTitle("Local peer"),
															new TextBlock().Text(() => viewModel.LocalPeer.Id)
														),
														new StackPanel().Children(
															new Border()
																.BorderBrush(new SolidColorBrush(Colors.LightGray))
																.Margin(0, 0, 0, 4)
																.BorderThickness(0, 0, 0, 1)
																.Child(new AutoLayout()
																	.Orientation(Orientation.Horizontal)
																	.Justify(AutoLayoutJustify.SpaceBetween)
																	.Padding(0, 0, 8, 0)
																	.Children(
																		new TextBlock()
																			.Text("Handshake")
																			.FontSize(16)
																			.FontWeight(FontWeights.SemiBold),
																		new Viewbox()
																			.Width(15)
																			.Height(15)
																			.Child(new PathIcon()
																				.Foreground(new SolidColorBrush(Colors.Black))
																				.Data("M300.9 149.2L184.3 278.8C179.7 283.9 179.9 291.8 184.8 296.7C215.3 327.2 264.8 327.2 295.3 296.7L327.1 264.9C331.3 260.7 336.6 258.4 342 258C348.8 257.4 355.8 259.7 361 264.9L537.6 440L608 384L608 96L496 160L472.2 144.1C456.4 133.6 437.9 128 418.9 128L348.5 128C347.4 128 346.2 128 345.1 128.1C328.2 129 312.3 136.6 300.9 149.2zM148.6 246.7L255.4 128L215.8 128C190.3 128 165.9 138.1 147.9 156.1L144 160L32 96L32 384L188.4 514.3C211.4 533.5 240.4 544 270.3 544L286 544L279 537C269.6 527.6 269.6 512.4 279 503.1C288.4 493.8 303.6 493.7 312.9 503.1L353.9 544.1L362.9 544.1C382 544.1 400.7 539.8 417.7 531.8L391 505C381.6 495.6 381.6 480.4 391 471.1C400.4 461.8 415.6 461.7 424.9 471.1L456.9 503.1L474.4 485.6C483.3 476.7 485.9 463.8 482 452.5L344.1 315.7L329.2 330.6C279.9 379.9 200.1 379.9 150.8 330.6C127.8 307.6 126.9 270.7 148.6 246.6z")
																			)
																	)
																),
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

	private static CursorListView CursorListView(MainViewModel viewModel)
	{
		var listView = new CursorListView()
			.ItemsSource(() => viewModel.AvailableHandshakes)
			.IsItemClickEnabled(true)
			.ItemTemplate<AvailableHandshakeViewModel>(AvailableHandshakes);

		// NB: this is a workaround for three other issues.
		// 1. Calling .Command fluently on the CursorListView doesn't work: the compiler matches
		// the extension method to the wrong type.
		// 2. Calling CommandExtensions.SetCommand explicitly (or rolling our own eventhandler for 
		// listView.ItemClick) requires a direct reference to viewModel.ConnectToHandshake. But viewModel
		// is just a proxy for type-safety - evaluating it eagerly (before the DataContext actually initializes)
		// always resolves to null.
		// 3. Defining inline event handler logic works for trivial cases, but fails when we need access
		// to viewModel data. We need a reference to the viewModel that resolves safely: a binding.
		listView.SetBinding(
			CommandExtensions.CommandProperty,
			new Binding { Path = new PropertyPath(nameof(MainViewModel.ConnectToHandshake)) }
		);

		return listView;
	}

	private static AutoLayout AvailableHandshakes(AvailableHandshakeViewModel viewModel) => new AutoLayout()
		.Orientation(Orientation.Horizontal)
		.Justify(AutoLayoutJustify.SpaceBetween)
		.Padding(0, 0, 8, 0)
		.Children(
			new TextBlock().Text(() => viewModel.AvailableHandshakeId),
			new Viewbox()
				.Width(15)
				.Height(15)
				.Child(new PathIcon()
				//NB: tempting to pass a Func<Brush> delegate into a helper method for the Viewbox, but it doesn't work.
				//Uno doesn't recognize that transitive binding to the viewModel.
					.Foreground(() => viewModel.IconBrush)
					.Data("M300.9 149.2L184.3 278.8C179.7 283.9 179.9 291.8 184.8 296.7C215.3 327.2 264.8 327.2 295.3 296.7L327.1 264.9C331.3 260.7 336.6 258.4 342 258C348.8 257.4 355.8 259.7 361 264.9L537.6 440L608 384L608 96L496 160L472.2 144.1C456.4 133.6 437.9 128 418.9 128L348.5 128C347.4 128 346.2 128 345.1 128.1C328.2 129 312.3 136.6 300.9 149.2zM148.6 246.7L255.4 128L215.8 128C190.3 128 165.9 138.1 147.9 156.1L144 160L32 96L32 384L188.4 514.3C211.4 533.5 240.4 544 270.3 544L286 544L279 537C269.6 527.6 269.6 512.4 279 503.1C288.4 493.8 303.6 493.7 312.9 503.1L353.9 544.1L362.9 544.1C382 544.1 400.7 539.8 417.7 531.8L391 505C381.6 495.6 381.6 480.4 391 471.1C400.4 461.8 415.6 461.7 424.9 471.1L456.9 503.1L474.4 485.6C483.3 476.7 485.9 463.8 482 452.5L344.1 315.7L329.2 330.6C279.9 379.9 200.1 379.9 150.8 330.6C127.8 307.6 126.9 270.7 148.6 246.6z")
				)
		);

	private static Border SectionTitle(string text) => new Border()
		.BorderBrush(new SolidColorBrush(Colors.LightGray))
		.Margin(0, 0, 0, 4)
		.BorderThickness(0, 0, 0, 1)
		.Child(new TextBlock()
			.Text(text)
			.FontSize(16)
			.FontWeight(FontWeights.SemiBold)
		);

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
