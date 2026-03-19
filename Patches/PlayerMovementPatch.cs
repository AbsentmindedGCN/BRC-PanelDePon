using HarmonyLib;
using Reptile;

namespace BRCPanelPon
{
    public static class PanelPonState
    {
        public static bool AppActive = false;
    }

    [HarmonyPatch(typeof(Player), "SetInputs")]
    public static class Player_SetInputs_PanelPonPatch
    {
        private static bool Prefix(Player __instance)
        {
            if (!PanelPonState.AppActive)
                return true;

            __instance.FlushInput();
            return false;
        }
    }
}