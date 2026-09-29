using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Source-XAML layout only. No view models, endpoint discovery, RPC, or system actions.
internal static class AgentLayoutChecks
{
    public static int Run(Window host, string source, string output, string fixture,
        Action pump, Func<string, object> parse)
    {
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException("Agent layout: " + name); checks++; }
        var area = (Grid)host.FindName("ContentArea");
        var saved = area.Children.Cast<UIElement>().ToArray();
        var width = host.Width; var height = host.Height;
        void Capture(FrameworkElement page, string name)
        {
            var size = new Size(page.ActualWidth, page.ActualHeight);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle((Brush)Application.Current.FindResource("ContentBg"), null, new Rect(size));
                dc.DrawRectangle(new VisualBrush(page), null, new Rect(size));
            }
            var image = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
            image.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(Path.Combine(output, $"layout-{name}-{fixture}.png")); encoder.Save(file);
        }
        try
        {
            foreach (var size in new[] { new Size(900, 600), new Size(1200, 750) })
            foreach (var route in new[] { "AgentActions", "AgentMonitor", "AgentIncidents" })
            {
                host.Width = size.Width; host.Height = size.Height;
                var page = (UserControl)parse(Path.Combine(source, "Pages", route + "Page.xaml"));
                area.Children.Clear(); area.Children.Add(page); pump(); host.UpdateLayout();
                var error = (TextBox)page.FindName(route == "AgentActions" ? "StatusText" : "ConnectionText");
                var details = string.Concat(Enumerable.Repeat("Agent endpoint diagnostic / 模拟错误 / C:\\long-state-directory\\rpc-endpoint.json; ", 30)) + "END";
                error.Text = details; pump(); host.UpdateLayout();
                Check(error.IsReadOnly && error.TextWrapping == TextWrapping.Wrap, "error is selectable read-only wrapped text");
                Check(error.Text.EndsWith("END") && error.VerticalScrollBarVisibility == ScrollBarVisibility.Auto, "full diagnostic retained with scrolling");
                var edge = error.TransformToAncestor(page).Transform(new Point(error.ActualWidth, 0)).X;
                Check(edge <= page.ActualWidth + 0.5 && error.ActualHeight <= 64.5, "long error stays inside viewport");
                var ids = route == "AgentActions" ? new[] { "ConnectButton", "ApplyPlanButton", "ApplyPriorityButton", "RollbackButton" }
                    : route == "AgentMonitor" ? new[] { "ConnectButton", "DisconnectButton", "StartRecButton", "StopRecButton" } : new[] { "ConnectButton" };
                foreach (var id in ids)
                {
                    var button = (Button)page.FindName(id); button.BringIntoView(); pump(); host.UpdateLayout();
                    var a = button.TransformToAncestor(page).Transform(new Point());
                    var b = button.TransformToAncestor(page).Transform(new Point(button.ActualWidth, button.ActualHeight));
                    Check(a.X >= -0.5 && b.X <= page.ActualWidth + 0.5, route + " " + id + " horizontal bounds");
                    Check(a.Y >= -0.5 && b.Y <= page.ActualHeight + 0.5, route + " " + id + " reachable vertically");
                    foreach (var label in TextBlocks(button))
                    {
                        var right = label.TransformToAncestor(button).Transform(new Point(label.ActualWidth, 0)).X;
                        Check(right <= button.ActualWidth - button.Padding.Right + 0.5, id + " localized content fits");
                    }
                }
                if (route == "AgentActions")
                {
                    ((Button)page.FindName("ApplyPriorityButton")).BringIntoView(); pump(); host.UpdateLayout();
                    var reason = (TextBox)page.FindName("PriorityReasonBox");
                    var priority = (ComboBox)page.FindName("PriorityBox");
                    Check(reason.ActualWidth > priority.ActualWidth, "reason uses its own full-width row");
                    Check((string)((ComboBoxItem)priority.SelectedItem).Tag == "Normal", "localized priority preserves protocol tag");
                    ((ScrollViewer)page.FindName("ActionsViewport")).ScrollToTop(); pump(); host.UpdateLayout();
                }
                Capture(page, route + "-" + (int)size.Width);
            }
            // Parse the raw viewer without its code-behind and never show a second Window.
            var raw = (Window)parse(Path.Combine(source, "RawSensorWindow.xaml"));
            var filter = (ComboBox)raw.FindName("TypeFilter");
            var content = (UIElement)raw.Content; raw.Content = null;
            area.Children.Clear(); area.Children.Add(content);
            filter.ItemsSource = new[] { "All", "CPU" }; filter.SelectedIndex = 0; pump(); host.UpdateLayout();
            var all = (string)Application.Current.FindResource("L.rawSensors.allTypes");
            Check(TextBlocks(filter).Any(t => t.Text == all), "All sentinel has localized display text");
            Check((string)filter.SelectedItem == "All", "localization does not change the internal All filter value");
            filter.SelectedIndex = 1; pump();
            Check((string)filter.SelectedItem == "CPU" && TextBlocks(filter).Any(t => t.Text == "CPU"), "hardware filter stays intact");
            Console.WriteLine($"PASS: {checks} Agent/RawSensor source-XAML layout checks ({fixture}; no RPC or mutations).");
            return checks;
        }
        finally
        {
            area.Children.Clear(); foreach (var child in saved) area.Children.Add(child);
            host.Width = width; host.Height = height; pump();
        }
    }
    private static IEnumerable<TextBlock> TextBlocks(DependencyObject root)
    {
        if (root is TextBlock text) yield return text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in TextBlocks(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
