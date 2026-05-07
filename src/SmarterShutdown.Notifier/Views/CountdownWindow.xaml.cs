using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SmarterShutdown.Core.IPC;

namespace SmarterShutdown.Notifier.Views;

public partial class CountdownWindow : Window
{
    private readonly IPipeClient _client;
    private readonly DispatcherTimer _timer;
    private DateTime _scheduledAt;
    private int? _postponesRemaining;

    public CountdownWindow(IPipeClient client, DateTime scheduledAt, string message, int? postponesRemaining, int? maxPostpones, bool postponeAllowed)
    {
        InitializeComponent();
        _client = client;
        _scheduledAt = scheduledAt;
        _postponesRemaining = postponesRemaining;

        if (!string.IsNullOrWhiteSpace(message))
        {
            MessageText.Text = message;
        }

        UpdatePostponeUi(postponeAllowed, maxPostpones);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => RenderRemaining();
        _timer.Start();
        RenderRemaining();
    }

    public void UpdateScheduledAt(DateTime scheduledAt, int? postponesRemaining, int? maxPostpones, bool postponeAllowed)
    {
        _scheduledAt = scheduledAt;
        _postponesRemaining = postponesRemaining;
        UpdatePostponeUi(postponeAllowed, maxPostpones);
        RenderRemaining();
    }

    private void RenderRemaining()
    {
        var remaining = _scheduledAt - DateTime.Now;
        if (remaining <= TimeSpan.Zero)
        {
            CountdownText.Text = "0:00";
            _timer.Stop();
            return;
        }
        // mm:ss when under an hour, h:mm:ss otherwise.
        CountdownText.Text = remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}:{remaining.Minutes:D2}:{remaining.Seconds:D2}"
            : $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
    }

    private void UpdatePostponeUi(bool postponeAllowed, int? maxPostpones)
    {
        var canPostpone = postponeAllowed
            && (_postponesRemaining is null || _postponesRemaining > 0);
        PostponeButton.IsEnabled = canPostpone;

        if (!postponeAllowed)
        {
            PostponesText.Text = "Postpone disabled by policy";
        }
        else if (_postponesRemaining is null)
        {
            PostponesText.Text = string.Empty; // unlimited
        }
        else if (maxPostpones is not null)
        {
            PostponesText.Text = $"{_postponesRemaining} of {maxPostpones} postpones left";
        }
        else
        {
            PostponesText.Text = $"{_postponesRemaining} postpones left";
        }
    }

    private async void PostponeButton_Click(object sender, RoutedEventArgs e)
    {
        PostponeButton.IsEnabled = false;
        try
        {
            await _client.SendAsync(new PipeMessage { Type = MessageType.PostponeRequest }, CancellationToken.None);
        }
        catch
        {
            // Best-effort send. If pipe is mid-reconnect, the user can click again.
            PostponeButton.IsEnabled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
