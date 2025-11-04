using System;
using HarmonyLib;
using Hazel;
using AmongUs.GameOptions;
using System.Reflection;
using System.Text;
using System.IO;

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
                        bool hasBody = false;

                        // LogicOptions (ゲーム設定) は特に不安定なので特別扱い
                        if (logicComponent.GetType().Name == "LogicOptions")
                        {
                            try
                            {
                                // Try normal serialize first; if it throws, perform diagnostics and attempt repair
                                try
                                {
                                    hasBody = logicComponent.Serialize(writer);
                                }
                                catch (System.Exception firstEx)
                                {
                                    Logger.Error($"LogicComponent[{index}] (LogicOptions) 初回Serializeで例外: {firstEx.Message}", "GameManagerSerializeFix");
                                    Logger.Exception(firstEx, "GameManagerSerializeFix");

                                    // ダンプと修復を試みる
                                    var dump = DumpLogicOptionsState(logicComponent);
                                    SaveDiagnosticDump(dump);

                                    bool repaired = AttemptRepairLogicOptions(logicComponent);
                                    Logger.Info($"LogicOptions 修復を試みました: 成功={repaired}", "GameManagerSerializeFix");

                                    // 修復後に再試行
                                    try
                                    {
                                        hasBody = logicComponent.Serialize(writer);
                                    }
                                    catch (System.Exception secondEx)
                                    {
                                        Logger.Error($"LogicComponent[{index}] (LogicOptions) 再Serializeで例外: {secondEx.Message}", "GameManagerSerializeFix");
                                        Logger.Exception(secondEx, "GameManagerSerializeFix");

                                        // 最終手段: 安全に空ボディを書き込んで継続する
                                        hasBody = SafeWriteEmptyLogicOptionsBody(writer);
                                    }
                                }
                            }
                            catch (System.Exception ex)
                            {
                                Logger.Error($"LogicComponent[{index}] (LogicOptions) の安全シリアライズに失敗: {ex.Message}", "GameManagerSerializeFix");
                                Logger.Exception(ex, "GameManagerSerializeFix");
                                writer.CancelMessage();
                                continue;
                            }
                        }
                        else
                        {
                            try
                            {
                                hasBody = logicComponent.Serialize(writer);
                            }
                            catch (System.NullReferenceException ex)
                            {
                                Logger.Error($"LogicComponent[{index}].Serialize で NullReferenceException が発生しました: {ex.Message}",
                                            "GameManagerSerializeFix");
                                Logger.Exception(ex, "GameManagerSerializeFix");
                                writer.CancelMessage();
                                continue;
                            }
                        }

                        if (hasBody) writer.EndMessage();
                        else writer.CancelMessage();
                        logicComponent.ClearDirtyFlag();
                    }
                    catch (System.Exception ex)
                    {
                        Logger.Error($"LogicComponent[{index}].Serialize で予期しない例外が発生しました: {ex.Message}",
                                    "GameManagerSerializeFix");
                        Logger.Exception(ex, "GameManagerSerializeFix");
                        writer.CancelMessage();
                        // 重大な例外はループ継続で他コンポーネントに影響を与えない
                        continue;
                    }
                }
            }

            __instance.ClearDirtyBits();
            __result = flag;
            return false;
        }

        private static string DumpLogicOptionsState(object logicOptions)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("==== LogicOptions State Dump ====");
                sb.AppendLine($"Type: {logicOptions.GetType().FullName}");

                var fields = logicOptions.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var f in fields)
                {
                    object val = null;
                    try { val = f.GetValue(logicOptions); } catch (System.Exception) { val = "<get failed>"; }
                    sb.AppendLine($"Field: {f.Name} = {(val == null ? "<null>" : val.ToString())}");
                }

                var props = logicOptions.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var p in props)
                {
                    object val = null;
                    try { if (p.GetIndexParameters().Length == 0) val = p.GetValue(logicOptions); else val = "<indexed>"; } catch (System.Exception) { val = "<get failed>"; }
                    sb.AppendLine($"Prop: {p.Name} = {(val == null ? "<null>" : val.ToString())}");
                }

                return sb.ToString();
            }
            catch (System.Exception ex)
            {
                return "Dump failed: " + ex.Message;
            }
        }

        private static void SaveDiagnosticDump(string dump)
        {
            try
            {
                var dir = Utils.GetLogFolder(true);
                var path = Path.Combine(dir.FullName, $"LogicOptionsDump-{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(path, dump);
                Logger.Info($"LogicOptions のダンプを保存しました: {path}", "GameManagerSerializeFix");
            }
            catch (System.Exception ex)
            {
                Logger.Error($"ダンプ保存に失敗しました: {ex.Message}", "GameManagerSerializeFix");
            }
        }

        private static bool AttemptRepairLogicOptions(object logicOptions)
        {
            try
            {
                // 可能なら GameOptionsManager の現在のオプションを再設定して内部状態を初期化する
                var go = GameOptionsManager.Instance?.CurrentGameOptions;
                if (go == null) return false;

                var mi = logicOptions.GetType().GetMethod("SetGameOptions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null)
                {
                    mi.Invoke(logicOptions, new object[] { go });
                    // さらに SyncOptions があれば呼ぶ
                    var ms = logicOptions.GetType().GetMethod("SyncOptions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    ms?.Invoke(logicOptions, null);
                    return true;
                }

                return false;
            }
            catch (System.Exception ex)
            {
                Logger.Error($"LogicOptions 修復処理で例外: {ex.Message}", "GameManagerSerializeFix");
                Logger.Exception(ex, "GameManagerSerializeFix");
                return false;
            }
        }

        private static bool SafeWriteEmptyLogicOptionsBody(MessageWriter writer)
        {
            try
            {
                // 最終手段で空のバイト列を書き込み、Hazel側でNREを起こさないようにする
                var dummy = new byte[0];
                var mi = typeof(MessageWriter).GetMethod("WriteBytesAndSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null)
                {
                    mi.Invoke(writer, new object[] { dummy });
                    return true;
                }
                return false;
            }
            catch (System.Exception ex)
            {
                Logger.Error($"SafeWriteEmptyLogicOptionsBody で例外: {ex.Message}", "GameManagerSerializeFix");
                Logger.Exception(ex, "GameManagerSerializeFix");
                return false;
            }
        }
    }
}