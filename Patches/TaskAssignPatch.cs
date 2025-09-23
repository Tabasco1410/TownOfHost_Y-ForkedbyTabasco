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

            List<NormalPlayerTask> DisabledTasks = new();
            for (var i = 0; i < unusedTasks.Count; i++)
            {
                var task = unusedTasks[i];
                if (task == null) continue; // null対策

                if (task.TaskType == TaskTypes.SwipeCard && Options.DisableSwipeCard.GetBool()) DisabledTasks.Add(task); // カードタスク
                if (task.TaskType == TaskTypes.SubmitScan && Options.DisableSubmitScan.GetBool()) DisabledTasks.Add(task); // スキャンタスク
                if (task.TaskType == TaskTypes.UnlockSafe && Options.DisableUnlockSafe.GetBool()) DisabledTasks.Add(task); // 金庫タスク
                if (task.TaskType == TaskTypes.UploadData && Options.DisableUploadData.GetBool()) DisabledTasks.Add(task); // アップロードタスク
                if (task.TaskType == TaskTypes.StartReactor && Options.DisableStartReactor.GetBool()) DisabledTasks.Add(task); // リアクター3x3
                if (task.TaskType == TaskTypes.ResetBreakers && Options.DisableResetBreaker.GetBool()) DisabledTasks.Add(task); // レバータスク
                if (task.TaskType == TaskTypes.RewindTapes && Options.DisableRewindTapes.GetBool()) DisabledTasks.Add(task); // テープ巻き戻し
                if (task.TaskType == TaskTypes.VentCleaning && Options.DisableVentCleaning.GetBool()) DisabledTasks.Add(task); // ベント掃除
                if (task.TaskType == TaskTypes.BuildSandcastle && Options.DisableBuildSandcastle.GetBool()) DisabledTasks.Add(task); // 砂のお城
                if (task.TaskType == TaskTypes.TestFrisbee && Options.DisableTestFrisbee.GetBool()) DisabledTasks.Add(task); // フリスビー
                if (task.TaskType == TaskTypes.WaterPlants && Options.DisableWaterPlants.GetBool()) DisabledTasks.Add(task); // 植物に水やり
                if (task.TaskType == TaskTypes.CatchFish && Options.DisableCatchFish.GetBool()) DisabledTasks.Add(task); // 魚釣り
                if (task.TaskType == TaskTypes.HelpCritter && Options.DisableHelpCritter.GetBool()) DisabledTasks.Add(task); // 卵
                if (task.TaskType == TaskTypes.TuneRadio && Options.DisableTuneRadio.GetBool()) DisabledTasks.Add(task); // ラジオ調整
                if (task.TaskType == TaskTypes.AssembleArtifact && Options.DisableAssembleArtifact.GetBool()) DisabledTasks.Add(task); // 遺物組み立て
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
            // null対策
            if (Main.RealOptionsData == null)
            {
                Logger.Warn("警告: RealOptionsData が null", "RpcSetTasksPatch");
                return;
            }

            var pc = Utils.GetPlayerById(__instance.PlayerId);
            if (pc == null) return;

            CustomRoles? RoleNullable = pc.GetCustomRole();
            if (RoleNullable == null) return;
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

            if (taskTypeIds == null) return;
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

            // タスク割り当て
            Il2CppSystem.Collections.Generic.HashSet<TaskTypes> usedTaskTypes = new();
            int start2 = 0, start3 = 0;

            var LongTasks = new Il2CppSystem.Collections.Generic.List<NormalPlayerTask>();
            foreach (var task in ShipStatus.Instance.LongTasks) if (task != null) LongTasks.Add(task);
            Shuffle(LongTasks);

            var ShortTasks = new Il2CppSystem.Collections.Generic.List<NormalPlayerTask>();
            foreach (var task in ShipStatus.Instance.ShortTasks) if (task != null) ShortTasks.Add(task);
            Shuffle(ShortTasks);

            // VentManager / FoxSpirit 固有処理
            if (pc.Is(CustomRoles.VentManager) || pc.Is(CustomRoles.FoxSpirit))
            {
                TasksList.Clear();
                ShortTasks.Clear();
                var ventTask = ShipStatus.Instance.ShortTasks.FirstOrDefault(t => t?.TaskType == TaskTypes.VentCleaning);
                if (ventTask != null) ShortTasks.Add(ventTask);
            }

            ShipStatus.Instance.AddTasksFromList(ref start2, NumLongTasks, TasksList, usedTaskTypes, LongTasks);
            ShipStatus.Instance.AddTasksFromList(ref start3, NumShortTasks, TasksList, usedTaskTypes, ShortTasks);

            // 配列に変換
            taskTypeIds = new Il2CppStructArray<byte>(TasksList.Count);
            for (int i = 0; i < TasksList.Count; i++)
                taskTypeIds[i] = TasksList[i];
        }

        public static void Shuffle<T>(Il2CppSystem.Collections.Generic.List<T> list)
        {
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
