using HarmonyLib;
using Hazel;

namespace TownOfHostY
{
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.Serialize))]
    class GameManagerSerializeFix
    {
        public static bool Prefix(GameManager __instance, [HarmonyArgument(0)] MessageWriter writer, [HarmonyArgument(1)] bool initialState, ref bool __result)
        {
            // null チェック
            if (__instance == null)
            {
                Logger.Error("GameManager が null です", "GameManagerSerializeFix");
                __result = false;
                return false;
            }

            if (__instance.LogicComponents == null)
            {
                Logger.Error("LogicComponents が null です", "GameManagerSerializeFix");
                __result = false;
                return false;
            }

            if (writer == null)
            {
                Logger.Error("MessageWriter が null です", "GameManagerSerializeFix");
                __result = false;
                return false;
            }

            bool flag = false;
            for (int index = 0; index < __instance.LogicComponents.Count; ++index)
            {
                GameLogicComponent logicComponent = __instance.LogicComponents[index];

                // LogicComponent が null の場合はスキップ
                if (logicComponent == null)
                {
                    Logger.Warn($"LogicComponent[{index}] が null です - スキップします", "GameManagerSerializeFix");
                    continue;
                }

                if (initialState || logicComponent.IsDirty)
                {
                    flag = true;
                    writer.StartMessage((byte)index);

                    try
                    {
                        bool hasBody = logicComponent.Serialize(writer);
                        if (hasBody) writer.EndMessage();
                        else writer.CancelMessage();
                        logicComponent.ClearDirtyFlag();
                    }
                    catch (System.NullReferenceException ex)
                    {
                        Logger.Error($"LogicComponent[{index}].Serialize で NullReferenceException が発生しました: {ex.Message}",
                                    "GameManagerSerializeFix");
                        Logger.Exception(ex, "GameManagerSerializeFix");
                        writer.CancelMessage();
                        // ゲーム終了
                        AmongUsClient.Instance.ExitGame(DisconnectReasons.ExitGame);
                        __result = false;
                        return false;
                    }
                    catch (System.Exception ex)
                    {
                        Logger.Error($"LogicComponent[{index}].Serialize で予期しない例外が発生しました: {ex.Message}",
                                    "GameManagerSerializeFix");
                        Logger.Exception(ex, "GameManagerSerializeFix");
                        writer.CancelMessage();
                        __result = false;
                        return false;
                    }
                }
            }

            __instance.ClearDirtyBits();
            __result = flag;
            return false;
        }
    }
}