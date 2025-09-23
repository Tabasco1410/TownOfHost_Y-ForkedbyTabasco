using AmongUs.GameOptions;
using HarmonyLib;

namespace TownOfHost_Y_ForkedbyTabasco.Patches
{
    [HarmonyPatch(typeof(LogicOptions), nameof(LogicOptions.GetKillDistance))]
    internal class KillDistancePatch
    {
        public static bool Prefix(LogicOptions __instance, ref float __result)
        {
            if (__instance == null)
            {
                __result = 0.5f; // フォールバック距離
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(GameOptionsFactory), nameof(GameOptionsFactory.ToBytes))]
    internal class SerializeGameOptionsPatch
    {
        // IL2CPP の実際のパラメータ名に合わせて 'data' に変更
        public static bool Prefix(ref IGameOptions data)
        {
            if (data == null)
            {
                return false; // バニラ処理に戻す
            }
            return true;
        }
    }
}
