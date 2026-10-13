using System.Text;
using System.Text.Json.Nodes;

namespace DeckServer;

// Talks to Firebase Realtime Database over its REST API.
//   rooms/<room>/buttons   <- PC publishes the button list
//   rooms/<room>/online    <- PC heartbeat (value changes every few seconds)
//   rooms/<room>/commands  <- phone pushes {"id": "..."}; PC executes then deletes
public class CloudClient
{
    public event Action<string>? Log;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _db;
    private readonly string _room;
    private readonly Control _ui;
    private bool _ok = true;
    private bool _first = true;

    public CloudClient(Control ui, string databaseUrl, string room)
    {
        _ui = ui;
        _db = databaseUrl.TrimEnd('/');
        _room = room;
    }

    private string Url(string path) => $"{_db}/rooms/{_room}/{path}.json";

    private static JsonNode LoadButtons() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "buttons.json")))!;

    private async Task Put(string path, string json)
    {
        var res = await _http.PutAsync(Url(path), new StringContent(json, Encoding.UTF8, "application/json"));
        if (!res.IsSuccessStatusCode)
            throw new Exception($"{(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
    }

    private async Task PublishButtons()
    {
        var arr = new JsonArray();
        foreach (var b in LoadButtons()["buttons"]!.AsArray())
        {
            arr.Add(new JsonObject
            {
                ["id"] = b!["id"]!.GetValue<string>(),
                ["label"] = b["label"]!.GetValue<string>(),
                ["icon"] = b["icon"]?.GetValue<string>() ?? ""
            });
        }
        await Put("buttons", arr.ToJsonString());
    }

    private async Task Poll()
    {
        var res = await _http.GetAsync(Url("commands"));
        if (!res.IsSuccessStatusCode)
            throw new Exception($"{(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        var text = await res.Content.ReadAsStringAsync();

        if (_first)
        {
            // Ignore anything pressed while the PC app was off.
            _first = false;
            if (text != "null") await _http.DeleteAsync(Url("commands"));
            return;
        }
        if (text == "null" || string.IsNullOrWhiteSpace(text)) return;

        if (JsonNode.Parse(text) is not JsonObject cmds) return;
        foreach (var kv in cmds.ToList())
        {
            var id = kv.Value?["id"]?.GetValue<string>();
            await _http.DeleteAsync(Url($"commands/{kv.Key}"));   // delete first so it never runs twice
            if (id != null) Run(id);
        }
    }

    // Only buttons defined in the local buttons.json can run. The phone sends an id, never a command.
    private void Run(string id)
    {
        foreach (var b in LoadButtons()["buttons"]!.AsArray())
        {
            if (b!["id"]!.GetValue<string>() == id)
            {
                Log?.Invoke($"Press: {id}");
                var action = b["action"]!;
                try { _ui.Invoke(() => ActionRunner.Run(action)); }
                catch (Exception ex) { Log?.Invoke("Action error: " + ex.Message); }
                return;
            }
        }
        Log?.Invoke($"Unknown button: {id}");
    }

    public async Task RunAsync(CancellationToken ct)
    {
        int tick = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (tick % 50 == 0) await PublishButtons();               // ~every 20 s (picks up buttons.json edits)
                if (tick % 10 == 0)
                    await Put("online", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());  // heartbeat ~4 s
                await Poll();
                tick++;
                if (!_ok) { _ok = true; Log?.Invoke("Cloud connection restored"); }
            }
            catch (Exception ex)
            {
                if (_ok) { _ok = false; Log?.Invoke("Cloud error: " + ex.Message); }
            }
            try { await Task.Delay(400, ct); } catch { return; }
        }
    }
}
