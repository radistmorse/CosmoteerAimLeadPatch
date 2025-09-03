using HarmonyLib;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CosmoteerAim
{
    public class Main
    {
        private static Harmony? harmony;

        [UnmanagedCallersOnly]
        public static void InitializePatches()
        {
            harmony = new Harmony("CosmoteerAim");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Halfling.Logging.Logger.Log("[Aim Lead] Mod Loaded");
        }
    }
}
