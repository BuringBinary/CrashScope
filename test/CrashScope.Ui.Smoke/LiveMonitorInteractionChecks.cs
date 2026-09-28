using System.Windows;
using System.Windows.Controls;
using CrashScope.Core.Models;
using CrashScope.Desktop;
using CrashScope.Desktop.Pages;

// Exercises the compiled page and its real WPF events inside the existing off-screen,
// plain-Application smoke host. No App, AppService, recorder, or user sessions are opened.
internal static class LiveMonitorInteractionChecks
{
    public static int Run(Window host, Action pump)
    {
        var checks = 0;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException("Live monitor: " + name);
            checks++;
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

            var list = (ListBox)page.FindName("EventListBox");
            var foreground = (CheckBox)page.FindName("FilterForeground");
            var process = (CheckBox)page.FindName("FilterProcess");
            foreground.IsChecked = false;
            source.EmitForeground(); pump();
            Check(list.Items.Count == 0, "filtered first event leaves an empty list without a dispatcher exception");
            foreground.IsChecked = true; pump();
            Check(list.Items.Count == 1, "changing a filter reveals buffered events without a new event");

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
            Check(source.EveryEventHas(0), "all six event handlers are removed on navigation");
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

    private sealed class SyntheticLiveEvents : ILiveMonitorEventSource
    {
        public event Action<ForegroundEvent>? ForegroundChanged;
        public event Action<ProcessEvent>? ProcessEventReceived;
        public event Action<GpuEngineSample>? GpuEngineUpdated;
        public event Action<DisplayTopologyEvent>? DisplayChanged;
        public event Action<PowerSchemeEvent>? PowerSchemeChanged;
        public event Action<RemoteSessionEvent>? RemoteSessionChanged;

        private static int Subscribers<T>(Action<T>? handlers) => handlers?.GetInvocationList().Length ?? 0;

        public bool EveryEventHas(int expected) => new[]
        {
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
