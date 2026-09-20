using CrashScope.Core.Models;
using CrashScope.Desktop.Services;
using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace CrashScope.Desktop.Pages;

/// <summary>
/// Read-only latency and throughput measurements against public endpoints.
/// All runs are cancellable; an upload that does not finish inside its budget is
/// reported as incomplete instead of extrapolated, so numbers are never inflated.
/// </summary>
public partial class SpeedTestPage : UserControl
{
    private const string DownloadUrl = "https://speed.cloudflare.com/__down?bytes=52428800";
    private const string UploadUrl = "https://speed.cloudflare.com/__up";
    private const int TimeBudgetSeconds = 20;

    private CancellationTokenSource? _runCts;
    private bool _busy;

    public SpeedTestPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => _runCts?.Cancel();
    }

    private async void PingButton_Click(object sender, RoutedEventArgs e) => await RunAsync(RunLatencyAsync);
    private async void DownButton_Click(object sender, RoutedEventArgs e) => await RunAsync(RunDownloadAsync);
    private async void UpButton_Click(object sender, RoutedEventArgs e) => await RunAsync(RunUploadAsync);

    private async void RunAllButton_Click(object sender, RoutedEventArgs e)
        => await RunAsync(async ct =>
        {
            await RunLatencyAsync(ct);
            await RunDownloadAsync(ct);
            await RunUploadAsync(ct);
        });

    private async Task RunAsync(Func<CancellationToken, Task> run)
    {
        if (_busy) return;
        _busy = true;
        _runCts = new CancellationTokenSource();
        SetButtons(enabled: false);
        LogLine(Loc.F("speedTest.log.started", DateTime.Now));
        try
        {
            await run(_runCts.Token);
        }
        catch (OperationCanceledException)
        {
            LogLine(Loc.T("speedTest.log.cancelled"));
        }
        catch (Exception ex)
        {
            LogLine(Loc.F("speedTest.log.failed", ex.Message));
        }
        finally
        {
            _runCts.Dispose();
            _runCts = null;
            _busy = false;
            SetButtons(enabled: true);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _runCts?.Cancel();

    private void SetButtons(bool enabled)
    {
        PingButton.IsEnabled = enabled;
        DownButton.IsEnabled = enabled;
        UpButton.IsEnabled = enabled;
        RunAllButton.IsEnabled = enabled;
        CancelButton.IsEnabled = !enabled;
    }

    private async Task RunLatencyAsync(CancellationToken ct)
    {
        (string Label, string Host)[] targets =
        [
            (Loc.T("speedTest.host.aliDns"), "223.5.5.5"),
            (Loc.T("speedTest.host.tencentDns"), "119.29.29.29"),
            ("Cloudflare", "1.1.1.1"),
            ("Google", "8.8.8.8")
        ];

        foreach (var (label, host) in targets)
        {
            ct.ThrowIfCancellationRequested();
            List<double> ok = [];
            var lost = 0;
            using var ping = new Ping();
            for (var i = 0; i < 4; i++)
            {
                try
                {
                    var reply = await ping.SendPingAsync(host, 2000);
                    if (reply.Status == IPStatus.Success) ok.Add(reply.RoundtripTime);
                    else lost++;
                }
                catch
                {
                    lost++;
                }

                await Task.Delay(150, ct);
            }

            var reading = ok.Count > 0
                ? Loc.F("speedTest.log.latencyResult", SpeedTestView.FormatLatency(SpeedTestView.Average(ok)), SpeedTestView.FormatLatency(SpeedTestView.Jitter(ok)))
                : Loc.T("speedTest.log.allTimedOut");
            LogLine(Loc.F("speedTest.log.latencyLine", label, host, reading, lost));
        }
    }

    private async Task RunDownloadAsync(CancellationToken ct)
    {
        LogLine(Loc.F("speedTest.log.downloadTest", DownloadUrl));
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CrashScope-SpeedTest/0.9");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeBudgetSeconds));

        var sw = Stopwatch.StartNew();
        long total = 0;
        try
        {
            using var resp = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            resp.EnsureSuccessStatusCode();
            using var stream = await resp.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0)
                total += read;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Time budget reached — evaluate from the partial transfer below.
        }

        sw.Stop();
        if (total == 0)
        {
            LogLine(Loc.T("speedTest.log.downloadNoData"));
            return;
        }

        var bps = total / sw.Elapsed.TotalSeconds;
        LogLine(Loc.F("speedTest.log.downloadResult", SpeedTestView.FormatMbps(SpeedTestView.ToMbps(bps)), total / 1048576.0, sw.Elapsed.TotalSeconds));
        LogLine(Loc.F("speedTest.log.verdict", SpeedTestView.Verdict(SpeedTestView.ToMbps(bps))));
    }

    private async Task RunUploadAsync(CancellationToken ct)
    {
        LogLine(Loc.F("speedTest.log.uploadTest", UploadUrl));
        var payload = new byte[8 * 1024 * 1024];
        new Random(20260918).NextBytes(payload);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CrashScope-SpeedTest/0.9");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeBudgetSeconds));

        var sw = Stopwatch.StartNew();
        var completed = false;
        try
        {
            using var content = new ByteArrayContent(payload);
            using var resp = await http.PostAsync(UploadUrl, content, timeout.Token);
            resp.EnsureSuccessStatusCode();
            completed = true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Incomplete: how many bytes actually left is unknown, so no number is reported.
        }

        sw.Stop();
        if (!completed)
        {
            LogLine(Loc.F("speedTest.log.uploadTimeout", TimeBudgetSeconds));
            return;
        }

        var bps = payload.Length / sw.Elapsed.TotalSeconds;
        LogLine(Loc.F("speedTest.log.uploadResult", SpeedTestView.FormatMbps(SpeedTestView.ToMbps(bps)), sw.Elapsed.TotalSeconds));
    }

    private void LogLine(string line)
    {
        ResultsText.AppendText($"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");
        ResultsText.ScrollToEnd();
    }
}
