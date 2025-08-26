using HarmonyLib;
using Hazel;

namespace TownOfHostY
{
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.Serialize))]
    class GameManagerSerializeFix
    {
        public static bool Prefix(GameManager __instance, [HarmonyArgument(0)] MessageWriter writer, [HarmonyArgument(1)] bool initialState, ref bool __result)
        {
            bool flag = false;
            for (int index = 0; index < __instance.LogicComponents.Count; ++index)
            {
                GameLogicComponent logicComponent = __instance.LogicComponents[index];
                if (initialState || logicComponent.IsDirty)
                {
                    flag = true;
                    writer.StartMessage((byte)index);
                    bool hasBody = logicComponent.Serialize(writer); // initialState は不要
                    if (hasBody) writer.EndMessage();
                    else writer.CancelMessage();
                    logicComponent.ClearDirtyFlag();
                }
            }
            __instance.ClearDirtyBits();
            __result = flag;
            return false;
        }
    }
    


}