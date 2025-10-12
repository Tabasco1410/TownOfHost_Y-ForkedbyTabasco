using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.AddOns.Crewmate;
using TownOfHostY.Roles.Crewmate;
using System.Linq;
using TownOfHostY.Roles.Neutral;

namespace TownOfHostY
{
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.AddTasksFromList))]
    class AddTasksFromListPatch
    {
        public static void Prefix(ShipStatus __instance,
            [HarmonyArgument(4)] Il2CppSystem.Collections.Generic.List<NormalPlayerTask> unusedTasks)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (!Options.DisableTasks.GetBool()) return;

            if (unusedTasks == null)
            {
                Logger.Warn("unusedTasks が null です - タスク無効化をスキップします", "AddTasksFromListPatch");
                return;
            }

            List<NormalPlayerTask> DisabledTasks = new();
            for (var i = 0; i < unusedTasks.Count; i++)
            {
                var task = unusedTasks[i];
                if (task == null) continue;

                if (task.TaskType == TaskTypes.SwipeCard && Options.DisableSwipeCard.GetBool()) DisabledTasks.Add(task);
                if (task.TaskType == TaskTypes.SubmitScan && Options.DisableSubmitScan.GetBool()) DisabledTasks.Add(task);
                if (task.TaskType == TaskTypes.UnlockSafe && Options.DisableUnlockSafe.GetBool()) DisabledTasks.Add(task);
                if (task.TaskType == TaskTypes.UploadData && Options.DisableUploadData.GetBool()) DisabledTasks.Add(task);
                if (task.TaskType == TaskTypes.StartReactor && Options.DisableStartReactor.GetBool()) DisabledTasks.Add(task);
                if (task.TaskType == TaskTypes.ResetBreakers && Options.DisableResetBreaker.GetBool()) DisabledTasks.Add(task);
                if (task.TaskType == TaskTypes.RewindTapes && Options.DisableRewindTapes.GetBool()) DisabledTasks.Add(task);
                if (task.TaskType == TaskTypes.VentCleaning && Options.DisableVentCleaning.GetBool()) DisabledTasks.Add(task);
                if (task.TaskType == TaskTypes.BuildSandcastle && Options.DisableBuildSandcastle.GetBool()) DisabledTasks.Add(task);
                if (task.TaskType == TaskTypes.TestFrisbee && Options.DisableTestFrisbee.GetBool()) DisabledTasks.Add(task);
                if (task.TaskType == TaskTypes.WaterPlants && Options.DisableWaterPlants.GetBool()) DisabledTasks.Add(task);
                if (task.TaskType == TaskTypes.CatchFish && Options.DisableCatchFish.GetBool()) DisabledTasks.Add(task);
                if (task.TaskType == TaskTypes.HelpCritter && Options.DisableHelpCritter.GetBool()) DisabledTasks.Add(task);
                if (task.TaskType == TaskTypes.TuneRadio && Options.DisableTuneRadio.GetBool()) DisabledTasks.Add(task);
                if (task.TaskType == TaskTypes.AssembleArtifact && Options.DisableAssembleArtifact.GetBool()) DisabledTasks.Add(task);
            }
            DisabledTasks.ForEach(task => unusedTasks.Remove(task));
        }
    }

    [HarmonyPatch(typeof(NetworkedPlayerInfo), nameof(NetworkedPlayerInfo.RpcSetTasks))]
    class RpcSetTasksPatch
    {
        public static void Prefix(NetworkedPlayerInfo __instance,
            [HarmonyArgument(0)] ref Il2CppStructArray<byte> taskTypeIds)
        {
            try
            {
                // ゲーム状態チェック
                if (Main.RealOptionsData == null)
                {
                    Logger.Error("CRITICAL: RealOptionsData が null です - ゲーム状態が破損しています", "RpcSetTasksPatch");
                    AmongUsClient.Instance.ExitGame(DisconnectReasons.ExitGame);
                    return;
                }

                if (__instance == null)
                {
                    Logger.Error("NetworkedPlayerInfo が null です", "RpcSetTasksPatch");
                    return;
                }

                var pc = Utils.GetPlayerById(__instance.PlayerId);
                if (pc == null)
                {
                    Logger.Warn($"PlayerId {__instance.PlayerId} のプレイヤーが見つかりません", "RpcSetTasksPatch");
                    return;
                }

                CustomRoles? RoleNullable = pc.GetCustomRole();
                if (RoleNullable == null)
                {
                    Logger.Warn($"{pc.name} のカスタム役職が null です", "RpcSetTasksPatch");
                    return;
                }

                CustomRoles role = RoleNullable.Value;

                // デフォルトのタスク数
                bool hasCommonTasks = true;
                int NumLongTasks = Main.NormalOptions.NumLongTasks;
                int NumShortTasks = Main.NormalOptions.NumShortTasks;

                // タスク数オーバーライド
                if (Options.OverrideTasksData.AllData.TryGetValue(role, out var data) && data.doOverride.GetBool())
                {
                    hasCommonTasks = data.assignCommonTasks.GetBool();
                    NumLongTasks = data.numLongTasks.GetInt();
                    NumShortTasks = data.numShortTasks.GetInt();
                }

                // 固有タスク処理
                if (pc.Is(CustomRoles.VentManager))
                    (hasCommonTasks, NumLongTasks, NumShortTasks) = VentManager.TaskData;
                if (pc.Is(CustomRoles.FoxSpirit))
                    (hasCommonTasks, NumLongTasks, NumShortTasks) = FoxSpirit.TaskData;
                if (pc.Is(CustomRoles.Workhorse))
                    (hasCommonTasks, NumLongTasks, NumShortTasks) = Workhorse.TaskData;
                if (pc.Is(CustomRoles.Rabbit) && Rabbit.IsFinish(pc))
                    (hasCommonTasks, NumLongTasks, NumShortTasks) = Rabbit.TaskData;

                if (taskTypeIds == null)
                {
                    Logger.Warn($"{pc.name} の taskTypeIds が null です", "RpcSetTasksPatch");
                    return;
                }

                if (taskTypeIds.Count == 0) hasCommonTasks = false;
                if (!hasCommonTasks && NumLongTasks == 0 && NumShortTasks == 0) NumShortTasks = 1;

                // 割り当て可能なタスクリスト
                Il2CppSystem.Collections.Generic.List<byte> TasksList = new();
                foreach (var num in taskTypeIds) TasksList.Add(num);

                // 共通タスク処理
                int defaultCommonTasksNum = Main.RealOptionsData.GetInt(Int32OptionNames.NumCommonTasks);
                if (hasCommonTasks)
                {
                    int removeStart = defaultCommonTasksNum;
                    int removeCount = TasksList.Count - defaultCommonTasksNum;
                    if (removeStart >= 0 && removeStart < TasksList.Count && removeCount > 0)
                        TasksList.RemoveRange(removeStart, removeCount);
                }
                else
                {
                    TasksList.Clear();
                }

                // ShipStatus チェック
                if (ShipStatus.Instance == null)
                {
                    Logger.Error("ShipStatus が null です - タスク割り当てをスキップします", "RpcSetTasksPatch");
                    return;
                }

                // タスク割り当て用のリストを作成
                Il2CppSystem.Collections.Generic.HashSet<TaskTypes> usedTaskTypes = new();
                int start2 = 0, start3 = 0;

                // LongTasks のコピーと null チェック
                var LongTasks = new Il2CppSystem.Collections.Generic.List<NormalPlayerTask>();
                if (ShipStatus.Instance.LongTasks != null)
                {
                    foreach (var task in ShipStatus.Instance.LongTasks)
                    {
                        if (task != null) LongTasks.Add(task);
                    }
                    Shuffle(LongTasks);
                }

                // ShortTasks のコピーと null チェック
                var ShortTasks = new Il2CppSystem.Collections.Generic.List<NormalPlayerTask>();
                if (ShipStatus.Instance.ShortTasks != null)
                {
                    foreach (var task in ShipStatus.Instance.ShortTasks)
                    {
                        if (task != null) ShortTasks.Add(task);
                    }
                    Shuffle(ShortTasks);
                }

                // VentManager / FoxSpirit 固有処理
                if (pc.Is(CustomRoles.VentManager) || pc.Is(CustomRoles.FoxSpirit))
                {
                    TasksList.Clear();
                    ShortTasks.Clear();

                    if (ShipStatus.Instance.ShortTasks != null)
                    {
                        var ventTask = ShipStatus.Instance.ShortTasks.FirstOrDefault(t => t != null && t.TaskType == TaskTypes.VentCleaning);
                        if (ventTask != null) ShortTasks.Add(ventTask);
                    }
                }

                // タスク割り当て（null チェック付き）
                if (LongTasks.Count > 0 || ShortTasks.Count > 0)
                {
                    ShipStatus.Instance.AddTasksFromList(ref start2, NumLongTasks, TasksList, usedTaskTypes, LongTasks);
                    ShipStatus.Instance.AddTasksFromList(ref start3, NumShortTasks, TasksList, usedTaskTypes, ShortTasks);
                }

                // 配列に変換
                taskTypeIds = new Il2CppStructArray<byte>(TasksList.Count);
                for (int i = 0; i < TasksList.Count; i++)
                    taskTypeIds[i] = TasksList[i];

                Logger.Info($"{pc.name} にタスク {TasksList.Count} 個を割り当てました (長: {NumLongTasks}, 短: {NumShortTasks})", "RpcSetTasksPatch");
            }
            catch (System.Exception ex)
            {
                Logger.Error($"RpcSetTasksPatch で例外が発生しました: {ex.Message}", "RpcSetTasksPatch");
                Logger.Exception(ex, "RpcSetTasksPatch");
                // ゲーム終了ではなく、元の処理を続行させる
                return;
            }
        }

        public static void Shuffle<T>(Il2CppSystem.Collections.Generic.List<T> list)
        {
            if (list == null || list.Count == 0) return;

            for (int i = 0; i < list.Count - 1; i++)
            {
                T obj = list[i];
                int rand = UnityEngine.Random.Range(i, list.Count);
                list[i] = list[rand];
                list[rand] = obj;
            }
        }
    }
}