using Godot;
using System;
using Steamworks;
using System.Threading;
using System.Collections.Generic;
using MessagePack;
using static Godot.Projection;
using ArcaneNetworking;

[GlobalClass]
[Icon("res://addons/arcane-networking/icon/steam_manager.svg")]
public partial class SteamManager : Node
{
    public static SteamManager manager = null;
    public SteamManager() => manager ??= this;


    /// <summary>
    /// We are running the callbacks on physics process to make sure we get a consistent
    /// Call, physics process always runs at a specified tickrate
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        SteamAPI.RunCallbacks();
    }


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

            if (SteamAPI.RestartAppIfNecessary((AppId_t)3122440))
            {
                GetTree().Quit(2);
                GD.PrintErr("APPLICATION RESTARTING DUE TO STEAM ERROR...");
                return;
            }

            if (!SteamAPI.Init())
            {
                GetTree().Quit(1);
                GD.PrintErr("Steam API Init Failed..");
                return;
            }
            SteamNetworkingUtils.InitRelayNetworkAccess();

            GD.Print("Steam Is Connected! " + SteamFriends.GetPersonaName());
        }
        catch (Exception e)
        {
            GetTree().Quit(1);
            GD.PrintErr("Steam Err: " + e.Message);
            return;
        }
    }

    public override void _Notification(int what)
    {
        base._Notification(what);

        if (what == MainLoop.NotificationCrash || what == NotificationWMCloseRequest)
        {
            SteamAPI.Shutdown();

            GetTree().Quit();
        }
    }

}