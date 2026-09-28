using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

// Isolated source-XAML render probe: no AppService, tray, recorder, user configuration. Only an off-screen, non-activating render host is created.
// Event handlers/x:Class are intentionally stripped. This is not end-to-end application testing.
internal static class Program
{
    private static int _checks;
    private static void Check(bool ok, string description)
    {
        if (!ok) throw new InvalidOperationException(description);
        _checks++;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var root = Path.GetFullPath(args[0]);
            var output = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(output);
            var source = Path.Combine(root, "src", "CrashScope.Desktop");
            _ = typeof(CrashScope.Desktop.OverviewMetricText).Assembly;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            ResourceDictionary Dictionary(string name) => (ResourceDictionary)XamlReader.Parse(File.ReadAllText(Path.Combine(source, "Themes", name + ".xaml")));
            var common = Dictionary("CommonResources");
            var dark = Dictionary("DarkTheme");
            var light = Dictionary("LightTheme");
            var controls = Dictionary("ControlStyles");
            app.Resources.MergedDictionaries.Add(common);
            app.Resources.MergedDictionaries.Add(dark);
            app.Resources.MergedDictionaries.Add(controls);
            Check(dark.Keys.Cast<object>().OrderBy(x => x.ToString()).SequenceEqual(light.Keys.Cast<object>().OrderBy(x => x.ToString())), "Palette keys must match");
            foreach (var palette in new[] { dark, light })
            {
                Check(Contrast(palette, "TextPrimary", "CardBg") >= 4.5, "Primary contrast");
                Check(Contrast(palette, "TextSecondary", "CardBg") >= 4.5, "Secondary contrast");
                Check(Contrast(palette, "NavCheckedText", "NavCheckedBg") >= 4.5, "Navigation contrast");
                Check(Contrast(palette, "AccentColor", "CardBg") >= 4.5, "Accent text contrast");
                Check(Contrast(palette, "FocusColor", "CardBg") >= 3, "Focus contrast");
            }
            var allKeys = new HashSet<string>(common.Keys.Cast<object>().Concat(dark.Keys.Cast<object>()).Concat(controls.Keys.Cast<object>()).OfType<string>());
            foreach (var file in new[] { "MainWindow.xaml", "Pages/HomePage.xaml", "Themes/ControlStyles.xaml" })
                foreach (Match match in Regex.Matches(File.ReadAllText(Path.Combine(source, file)), @"\{(?:Static|Dynamic)Resource\s+([^}]+)\}"))
                    if (!match.Groups[1].Value.StartsWith("L.")) Check(allKeys.Contains(match.Groups[1].Value), "Missing resource " + match.Groups[1].Value);

            var main = (Window)ParseView(Path.Combine(source, "MainWindow.xaml"));
            var home = (UserControl)ParseView(Path.Combine(source, "Pages/HomePage.xaml"));
            ((Grid)main.FindName("ContentArea")).Children.Add(home);
            ((RadioButton)main.FindName("NavHome")).IsChecked = true;
            // Keep the tree connected to an Application window for real resource invalidation.
            // Never activate or display this isolated host on the user's working desktop.
            main.WindowStartupLocation = WindowStartupLocation.Manual;
            main.Left = -32000; main.Top = -32000;
            main.ShowActivated = false; main.ShowInTaskbar = false;
            main.Show();
            var surface = (FrameworkElement)main.Content;

            // Recorder was merged into Home: recording status and controls now live on the Home page
            // plus the always-visible sidebar card, so there is no separate NavRecorder route.
            Check(new[] { "NavHome", "NavDashboard", "NavHardware", "NavDiskHealth", "NavSpeedTest", "NavLiveMonitor", "NavAgentMonitor", "NavAgentIncidents", "NavAgentActions", "NavIncidents", "NavTimeline", "NavSettings" }.All(n => main.FindName(n) is RadioButton), "All routes retained");
            Check(main.FindName("NavRecorder") is null, "Recorder route removed after merge into Home");
            Check(main.FindName("SidebarStateText") is TextBlock && main.FindName("SidebarRecordButton") is Button, "Global recording control is present");
            // The hint line collapsed into the row tooltip when the sidebar card was slimmed down;
            // the dot plus the state text are what remain on screen.
            Check(main.FindName("SidebarStatusDot") is System.Windows.Shapes.Ellipse && main.FindName("SidebarStateText") is TextBlock, "Global recording trust summary is present");
            Check(main.FindName("SidebarHintText") is null, "Sidebar hint line retired into the row tooltip");
            Check(main.FindName("SidebarRecorderRow") is Grid, "Slim recorder row is present");
            ((TextBlock)home.FindName("SessionText")).Text = "ISOLATED RENDER FIXTURE — no live collector";
            var action = (Button)home.FindName("CheckButton");
            var nav = (RadioButton)main.FindName("NavHome");
            main.Measure(new Size(1200, 750)); main.Arrange(new Rect(0, 0, 1200, 750)); main.UpdateLayout();
            var darkForeground = ((SolidColorBrush)action.Foreground).Color;
            app.Resources.MergedDictionaries[1] = light;
            Pump(); main.UpdateLayout();
            Check(((SolidColorBrush)action.Foreground).Color != darkForeground, "Existing button follows live theme switch");
            Check(((SolidColorBrush)action.Foreground).Color == ((SolidColorBrush)light["TextPrimary"]).Color, "Light button foreground");
            Check(((SolidColorBrush)nav.Foreground).Color == ((SolidColorBrush)light["NavCheckedText"]).Color, "Selected navigation follows live theme switch");

            var snapshots = 0;
            foreach (var locale in new[] { "en-US", "zh-CN" })
            {
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "Locales", locale + ".json")));
                var strings = new ResourceDictionary();
                Flatten(json.RootElement, "L", strings);
                if (app.Resources.MergedDictionaries.Count == 4) app.Resources.MergedDictionaries.RemoveAt(3);
                app.Resources.MergedDictionaries.Add(strings);
                // Synthetic recent records exercise the actual row templates in both languages.
                var sample = new CrashScope.Desktop.IncidentInfo("fixture-only-not-user-data", "",
                    new DateTimeOffset(2026, 9, 18, 1, 16, 0, TimeSpan.Zero), "Unclassified", 0, Array.Empty<string>(), null!);
                ((ListView)home.FindName("IncidentsList")).ItemsSource = new[]
                {
                    CrashScope.Desktop.IncidentPresentation.Create(sample, locale, k => (string)strings["L." + k]),
                    CrashScope.Desktop.IncidentPresentation.Create(sample with { DirectoryName = "fixture-2", Timestamp = sample.Timestamp.AddMinutes(-7) }, locale, k => (string)strings["L." + k])
                };
                ((TextBlock)home.FindName("IncidentsEmptyText")).Visibility = Visibility.Collapsed;
                foreach (var file in new[] { "MainWindow.xaml", "Pages/HomePage.xaml" })
                    foreach (Match match in Regex.Matches(File.ReadAllText(Path.Combine(source, file)), @"\{DynamicResource\s+(L\.[^}]+)\}"))
                        Check(strings.Contains(match.Groups[1].Value), "Missing locale " + match.Groups[1].Value);
                foreach (var theme in new[] { "Dark", "Light" })
                {
                    app.Resources.MergedDictionaries[1] = theme == "Dark" ? dark : light;
                    foreach (var size in new[] { new Size(1200, 750), new Size(900, 600) })
                    foreach (var scale in new[] { 1.0, 1.25, 1.5 })
                    {
                        main.Width = size.Width; main.Height = size.Height;
                        main.Measure(size); main.Arrange(new Rect(size)); Pump(); main.UpdateLayout();
                        Check(home.ActualWidth > 500, "Home keeps usable width at minimum window");
                        Check(((TextBlock)home.FindName("CpuValueText")).Text == "—", "Missing metric stays unknown");
                        if (size.Width == 1200 && scale == 1.0)
                        {
                            // First-screen budget from the product plan (5.1): with the scroll at zero the
                            // second recent record is fully inside the Home viewport, and the route list
                            // fits the sidebar without its own scrollbar while Advanced stays collapsed.
                            var list = (ListView)home.FindName("IncidentsList");
                            var second = list.ItemContainerGenerator.ContainerFromIndex(1) as FrameworkElement;
                            Check(second is not null && second.ActualHeight > 0, "Second recent record is realised");
                            var secondBottom = second!.TransformToAncestor(home).Transform(new Point(0, second.ActualHeight)).Y;
                            Check(secondBottom <= home.ActualHeight + 0.5, $"Two recent records fit the first screen at 1200x750 ({locale}/{theme}: bottom {secondBottom:0} vs viewport {home.ActualHeight:0})");
                            var routeScroll = FindAncestor<ScrollViewer>((RadioButton)main.FindName("NavHardware"))!;
                            Check(routeScroll.ScrollableHeight < 0.5, $"Route list needs no scrolling at 1200x750 ({locale}/{theme}: {routeScroll.ScrollableHeight:0}px overflow)");
                        }
                        var bitmap = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                        bitmap.Render(surface);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var file = File.Create(Path.Combine(output, $"{locale}-{theme}-{size.Width}x{size.Height}-{scale:0.00}.png"));
                        encoder.Save(file); snapshots++;
                    }
                }
            }
            // Exercise the reduced navigation at the minimum size: Settings is pinned outside
            // the scroll list, while the advanced group starts collapsed and keeps every Agent route.
            var settings = (RadioButton)main.FindName("NavSettings");
            Check(settings.IsVisible && FindAncestor<ScrollViewer>(settings) is null, "Settings stays pinned outside the route scroll list");
            var navScroll = FindAncestor<ScrollViewer>((RadioButton)main.FindName("NavHardware"));
            Check(navScroll is not null, "Tool routes remain inside the scroll list");
            var agentToggle = (System.Windows.Controls.Primitives.ToggleButton)main.FindName("AgentNavigationToggle");
            var agentItems = (StackPanel)main.FindName("AgentNavigationItems");
            Check(agentToggle.IsChecked != true && agentItems.Visibility == Visibility.Collapsed, "Advanced routes start collapsed");
            Check(((RadioButton)main.FindName("NavHome")).GroupName == "MainNavigation", "Routes share one keyboard navigation group");
            Check(((RadioButton)main.FindName("NavSettings")).GroupName == "MainNavigation", "Pinned Settings keeps the same route group");
            Check(((System.Windows.Controls.Primitives.Track)FindVisual<System.Windows.Controls.Primitives.Track>(navScroll!)!).Thumb is not null, "Themed scrollbar has a draggable thumb");

            // Explicit synthetic visual states; these are not collector integration tests.
            main.Width = 1200; main.Height = 750;
            app.Resources.MergedDictionaries[1] = dark;
            navScroll!.ScrollToTop();
            foreach (var state in new[] { "recording", "faulted" })
            {
                ((TextBlock)home.FindName("RecorderStateText")).Text = (string)app.Resources["L.overviewUi." + state];
                ((TextBlock)home.FindName("CollectionHealthText")).Text = "隔离渲染示例 · 非实机采集状态";
                ((TextBlock)home.FindName("CpuValueText")).Text = state == "recording" ? "24 %" : "—";
                ((TextBlock)home.FindName("GpuValueText")).Text = state == "recording" ? "38 %" : "—";
                ((TextBlock)home.FindName("RamValueText")).Text = state == "recording" ? "12.4 GB" : "—";
                var start = (Button)home.FindName("StartStopButton");
                start.Content = (string)app.Resources[state == "recording" ? "L.common.stop" : "L.common.start"];
                start.SetResourceReference(FrameworkElement.StyleProperty, state == "recording" ? "DangerButton" : "AccentButton");
                ((TextBlock)main.FindName("SidebarStateText")).Text = (string)app.Resources["L.sidebarUi." + (state == "recording" ? "recording" : "faulted")];
                ((TextBlock)main.FindName("SidebarStateText")).SetResourceReference(TextBlock.ForegroundProperty, state == "recording" ? "SuccessColor" : "DangerColor");
                ((Grid)main.FindName("SidebarRecorderRow")).ToolTip = state == "recording" ? "记录状态: 正在记录\n最近采样：10:32:18" : "记录状态: 采集故障\n请检查采集器或诊断日志。";
                var sidebarButton = (Button)main.FindName("SidebarRecordButton");
                // Icon-only in the slim row: keep just the leading MDL2 glyph, colour carries the stop affordance.
                var sidebarLabel = (string)app.Resources["L.sidebarUi." + (state == "recording" ? "stopRecording" : "retryRecording")];
                sidebarButton.Content = sidebarLabel.Length > 0 && sidebarLabel[0] is >= '\uE000' and <= '\uF8FF' ? sidebarLabel[..1] : sidebarLabel;
                sidebarButton.ToolTip = sidebarLabel.Trim();
                sidebarButton.SetResourceReference(Control.ForegroundProperty, state == "recording" ? "DangerColor" : "TextPrimary");
                var sidebarError = (TextBlock)main.FindName("SidebarErrorText");
                sidebarError.Text = state == "faulted" ? "原因：隔离测试采集器不可用。" : "";
                sidebarError.Visibility = state == "faulted" ? Visibility.Visible : Visibility.Collapsed;
                var error = (TextBlock)home.FindName("ErrorText");
                error.Text = "隔离测试：采集器不可用，请检查权限与诊断日志。";
                error.Visibility = state == "faulted" ? Visibility.Visible : Visibility.Collapsed;
                ((System.Windows.Shapes.Ellipse)home.FindName("StatusDot")).SetResourceReference(System.Windows.Shapes.Shape.FillProperty, state == "recording" ? "SuccessColor" : "DangerColor");
                Pump(); main.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1200, 750, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var fixture = File.Create(Path.Combine(output, $"fixture-{state}-zh-CN-Dark.png")); encoder.Save(fixture); snapshots++;
            }
            _checks += IncidentInteractionChecks.Run(main, source, output, Pump);
            _checks += LiveMonitorInteractionChecks.Run(main, Pump);
            main.Close();
            Console.WriteLine($"PASS: {_checks} total checks; {snapshots} source-XAML snapshots plus 1 compiled incident screenshot.");
            Console.WriteLine("DPI values are render resolutions, not real per-monitor DPI transitions. Incident and live-monitor interactions used synthetic data; no recorder or real export was run.");
            app.Shutdown(); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (var node = VisualTreeHelper.GetParent(child); node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is T match) return match;
        return null;
    }
    private static T? FindVisual<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var nested = FindVisual<T>(child); if (nested is not null) return nested;
        }
        return null;
    }
    private static void Pump()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
    private static object ParseView(string path)
    {
        var document = XDocument.Load(path);
        foreach (var element in document.Descendants())
            foreach (var attribute in element.Attributes().ToArray())
                if (attribute.Name.LocalName is "Class" or "Click" or "Checked" or "MouseLeftButtonDown") attribute.Remove();
        return XamlReader.Parse(document.ToString());
    }
    private static void Flatten(JsonElement element, string prefix, ResourceDictionary target)
    {
        foreach (var property in element.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.Object) Flatten(property.Value, prefix + "." + property.Name, target);
            else if (property.Value.ValueKind == JsonValueKind.String) target[prefix + "." + property.Name] = property.Value.GetString()!;
    }
    private static double Contrast(ResourceDictionary palette, string first, string second)
    {
        static double L(Color c)
        {
            static double Channel(byte b) { var v = b / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        }
        var a = L(((SolidColorBrush)palette[first]).Color); var b = L(((SolidColorBrush)palette[second]).Color);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
}
