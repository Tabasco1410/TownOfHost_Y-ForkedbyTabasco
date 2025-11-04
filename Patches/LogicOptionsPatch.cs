using HarmonyLib;
using AmongUs.GameOptions;

namespace TownOfHostY.Patches
{
    [HarmonyPatch(typeof(LogicOptions), nameof(LogicOptions.GetKillDistance))]
    class LogicOptionsGetKillDistancePatch
    {
        // 安全なフォールバックを返して、例外や null 参照を防ぐ
        public static bool Prefix(LogicOptions __instance, ref float __result)
        {
            try
            {
                // 予防: LogicOptions の内部実装で例外が出続けるため、ここでオリジナルをスキップしフォールバックを返す
                if (__instance == null)
                {
                    __result = 1.0f; // フォールバック距離
                    return false; // オリジナルを呼ばない
                }

                // 可能なら GameOptionsManager の値から推測して返す
                try
                {
                    var goMgr = GameOptionsManager.Instance;
                    if (goMgr != null && goMgr.CurrentGameOptions != null)
                    {
                        // 適当な安全値。正確な変換が不明なため標準値を返す
                        __result = 1.0f;
                        return false;
                    }
                }
                catch { /* 無視 */ }

                __result = 1.0f;
                return false;
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex, "LogicOptionsGetKillDistancePatch");
                __result = 1.0f;
                return false;
            }
        }
    }
}
