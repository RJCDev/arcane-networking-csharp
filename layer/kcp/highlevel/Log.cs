// A simple logger class that uses Console.WriteLine by default.
// Can also do Logger.LogMethod = Debug.Log for Unity etc.
// (this way we don't have to depend on UnityEngine)
using System;
using Godot;

namespace kcp2k
{
    public static class Log
    {
        public static Action<string> Info = GD.Print;
        public static Action<string> Warning = GD.PushWarning;
        public static Action<string> Error = GD.PrintErr;
    }
}
