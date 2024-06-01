using VRage.Utils;

namespace SimpleStore.StoreBlock
{
    public static class MyLogExtension
    {
        public static void WriteLineIf(this MyLog myLog, bool enable, string message)
        {
            if (enable)
                myLog.WriteLine($"[DEBUG] {message}");
        }
    }
}
