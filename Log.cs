using Sandbox.Game;
using VRage.Utils;
using VRageMath;

namespace SimpleStore
{
    public static class Log
    {
        public static string Prefix = "SimpleStore";
        public static bool Debug;

        public static void Msg(string msg)
        {
            MyLog.Default.WriteLine($"{Prefix} {msg}");
        }

        public static void Msg(string msg, long playerId)
        {
            MyLog.Default.WriteLine($"{Prefix}: {msg}");
            MyVisualScriptLogicProvider.SendChatMessageColored(msg, Color.Blue, Prefix, playerId);
        }
    }
}
