namespace PasteBeside;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        this
            .Background(Theme.Brushes.Background.Default)
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
						.ColumnDefinitions("4*, *")
						.Children(
							BasicBorder(new TextBlock().Text("Hello Uno Platform!")).Grid(column: 0),
							BasicBorder(new TextBlock().Text("Goodbye Uno Platform!")).Grid(column: 1)
					).Grid(row: 1)
				)
			);
    }

	private static Border BasicBorder(UIElement child) => new Border()
		.BorderBrush("Gray")
		.BorderThickness(1)
		.Margin(1)
		.Padding(1)
		.Child(child);
}
