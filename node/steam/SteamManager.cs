using Godot;
using System;
using Steamworks;

[GlobalClass]
[Icon("res://addons/arcane-networking/icon/steam_manager.svg")]
public partial class SteamManager : Node
{
    [Export] uint AppID = 480;
    public static CSteamID MySteamID;
    public static SteamManager manager = null;
    public SteamManager() => manager ??= this;

    public override void _Process(double delta) => SteamAPI.RunCallbacks();
    public override void _EnterTree()
    {
        GD.Print("Starting Steam Manager..");
        try
        {
            if (!Packsize.Test())
            {
                GD.PrintErr("[Steamworks.NET] Packsize Test returned false, the wrong version of Steamworks.NET is being run in this platform.", this);
                GetTree().Quit(4);
            }

            if (!DllCheck.Test())
            {
                GD.PrintErr("[Steamworks.NET] DllCheck Test returned false, One or more of the Steamworks binaries seems to be the wrong version.", this);
                GetTree().Quit(3);
            }

            if (SteamAPI.RestartAppIfNecessary((AppId_t)AppID))
            {
                GetTree().Quit(2);
                GD.PrintErr("[Steamworks.NET] APPLICATION RESTARTING DUE TO STEAM ERROR...");
                return;
            }

            if (!SteamAPI.Init())
            {
                GetTree().Quit(1);
                GD.PrintErr("[Steamworks.NET] Steam API Init Failed.. Please Open Steam");
                return;
            }

            SteamNetworkingUtils.InitRelayNetworkAccess();

            GD.Print("[Steamworks.NET] Steam Is Connected! " + SteamFriends.GetPersonaName());
        }
        catch (Exception e)
        {
            GetTree().Quit(1);
            GD.PrintErr("[Steamworks.NET] Steam Err: " + e.Message);
            return;
        }

        MySteamID = SteamUser.GetSteamID();
    }
    public override void _Notification(int what)
    {
        base._Notification(what);

        if (what == MainLoop.NotificationCrash || what == NotificationWMCloseRequest)
        {
            SteamAPI.Shutdown();
        }
    }

}