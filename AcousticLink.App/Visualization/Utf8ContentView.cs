using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AcousticLink.App.Protocol;

namespace AcousticLink.App.Visualization;

// Vista de bytes ya validados. No codifica el texto ni consulta referencias experimentales.
public sealed class Utf8ContentView : UserControl
{
    private readonly Grid layout = new();
    private readonly StackPanel cardsPanel = new();
    private readonly StackPanel byteDetail = new();
    private readonly List<Border> cards = new();
    private readonly BitMapView map = new() { ShowConventionalRoles = true, CompactSingleGroup = true };
    private FdmTextChannelResult? channel;
    private string channelName = "";
    public event Action<int, int>? ExploreBytesRequested;

    public Utf8ContentView()
    {
        byteDetail.VerticalAlignment = VerticalAlignment.Center;
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var left = new ScrollViewer { Content = cardsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var right = new ScrollViewer { Content = byteDetail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(right, 1); layout.Children.Add(left); layout.Children.Add(right); Content = layout;
        SizeChanged += (_, _) =>
        {
            bool narrow = ActualWidth < 700;
            layout.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            layout.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            layout.RowDefinitions[1].Height = narrow ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            Grid.SetColumn(right, narrow ? 0 : 1); Grid.SetRow(right, narrow ? 1 : 0);
        };
        map.PositionSelected += bit => HighlightByte((bit.Index - 8) / 8);
        Clear();
    }
    public void Clear(string status = "Sin mensaje validado")
    {
        channel = null; cards.Clear(); cardsPanel.Children.Clear(); byteDetail.Children.Clear();
        cardsPanel.Children.Add(new TextBlock { Text = status, FontSize = 22 });
    }
    public void Show(FdmTextChannelResult result, string name)
    {
        Clear(); channel = result; channelName = name;
        var characters = FramePresentation.Characters(result);
        if (characters.Count == 0) { byteDetail.Children.Add(new TextBlock { Text = "CRC y UTF-8 válidos son necesarios para mostrar bytes recuperados. " + result.Reason }); return; }
        cardsPanel.Children.Clear();
        cardsPanel.Children.Add(new TextBlock { Text = result.Text, FontSize = 28 });
        cardsPanel.Children.Add(new TextBlock { Text = $"Canal {name} · {result.ByteLength} bytes UTF-8 validados" });
        var row = new WrapPanel();
        foreach (var character in characters)
        {
            var face = new StackPanel();
            var glyph = new Button { Content = character.Character, FontSize = 28, Tag = character, Padding = new Thickness(8, 0, 8, 0) };
            glyph.Click += (_, _) => SelectByte(character.ByteOffset);
            face.Children.Add(glyph); face.Children.Add(new TextBlock { Text = $"{character.Bytes.Count} byte(s)" });
            for (int i = 0; i < character.Bytes.Count; i++)
            {
                int index = character.ByteOffset + i;
                var byteButton = new Button { Content = new TextBlock { Text = $"{character.Bits.Split(' ')[i]}\n0x{character.Bytes[i]:X2}", FontFamily = (FontFamily)FindResource("CodeFont") },
                    Tag = index, Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(0, 2, 0, 0) };
                byteButton.Click += (_, _) => SelectByte(index); face.Children.Add(byteButton);
            }
            var card = new Border { Child = face, Tag = character, Padding = new Thickness(8), Margin = new Thickness(3), BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
            row.Children.Add(card); cards.Add(card);
        }
        cardsPanel.Children.Add(row);
        cardsPanel.Children.Add(new TextBlock { Text = "Contenido completo · agrupado por bytes", Margin = new Thickness(0, 8, 0, 0) });
        cardsPanel.Children.Add(new TextBlock { Text = string.Join(" ", characters.Select(c => c.Bits)), FontFamily = (FontFamily)FindResource("CodeFont") });
        SelectByte(0);
    }
    public void HighlightByte(int index)
    {
        foreach (var card in cards)
        {
            var c = (ValidatedCharacter)card.Tag;
            card.BorderThickness = new Thickness(index >= c.ByteOffset && index < c.ByteOffset + c.Bytes.Count ? 3 : 1);
            card.BorderBrush = card.BorderThickness.Left == 3 ? Brushes.LightSkyBlue : Brushes.SlateGray;
        }
    }
    public void SelectByte(int index)
    {
        if (channel is null) return;
        var character = FramePresentation.Characters(channel).FirstOrDefault(c => index >= c.ByteOffset && index < c.ByteOffset + c.Bytes.Count);
        if (character is null) return;
        int local = index - character.ByteOffset;
        byteDetail.Children.Clear();
        byteDetail.Children.Add(new TextBlock { Text = $"Byte UTF-8 {index + 1} · carácter {character.Character}", FontSize = 22 });
        byteDetail.Children.Add(new TextBlock { Text = "Contenido original validado, no símbolos codificados de siete." });
        map.WordTraces = channel.HammingDiagnostics?.Traces ?? Array.Empty<Hamming74WordTrace>();
        map.ShowSection(character.Bits.Split(' ')[local], channelName, channel.UsesHamming, channel.UsesHamming, 8 + index * 8, 8, "Ocho bits originales, MSB primero");
        WorkspaceMotion.Reveal(byteDetail); byteDetail.Children.Add(map); map.HighlightRange(8 + index * 8, 8); HighlightByte(index);
        var explore = new Button { Content = "Localizar este byte en Trama", Margin = new Thickness(0, 8, 0, 0) };
        explore.Click += (_, _) => ExploreBytesRequested?.Invoke(index, 1); byteDetail.Children.Add(explore);
    }
}
