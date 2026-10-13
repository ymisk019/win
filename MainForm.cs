using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeckServer;

public class MainForm : Form
{
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _link = new() { ReadOnly = true, Dock = DockStyle.Top, Font = new Font("Consolas", 10) };
    private readonly Button _copy = new() { Text = "Copy link", Dock = DockStyle.Top, Height = 32 };
    private readonly CancellationTokenSource _cts = new();

    public MainForm()
    {
        Text = "Deck Server (Cloud)";
        Width = 620; Height = 380;
        Controls.Add(_log);
        Controls.Add(_copy);
        Controls.Add(_link);
        _copy.Click += (_, _) => { if (_link.Text.Length > 0) Clipboard.SetText(_link.Text); };
        Load += (_, _) => StartClient();
        FormClosing += (_, _) => _cts.Cancel();
    }

    private void StartClient()
    {
        try
        {
            var cfgPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            var cfg = JsonNode.Parse(File.ReadAllText(cfgPath))!;
            var db = cfg["databaseUrl"]!.GetValue<string>().Trim();
            var site = cfg["siteUrl"]!.GetValue<string>().Trim().TrimEnd('/');
            var room = cfg["room"]?.GetValue<string>() ?? "";

            if (db.Contains("YOUR-PROJECT") || site.Contains("YOUR-PROJECT"))
            {
                Log("Edit config.json first: set databaseUrl and siteUrl, then restart.");
                return;
            }

            if (string.IsNullOrWhiteSpace(room))
            {
                room = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(); // 32 chars, secret
                cfg["room"] = room;
                File.WriteAllText(cfgPath, cfg.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }

            _link.Text = $"{site}/#{room}";
            Log("Open the link above on your phone. Keep it private.");

            var client = new CloudClient(this, db, room);
            client.Log += Log;
            _ = Task.Run(() => client.RunAsync(_cts.Token));
        }
        catch (Exception ex) { Log("Startup error: " + ex.Message); }
    }

    private void Log(string msg)
    {
        if (IsDisposed) return;
        BeginInvoke(() => _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}"));
    }
}
