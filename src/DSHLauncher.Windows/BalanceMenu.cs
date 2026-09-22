using System;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

#nullable enable

namespace DSHLauncher;

/// <summary>Windows-only balance UI. Credentials and balance are never written to app files.</summary>
internal sealed class BalanceMenu : IDisposable
{
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(15),
        MaxResponseContentBufferSize = 64 * 1024
    };
    private readonly ToolStripMenuItem refreshItem;
    private CancellationTokenSource? pendingRequest;
    private DateTime lastAttemptUtc = DateTime.MinValue;
    private bool disposed;
    private bool editing;

    public ToolStripMenuItem Item { get; } = new LauncherMenuItem("余额：未配置");

    public BalanceMenu()
    {
        refreshItem = new LauncherMenuItem("刷新余额", async (_, _) => await RefreshAsync(force: true));
        Item.DropDownItems.Add(refreshItem);
        Item.DropDownItems.Add(new LauncherMenuItem("设置／更换密钥…", async (_, _) => await SetKeyAsync()));
        Item.DropDownItems.Add(new LauncherMenuItem("清除密钥", async (_, _) => await ClearKeyAsync()));
    }

    public async Task RefreshAsync(bool force = false)
    {
        if (disposed || (!force && (pendingRequest is not null || DateTime.UtcNow - lastAttemptUtc < TimeSpan.FromMinutes(1))))
            return;

        pendingRequest?.Cancel();
        using var cancellation = new CancellationTokenSource();
        pendingRequest = cancellation;
        lastAttemptUtc = DateTime.UtcNow;
        refreshItem.Enabled = false;
        Item.Text = "余额：查询中…";
        Item.ToolTipText = string.Empty;

        try
        {
            string? key = WindowsCredentialStore.Read();
            if (key is null)
            {
                Item.Text = "余额：未配置";
                return;
            }

            // Use an explicit request header: no key in URLs, shell arguments or shared client defaults.
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            key = null;
            using HttpResponseMessage response = await client.SendAsync(request, cancellation.Token);
            if (!IsCurrent(cancellation)) return;

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                Item.Text = "余额：密钥无效或无权限";
                return;
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Item.Text = "余额：请求过于频繁";
                return;
            }
            if (!response.IsSuccessStatusCode)
            {
                Item.Text = "余额：查询失败";
                return;
            }

            string json = await response.Content.ReadAsStringAsync(cancellation.Token);
            if (!IsCurrent(cancellation)) return;
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            bool available = root.GetProperty("is_available").GetBoolean();
            var balances = root.GetProperty("balance_infos").EnumerateArray().Select(entry =>
            {
                string currency = entry.GetProperty("currency").GetString() ?? "";
                if (currency is not "CNY" and not "USD") throw new FormatException();
                string total = FormatAmount(entry.GetProperty("total_balance"));
                string granted = FormatAmount(entry.GetProperty("granted_balance"));
                string toppedUp = FormatAmount(entry.GetProperty("topped_up_balance"));
                return (Summary: $"{currency} {total}", Detail: $"{currency}：赠金 {granted}，充值 {toppedUp}");
            }).ToArray();
            if (balances.Length == 0) throw new FormatException();

            Item.Text = "余额：" + string.Join(" / ", balances.Select(balance => balance.Summary))
                + (available ? "" : "（不可用）");
            Item.ToolTipText = string.Join(Environment.NewLine, balances.Select(balance => balance.Detail))
                + $"{Environment.NewLine}更新于 {DateTime.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent(cancellation)) Item.Text = "余额：查询超时";
        }
        catch (Win32Exception)
        {
            if (IsCurrent(cancellation)) Item.Text = "余额：凭据读取失败";
        }
        catch (Exception)
        {
            // Never show raw exceptions, HTTP bodies or headers, which may contain sensitive data.
            if (IsCurrent(cancellation)) Item.Text = "余额：查询失败";
        }
        finally
        {
            if (ReferenceEquals(pendingRequest, cancellation))
            {
                pendingRequest = null;
                if (!disposed) refreshItem.Enabled = true;
            }
        }
    }

    private static string FormatAmount(JsonElement element)
    {
        decimal amount = decimal.Parse(element.GetString() ?? "", NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture);
        return amount.ToString("0.00##########################", CultureInfo.InvariantCulture);
    }

    private bool IsCurrent(CancellationTokenSource cancellation) =>
        !disposed && !cancellation.IsCancellationRequested && ReferenceEquals(pendingRequest, cancellation);

    private async Task SetKeyAsync()
    {
        if (disposed || editing) return;
        editing = true;
        try
        {
            using var dialog = new ApiKeyDialog();
            // The dialog always starts empty; the saved key is never loaded into it.
            if (dialog.ShowDialog() != DialogResult.OK || disposed) return;
            await RefreshAsync(force: true);
        }
        finally { editing = false; }
    }

    private async Task ClearKeyAsync()
    {
        if (disposed || editing) return;
        editing = true;
        try
        {
            if (MessageBox.Show("清除用于余额查询的密钥？DSH 自身的配置不受影响。", "清除密钥",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            WindowsCredentialStore.Delete();
            await RefreshAsync(force: true);
        }
        catch (Win32Exception)
        {
            MessageBox.Show("无法清除 Windows 凭据，请稍后重试。", "清除失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { editing = false; }
    }

    public void Dispose()
    {
        disposed = true;
        pendingRequest?.Cancel();
        client.Dispose();
    }
}

internal sealed class ApiKeyDialog : Form
{
    private readonly TextBox input = new()
    {
        UseSystemPasswordChar = true,
        MaxLength = 512,
        Dock = DockStyle.Fill,
        PlaceholderText = "输入新的 DeepSeek API 密钥"
    };

    public ApiKeyDialog()
    {
        Text = "设置余额查询密钥";
        Font = LauncherAppearance.Medium;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(440, 190);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.Controls.Add(new Label
        {
            Text = "密钥保存在此 Windows 用户的凭据管理器中。\n保存后不回显；输入新密钥将覆盖原密钥。",
            Dock = DockStyle.Fill
        }, 0, 0);
        layout.Controls.Add(input, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        var save = new Button { Text = "保存", AutoSize = true };
        save.Click += (_, _) => SaveKey();
        actions.Controls.Add(cancel);
        actions.Controls.Add(save);
        layout.Controls.Add(actions, 0, 2);
        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;
        Shown += (_, _) => input.Focus();
        FormClosed += (_, _) => input.Clear();
    }

    private void SaveKey()
    {
        string key = input.Text.Trim();
        if (key.Length == 0 || key.Any(character => character < 33 || character > 126))
        {
            MessageBox.Show(this, "请输入有效密钥，不能包含空白或非英文字符。", "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            WindowsCredentialStore.Write(key);
            input.Clear();
            DialogResult = DialogResult.OK;
        }
        catch (Win32Exception)
        {
            MessageBox.Show(this, "无法保存到 Windows 凭据管理器，请稍后重试。", "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
