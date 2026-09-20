using CrashScope.Core.Models;
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
        LogLine($"—— {DateTime.Now:HH:mm:ss} 开始 ——");
        try
        {
            await run(_runCts.Token);
        }
        catch (OperationCanceledException)
        {
            LogLine("已取消。");
        }
        catch (Exception ex)
        {
            LogLine($"失败：{ex.Message}");
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
            ("阿里 DNS", "223.5.5.5"),
            ("腾讯 DNS", "119.29.29.29"),
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
                ? $"平均 {SpeedTestView.FormatLatency(SpeedTestView.Average(ok))}，抖动 {SpeedTestView.FormatLatency(SpeedTestView.Jitter(ok))}"
                : "全部超时";
            LogLine($"延迟 {label} ({host}): {reading}，丢包 {lost}/4");
        }
    }

    private async Task RunDownloadAsync(CancellationToken ct)
    {
        LogLine($"下载测试：{DownloadUrl}");
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
            LogLine("下载失败：无数据（端点不可达或被代理/防火墙拦截）");
            return;
        }

        var bps = total / sw.Elapsed.TotalSeconds;
        LogLine($"下载：{SpeedTestView.FormatMbps(SpeedTestView.ToMbps(bps))}（{total / 1048576.0:F1} MB / {sw.Elapsed.TotalSeconds:F1}s）");
        LogLine($"结论：{SpeedTestView.Verdict(SpeedTestView.ToMbps(bps))}");
    }

    private async Task RunUploadAsync(CancellationToken ct)
    {
        LogLine($"上传测试：{UploadUrl}");
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
            LogLine($"上传：未在 {TimeBudgetSeconds}s 内完成（带宽较低或端点不可达），不报告数值。");
            return;
        }

        var bps = payload.Length / sw.Elapsed.TotalSeconds;
        LogLine($"上传：{SpeedTestView.FormatMbps(SpeedTestView.ToMbps(bps))}（8.0 MB / {sw.Elapsed.TotalSeconds:F1}s）");
    }

    private void LogLine(string line)
    {
        ResultsText.AppendText($"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");
        ResultsText.ScrollToEnd();
    }
}
