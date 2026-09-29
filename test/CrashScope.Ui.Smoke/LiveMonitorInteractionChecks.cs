using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls;
using CrashScope.Core.Models;
using CrashScope.Core.Monitoring;
using CrashScope.Desktop;
using CrashScope.Desktop.Pages;

// Exercises the compiled page and its real WPF events inside the existing off-screen,
// plain-Application smoke host. No App, AppService, recorder, or user sessions are opened.
internal static class LiveMonitorInteractionChecks
{
    public static int Run(Window host, Action pump, string output, string fixture)
    {
        var checks = 0;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException("Live monitor: " + name);
            checks++;
        }

        string T(string key) => (string)Application.Current.FindResource("L." + key);
        void Capture(FrameworkElement view, string name)
        {
            host.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, $"compiled-live-{name}-{fixture}.png")); encoder.Save(file);
        }
        var container = (Grid)host.FindName("ContentArea");
        var original = container.Children.Cast<UIElement>().ToArray();
        var source = new SyntheticLiveEvents();
        try
        {
            container.Children.Clear();
            var page = new LiveMonitorPage(source);
            container.Children.Add(page);
            pump(); host.UpdateLayout();
            Check(source.EveryEventHas(1), "one subscriber for every event when the page is loaded");
            Check(page.FindName("FilterTelemetry") is null, "no inactive telemetry filter is advertised");

            var empty = (TextBlock)page.FindName("EmptyEventsText");
            var status = (TextBlock)page.FindName("LiveStatusText");
            // Dynamic resources work without constructing App/AppService in the isolated host.
            Check(empty.Visibility == Visibility.Visible && empty.Text == T("monitoring.eventsStopped"), "initial stopped state explains empty content");
            Capture(page, "stopped");
            source.SetState(RecorderState.Starting); pump();
            Check(status.Text == T("monitoring.starting"), "start transition updates without an event");
            source.SetState(RecorderState.Faulted); pump();
            Check(empty.Text == T("monitoring.faulted"), "fault is not presented as healthy empty data");
            source.SetState(RecorderState.Recording); pump();
            Check(empty.Text == T("monitoring.eventsWaiting"), "recording without events explains waiting");
            Check(((Button)page.FindName("ClearEventsButton")).ContentTemplate is not null, "clear button separates icon and label fonts");
            var glyph = TextBlocks((Button)page.FindName("ClearEventsButton")).First(t => t.Text.Any(c => c >= '\uE000' && c <= '\uF8FF'));
            Check(glyph.FontFamily.Source == "Segoe MDL2 Assets", "PUA icon is rendered using the icon font");
            Check(new Typeface(glyph.FontFamily, glyph.FontStyle, glyph.FontWeight, glyph.FontStretch).TryGetGlyphTypeface(out var face)
                && glyph.Text.All(c => face.CharacterToGlyphMap.TryGetValue(c, out var index) && index != 0), "installed icon font contains the requested glyph");
            var clear = (Button)page.FindName("ClearEventsButton");
            foreach (var part in TextBlocks(clear))
            {
                var right = part.TransformToAncestor(clear).Transform(new Point(part.ActualWidth, 0)).X;
                Check(right <= clear.ActualWidth - clear.Padding.Right + 0.5, "clear icon and localized label fit inside the button");
            }
            var list = (ListBox)page.FindName("EventListBox");
            var foreground = (CheckBox)page.FindName("FilterForeground");
            var process = (CheckBox)page.FindName("FilterProcess");
            foreground.IsChecked = false;
            source.EmitForeground(); pump();
            Check(list.Items.Count == 0, "filtered first event leaves an empty list without a dispatcher exception");
            Check(empty.Text == T("monitoring.eventsFiltered"), "filtered events have a distinct explanation");
            Capture(page, "filtered");
            foreground.IsChecked = true; pump();
            Check(list.Items.Count == 1, "changing a filter reveals buffered events without a new event");
            Check(empty.Visibility == Visibility.Collapsed, "empty overlay disappears when events are visible");
            source.SetState(RecorderState.Stopped); pump();
            Check(status.Text == T("monitoring.eventsStopped") && list.Items.Count == 1, "stop labels retained events as historical without deleting them");
            source.SetState(RecorderState.Recording); pump();

            source.EmitProcess(); pump();
            Check(list.Items.Count == 2, "a second category is shown");
            process.IsChecked = false; pump();
            Check(list.Items.Count == 1, "a category filter immediately hides existing events");
            foreground.IsChecked = false; pump();
            Check(list.Items.Count == 0, "hiding every buffered event leaves the list empty safely");
            process.IsChecked = true; foreground.IsChecked = true;
            ((Button)page.FindName("ClearEventsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(list.Items.Count == 0, "the compiled Clear button removes buffered events");
            foreground.IsChecked = false; foreground.IsChecked = true;
            Check(list.Items.Count == 0, "changing filters after Clear does not resurrect old events");

            // Leave while a background-style event is still queued on the dispatcher.
            source.EmitForeground();
            container.Children.Clear(); pump();
            Check(source.EveryEventHas(0), "all seven event handlers are removed on navigation");
            Check(list.Items.Count == 0, "queued events do not update the detached page");
            source.EmitForeground(); pump();
            Check(list.Items.Count == 0, "later events cannot update the detached page");

            container.Children.Add(page); pump();
            Check(source.EveryEventHas(1), "reloading the same page subscribes exactly once");
            container.Children.Clear(); pump();
            Check(source.EveryEventHas(0), "reloaded page releases all subscriptions again");

            for (var i = 0; i < 20; i++)
            {
                container.Children.Add(new LiveMonitorPage(source)); pump();
                Check(source.EveryEventHas(1), $"navigation {i + 1} has no duplicate subscribers");
                container.Children.Clear(); pump();
                Check(source.EveryEventHas(0), $"navigation {i + 1} leaves no subscribers");
            }
            Console.WriteLine($"PASS: {checks} compiled WPF live-monitor interaction checks (synthetic events; no recorder or user data).");
            return checks;
        }
        finally
        {
            container.Children.Clear();
            foreach (var child in original) container.Children.Add(child);
            pump();
        }
    }

    private static IEnumerable<TextBlock> TextBlocks(DependencyObject root)
    {
        if (root is TextBlock text) yield return text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var item in TextBlocks(VisualTreeHelper.GetChild(root, i))) yield return item;
    }

    private sealed class SyntheticLiveEvents : ILiveMonitorEventSource
    {
        public RecorderState DisplayRecorderState { get; private set; } = RecorderState.Stopped;
        public event Action? StatusChanged;
        public void SetState(RecorderState state) { DisplayRecorderState = state; StatusChanged?.Invoke(); }
        public event Action<ForegroundEvent>? ForegroundChanged;
        public event Action<ProcessEvent>? ProcessEventReceived;
        public event Action<GpuEngineSample>? GpuEngineUpdated;
        public event Action<DisplayTopologyEvent>? DisplayChanged;
        public event Action<PowerSchemeEvent>? PowerSchemeChanged;
        public event Action<RemoteSessionEvent>? RemoteSessionChanged;

        private static int Subscribers<T>(Action<T>? handlers) => handlers?.GetInvocationList().Length ?? 0;

        public bool EveryEventHas(int expected) => new[]
        {
            StatusChanged?.GetInvocationList().Length ?? 0,
            Subscribers(ForegroundChanged), Subscribers(ProcessEventReceived),
            Subscribers(GpuEngineUpdated), Subscribers(DisplayChanged),
            Subscribers(PowerSchemeChanged), Subscribers(RemoteSessionChanged)
        }.All(count => count == expected);

        public void EmitForeground() => ForegroundChanged?.Invoke(new ForegroundEvent(
            DateTimeOffset.UtcNow, 42, "synthetic-game.exe", 0));

        public void EmitProcess() => ProcessEventReceived?.Invoke(new ProcessEvent(
            DateTimeOffset.UtcNow, "Started", 42, "synthetic-game.exe"));
    }
}
