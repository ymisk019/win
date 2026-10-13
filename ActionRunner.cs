using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace DeckServer;

public static class ActionRunner
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    public static void Run(JsonNode action)
    {
        var type = action["type"]?.GetValue<string>();
        var target = action["target"]?.GetValue<string>() ?? "";
        switch (type)
        {
            case "open":
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                break;
            case "keys":
                // SendKeys syntax: ^ = Ctrl, % = Alt, + = Shift, e.g. "^c", "%{F4}"
                SendKeys.SendWait(target);
                break;
            case "media":
                Media(target);
                break;
        }
    }

    private static void Media(string name)
    {
        byte vk = name switch
        {
            "vol_up" => 0xAF,
            "vol_down" => 0xAE,
            "mute" => 0xAD,
            "next" => 0xB0,
            "prev" => 0xB1,
            "play_pause" => 0xB3,
            _ => 0
        };
        if (vk == 0) return;
        keybd_event(vk, 0, 0, UIntPtr.Zero);   // key down
        keybd_event(vk, 0, 2, UIntPtr.Zero);   // key up
    }
}
