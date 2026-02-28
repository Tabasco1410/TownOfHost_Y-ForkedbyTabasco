using HarmonyLib;

namespace TownOfHostY;

// ★ TOH-K移植: NetworkedPlayrInfoPatch.cs より
// Disconnected偽装中に NetworkedPlayerInfo.Serialize が自動送信するのを防ぐパッチ。
// ShowIntroForVanilla の処理中に data.Disconnected をローカルで書き換えても
// このパッチがなければ AmongUs が即座に Serialize/送信してしまい偽装が崩れる。

[HarmonyPatch(typeof(NetworkedPlayerInfo), nameof(NetworkedPlayerInfo.Serialize))]
class GameDataSerializePatch
{
    // >0 のとき Serialize を強制通過させる（明示的に送りたい場合）
    public static int SerializeMessageCount;
    // true のとき Serialize を完全ブロック
    public static bool DontTouch;

    public static bool Prefix(NetworkedPlayerInfo __instance, ref bool __result)
    {
        if (AmongUsClient.Instance == null || !GameStates.IsInGame)
        {
            __result = true;
            return true;
        }
        if (DontTouch)
        {
            __instance.ClearDirtyBits();
            __result = false;
            return false;
        }
        if (SerializeMessageCount > 0)
        {
            __result = true;
            return true;
        }
        return true;
    }
}

[HarmonyPatch(typeof(NetworkedPlayerInfo), nameof(NetworkedPlayerInfo.MarkDirty))]
class NetworkedPlayerInfoMarkDirtyPatch
{
    // DontTouch=true の間は MarkDirty もブロック
    public static bool Prefix() => GameDataSerializePatch.DontTouch is false;
}
