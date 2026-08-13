namespace PasteBeside;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        this
            .Background(Theme.Brushes.Background.Default)
			.DataContext(new MainViewModel(), (page, viewModel) => page
				.Content(new Grid()
					.RowDefinitions("*, 4*")
					.Children(
						new Grid()
							.ColumnDefinitions("*,*,*,*")
							.Children(
								BasicBorder(new TextBlock().Text("Block one!")).Grid(column: 0),
								BasicBorder(new TextBlock().Text("Block two!")).Grid(column: 1),
								BasicBorder(new TextBlock().Text("Block three!")).Grid(column: 2),
								BasicBorder(new TextBlock().Text("Block four!")).Grid(column: 3)
						).Grid(row: 0),
						new Grid()
							.ColumnDefinitions("3*, *")
							.Children(
								BasicBorder(new TextBlock().Text("Hello Uno Platform!")).Grid(column: 0),
								BasicBorder(
									new ListView()
										.Background(Theme.Brushes.Background.Default)
										.ItemsSource(() => viewModel.ClientLog.Messages)
										.ItemTemplate<ClientLogMessage>(LogMessageTemplate)
								).Grid(column: 1)
						).Grid(row: 1)
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
