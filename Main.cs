using HarmonyLib;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
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

    [HarmonyPatch]
    public class ModInfo
    {
        [HarmonyPatch(typeof(Cosmoteer.Mods.ModInfo), "TryLoadMod")]
        [HarmonyPostfix]
        public static void TryLoadModPatch(ref Cosmoteer.Mods.ModInfo modInfo)
        {
            if (modInfo.ID == "radistmorse.attackaimlead")
            {
                // remove the warning from the description
                modInfo.Description = modInfo.Description?.Split(['\r', '\n']).First();
            }
        }
    }
}
