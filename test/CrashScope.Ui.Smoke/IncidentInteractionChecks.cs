using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CrashScope.Core.Models;
using CrashScope.Desktop;
using CrashScope.Desktop.Pages;

internal static class IncidentInteractionChecks
{
    public static int Run(Window host, string source, string output, Action pump)
    {
        var checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException("Incident interaction: " + name); checks++; }
        var analysis = new IncidentAnalysis(null!, null, 0, 0, null, Array.Empty<SensorTransition>(), Array.Empty<TimelineEntry>(),
            new[] { new FailureHypothesis(FailureMode.Unclassified, 0, 0, Array.Empty<EvidenceItem>()) }, Array.Empty<string>(), GraphicsContext.Empty);
        var stamp = new DateTimeOffset(2026, 9, 18, 1, 16, 0, TimeSpan.Zero);
        var first = new IncidentInfo("fixture-first", "", stamp, "Unclassified", 0, Array.Empty<string>(), analysis);
        var wanted = new IncidentInfo("fixture-second", "", stamp.AddMinutes(-7), "FutureMode", 0, Array.Empty<string>(), analysis);
        IReadOnlyList<IncidentInfo> data = new[] { first, wanted };
        var locale = "zh-CN";
        string Translate(string key)
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "Locales", locale + ".json")));
            var value = json.RootElement;
            foreach (var part in key.Split('.')) if (!value.TryGetProperty(part, out value)) return key;
            return value.GetString() ?? key;
        }
        var original = host.Content;
        try
        {
            var page = new IncidentsPage(() => data, () => locale, Translate, wanted.DirectoryName, true);
            host.Content = page; host.Width = 1200; host.Height = 750; pump(); host.UpdateLayout();
            var list = (ListBox)page.FindName("IncidentListBox");
            var export = (Button)page.FindName("ExportButton");
            Check(((IncidentListItem)list.SelectedItem).Incident.DirectoryName == wanted.DirectoryName, "ID selects second record, not first");
            Check(export.IsEnabled, "one selection enables export");
            Check(((TextBox)page.FindName("RecordIdText")).Text == wanted.DirectoryName, "technical ID preserved");
            Check(((TextBlock)page.FindName("DetailTitle")).Text.Contains("分类信息缺失"), "unknown mode localized");
            Check(((TextBlock)page.FindName("DetailTime")).Text.StartsWith("最后记录"), "heartbeat time label");
            list.SelectedItems.Add(list.Items[0]);
            Check(((Button)page.FindName("CompareButton")).IsEnabled, "two selections enable comparison");
            Check(!export.IsEnabled, "two selections do not export arbitrary record");
            list.SelectedItems.Clear(); Check(!export.IsEnabled, "no selection disables export");
            Check(((TextBox)page.FindName("RecordIdText")).Text == "", "clearing selection clears stale details");
            list.SelectedItem = list.Items[1];
            locale = "en-US"; page.RefreshLocale();
            Check(((IncidentListItem)list.SelectedItem).Incident.DirectoryName == wanted.DirectoryName, "locale preserves selection");
            Check(((TextBlock)page.FindName("DetailTitle")).Text.Contains("Classification data missing"), "code generated text follows locale");
            var wentBack = false; page.BackRequested += () => wentBack = true;
            ((Button)page.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(wentBack, "compiled back button event is connected");
            locale = "zh-CN"; page.RefreshLocale(); pump();
            var image = new RenderTargetBitmap(1200, 750, 96, 96, PixelFormats.Pbgra32); image.Render(page);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(Path.Combine(output, "fixture-incident-details-compiled.png"))) encoder.Save(file);
            data = new[] { first }; page.RefreshList();
            Check(!export.IsEnabled && list.SelectedItems.Count == 0, "removed selection cannot export stale record");
            var missing = new IncidentsPage(() => data, () => locale, Translate, "missing-id", true);
            host.Content = missing; pump();
            Check(((ListBox)missing.FindName("IncidentListBox")).SelectedItems.Count == 0, "missing target never selects first row");
            Check(((TextBlock)missing.FindName("SelectionNotice")).Text.Contains("不可用"), "missing target shown explicitly");
            data = Array.Empty<IncidentInfo>(); missing.RefreshList();
            Check(!((Button)missing.FindName("ExportButton")).IsEnabled, "empty list cannot export");
            var deferred = new TaskCompletionSource<IncidentReadResult>();
            var loads = 0;
            var retryPage = new IncidentsPage(() => ++loads == 1 ? deferred.Task : Task.FromResult(IncidentReadResult.Success(new[] { first })),
                () => locale, Translate);
            host.Content = retryPage; pump();
            Check(((TextBlock)retryPage.FindName("ReadNoticeText")).Text.Contains("正在读取"), "loading feedback before IO completes");
            Check(!((Button)retryPage.FindName("ExportButton")).IsEnabled, "cannot export while loading");
            deferred.SetResult(IncidentReadResult.Failed(new UnauthorizedAccessException("synthetic access failure"))); pump();
            Check(((TextBlock)retryPage.FindName("ReadNoticeText")).Text.Contains("无法读取"), "root error is not empty success");
            Check(((Expander)retryPage.FindName("ReadIssuesPanel")).Visibility == Visibility.Visible, "read errors have inspectable details");
            ((Button)retryPage.FindName("ReloadIncidentsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); pump();
            Check(((ListBox)retryPage.FindName("IncidentListBox")).Items.Count == 1 && loads == 2, "compiled retry reloads successfully");
            Check(((Expander)retryPage.FindName("ReadIssuesPanel")).Visibility == Visibility.Collapsed, "successful retry clears old error panel");
            var partial = new IncidentsPage(() => Task.FromResult(new IncidentReadResult(new[] { first },
                new[] { new IncidentReadIssue("fixture-broken", "invalidAnalysis", "synthetic broken record") })), () => locale, Translate);
            host.Content = partial; pump();
            var partialList = (ListBox)partial.FindName("IncidentListBox"); partialList.SelectedIndex = 0;
            Check(partialList.Items.Count == 1, "valid record retained beside failed record");
            Check(((TextBlock)partial.FindName("ReadNoticeText")).Text.Contains("不完整"), "selection does not erase partial read warning");
            var late = new TaskCompletionSource<IncidentReadResult>();
            var abandoned = new IncidentsPage(() => late.Task, () => locale, Translate);
            host.Content = abandoned; pump(); host.Content = partial; pump();
            late.SetResult(IncidentReadResult.Failed(new IOException("late synthetic failure"))); pump();
            Check(((TextBlock)abandoned.FindName("ReadNoticeText")).Text.Contains("正在读取"), "late response never updates an unloaded page");
            var throws = new IncidentsPage(() => Task.FromException<IncidentReadResult>(new IOException("fixture IO failure")), () => locale, Translate);
            host.Content = throws; pump();
            Check(((TextBlock)throws.FindName("ReadNoticeText")).Text.Contains("无法读取"), "faulted async provider is surfaced");
            Check(((Button)throws.FindName("ReloadIncidentsButton")).IsEnabled, "failed async provider permits retry");
            var targetAttempts = 0;
            var retryTarget = new IncidentsPage(() => Task.FromResult(++targetAttempts == 1
                ? IncidentReadResult.Failed(new IOException("synthetic temporary failure"))
                : IncidentReadResult.Success(new[] { first, wanted })), () => locale, Translate, wanted.DirectoryName, true);
            host.Content = retryTarget; pump();
            ((Button)retryTarget.FindName("ReloadIncidentsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); pump();
            Check(((IncidentListItem)((ListBox)retryTarget.FindName("IncidentListBox")).SelectedItem).Incident.DirectoryName == wanted.DirectoryName,
                "retry preserves the originally requested ID after a read failure");
            Console.WriteLine($"PASS: {checks} compiled WPF incident interaction checks (synthetic records; no collector/export writes).");
            return checks;
        }
        finally { host.Content = original; }
    }
}
