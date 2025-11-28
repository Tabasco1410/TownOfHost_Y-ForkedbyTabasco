using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;

using TownOfHostY.Roles.Core;
using static TownOfHostY.Translator;

namespace TownOfHostY.Roles
{
    public static class RoleAssignManager
    {
        
        private static readonly int idStart = (int)Options.offsetId.FeatSpecial + 200;

        class RandomAssignOptions
        {
            public int Min => min();
            private Func<int> min;
            public int Max => max();
            private Func<int> max;

            private RandomAssignOptions(int id, OptionItem parent, CustomRoleTypes roleTypes, int maxCount)
            {
                var replacementDictionary = new Dictionary<string, string>()
                { { "%roleType%", GetString($"CustomRoleTypes.{roleTypes}") } };

                var minOption = IntegerOptionItem.Create(idStart + id + 1, "RoleTypeMin", new(0, maxCount, 1), 0, TabGroup.ModMainSettings, false)
                    .SetParent(parent)
                    .SetValueFormat(OptionFormat.Players);
                var maxOption = IntegerOptionItem.Create(idStart + id + 2, "RoleTypeMax", new(0, maxCount, 1), 0, TabGroup.ModMainSettings, false)
                    .SetParent(parent)
                    .SetValueFormat(OptionFormat.Players);

                minOption.ReplacementDictionary =
                maxOption.ReplacementDictionary = replacementDictionary;

                min = () => minOption.GetInt();
                max = () => maxOption.GetInt();

                RandomAssignOptionsCollection.Add(roleTypes, this);
            }
            public static RandomAssignOptions Create(int id, OptionItem parent, CustomRoleTypes roleTypes, int maxCount = 15)
                => new(id, parent, roleTypes, maxCount);
        }

        private static AssignAlgorithm AssignMode => assignMode();
        private static Func<AssignAlgorithm> assignMode;
        private enum AssignAlgorithm
        {
            Fixed,
            Random
        }

        private static readonly string[] AssignModeSelections =
        {
            "AssignAlgorithm.Fixed",
            "AssignAlgorithm.Random"
        };

        private static readonly CustomRoles[] AllMainRoles = CustomRolesHelper.AllMainRoles;
        public static OptionItem OptionAssignMode;
        private static readonly Dictionary<CustomRoleTypes, RandomAssignOptions> RandomAssignOptionsCollection = new(CustomRolesHelper.AllRoleTypes.Length);
        private static readonly Dictionary<CustomRoleTypes, int> AssignCount = new(CustomRolesHelper.AllRoleTypes.Length);
        private static readonly List<CustomRoles> AssignRoleList = new(CustomRolesHelper.AllRoles.Length);

        public static void SetupOptionItem()
        {
            OptionAssignMode = StringOptionItem.Create(idStart, "AssignMode", AssignModeSelections, 0, TabGroup.ModMainSettings, false)
                .SetColor(Palette.LightBlue);

            assignMode = () => (AssignAlgorithm)OptionAssignMode.GetInt();
            RandomAssignOptionsCollection.Clear();
            RandomAssignOptions.Create(10, OptionAssignMode, CustomRoleTypes.Impostor, 3);
            RandomAssignOptions.Create(20, OptionAssignMode, CustomRoleTypes.Madmate);
            RandomAssignOptions.Create(30, OptionAssignMode, CustomRoleTypes.Crewmate);
            RandomAssignOptions.Create(40, OptionAssignMode, CustomRoleTypes.Neutral);
        }

        public static bool CheckRoleCount()
        {
            if (AssignMode == AssignAlgorithm.Fixed) return true;
            var opt = Main.NormalOptions.Cast<IGameOptions>();

            var playerCount = GameData.Instance.PlayerCount;
            var numImpostors = Math.Min(playerCount, opt.GetInt(Int32OptionNames.NumImpostors));

            var impOptions = RandomAssignOptionsCollection[CustomRoleTypes.Impostor];

            var min = impOptions.Min;
            var max = impOptions.Max;
            if (min > max || min > numImpostors || max > numImpostors)
            {
                var msg = GetString("Warning.NotMatchImpostorCount");
                Logger.SendInGame(msg);
                Logger.Warn(msg, "BeginGame");
                return false;
            }

            var roleMinCount = 0;
            foreach (var options in RandomAssignOptionsCollection.Values)
                roleMinCount += options.Min;
            if (roleMinCount > playerCount)
            {
                var msg = GetString("Warning.NotMatchRoleCount");
                Logger.SendInGame(msg);
                Logger.Warn(msg, "BeginGame");
                return false;
            }

            return true;
        }

        public static void SelectAssignRoles()
        {
            AssignCount.Clear();
            AssignRoleList.Clear();

            switch (AssignMode)
            {
                case AssignAlgorithm.Fixed:
                    SetFixedAssignRole();
                    SetAddOnsList(true);
                    break;
                case AssignAlgorithm.Random:
                    SetRandomAssignCount();
                    SetRandomAssignRoleList();
                    SetAddOnsList(false);
                    break;
            }

            AssignRoleList.Sort();

            
            Logger.Info($"{string.Join(", ", AssignCount)}", "AssignCount");
            Logger.Info($"{string.Join(", ", AssignRoleList)}", "AssignRoleList");
        }

        ///<summary>
        /// 役職の固定アサイン抽選
        /// chanceが10%以上の役職を全て追加
        ///</summary>
        private static void SetFixedAssignRole()
        {
            int numImpostorsLeft = Math.Min(GameData.Instance.PlayerCount, Main.RealOptionsData.GetInt(Int32OptionNames.NumImpostors));
            int numOthersLeft = GameData.Instance.PlayerCount - numImpostorsLeft;

            foreach (var role in GetCandidateRoleList(10).OrderBy(x => Guid.NewGuid()))
            {
                if (numImpostorsLeft <= 0 && numOthersLeft <= 0) break;

                var targetRoles = role.GetAssignUnitRolesArray();
                var numImpostorAssign = targetRoles.Count(r => r.GetAssignRoleType() == CustomRoleTypes.Impostor);
                var numOthersAssign = targetRoles.Length - numImpostorAssign;

                if (numImpostorAssign > numImpostorsLeft || numOthersAssign > numOthersLeft) continue;

                AssignRoleList.AddRange(targetRoles);
                numImpostorsLeft -= numImpostorAssign;
                numOthersLeft -= numOthersAssign;
            }

            foreach (var roleType in CustomRolesHelper.AllRoleTypes)
            {
                var count = AssignRoleList.Count(r => r.GetAssignRoleType() == roleType);
                AssignCount.Add(roleType, count);
            }
        }

        ///<summary>
        /// 設定と実際の人数から各役職のアサイン数を決定
        ///</summary>
        private static void SetRandomAssignCount()
        {
            var rand = IRandom.Instance;
            int numImpostors = Math.Min(GameData.Instance.PlayerCount, Main.RealOptionsData.GetInt(Int32OptionNames.NumImpostors));
            int numOthers = GameData.Instance.PlayerCount - numImpostors;

            List<CustomRoleTypes> otherRoleTypesList = new();
            if (numOthers > 0)
            {
                var otherOpts = RandomAssignOptionsCollection.Where(x => x.Key != CustomRoleTypes.Impostor);
                foreach (var (roleType, options) in otherOpts)
                    otherRoleTypesList.AddRange(Enumerable.Repeat(roleType, options.Min));

                while (otherRoleTypesList.Count > numOthers)
                    otherRoleTypesList.RemoveAt(rand.Next(otherRoleTypesList.Count));

                int numAdditional = numOthers - otherRoleTypesList.Count;
                if (numAdditional > 0)
                {
                    List<CustomRoleTypes> additionalList = new();
                    foreach (var (roleType, options) in otherOpts)
                    {
                        int addCount = Math.Max(0, rand.Next(options.Max - options.Min + 1));
                        additionalList.AddRange(Enumerable.Repeat(roleType, addCount));
                    }
                    while (additionalList.Count > numAdditional)
                        additionalList.RemoveAt(rand.Next(additionalList.Count));
                    otherRoleTypesList.AddRange(additionalList);
                }
            }

            foreach (var (roleTypes, options) in RandomAssignOptionsCollection)
            {
                if (roleTypes == CustomRoleTypes.Impostor)
                {
                    int impAssignCount = Math.Min(numImpostors, rand.Next(options.Min, options.Max + 1));
                    AssignCount.Add(roleTypes, impAssignCount);
                }
                else
                {
                    AssignCount.Add(roleTypes, otherRoleTypesList.Count(x => x == roleTypes));
                }
            }
        }

        ///<summary>
        /// 役職のアサイン抽選
        /// 既に決まったアサイン枠数に合わせて決定
        ///</summary>
        private static void SetRandomAssignRoleList()
        {
            List<(CustomRoles, int)> randomRoleTicketPool = new();
            var rand = IRandom.Instance;
            var assignCount = new Dictionary<CustomRoleTypes, int>(AssignCount);

            foreach (var role in GetCandidateRoleList(100).OrderBy(x => Guid.NewGuid()))
            {
                var targetRoles = role.GetAssignUnitRolesArray();
                if (CustomRolesHelper.AllRoleTypes.Any(type =>
                        assignCount.TryGetValue(type, out var cnt) &&
                        targetRoles.Count(r => r.GetAssignRoleType() == type) > cnt))
                    continue;

                foreach (var target in targetRoles)
                {
                    AssignRoleList.Add(target);
                    var tType = target.GetAssignRoleType();
                    if (assignCount.ContainsKey(tType)) assignCount[tType]--;
                }
            }

            if (assignCount.All(kvp => kvp.Value <= 0)) return;

            foreach (var role in AllMainRoles.OrderBy(x => Guid.NewGuid()))
            {
                if (!role.IsAssignable()) continue;
                var chance = role.GetChance();
                var count = role.GetCount();
                if (chance is 0 or 100) continue;
                if (count == 0) continue;
                for (int i = 0; i < count; i++)
                    randomRoleTicketPool.AddRange(Enumerable.Repeat((role, i), chance / 10));
            }

            while (assignCount.Any(kvp => kvp.Value > 0) && randomRoleTicketPool.Count > 0)
            {
                var ticket = randomRoleTicketPool[rand.Next(randomRoleTicketPool.Count)];
                Logger.Info($"{ticket.Item1}", "SetRandomAssignRoleList");
                var targets = ticket.Item1.GetAssignUnitRolesArray();
                Logger.Info($"{targets}", "SetRandomAssignRoleList");

                if (CustomRolesHelper.AllRoleTypes.Where(t => t != CustomRoleTypes.Unit)
                    .All(t => targets.Count(r => r.GetAssignRoleType() == t) <= assignCount[t]))
                {
                    foreach (var trg in targets)
                    {
                        AssignRoleList.Add(trg);
                        assignCount[trg.GetAssignRoleType()]--;
                    }
                }

                randomRoleTicketPool.RemoveAll(x => x == ticket);
            }
        }

        ///<summary>
        /// 属性のアサイン抽選
        /// 枠制限が無いので個別に抽選
        ///</summary>
        private static void SetAddOnsList(bool isFixedAssign)
        {
            foreach (var subRole in CustomRolesHelper.AllAddOnRoles)
            {
                var chance = subRole.GetChance();
                var count = subRole.GetAssignCount();
                if (chance == 0 || count == 0) continue;
                var rnd = IRandom.Instance;
                for (int i = 0; i < count; i++)
                    if (isFixedAssign || rnd.Next(100) < chance)
                        AssignRoleList.AddRange(subRole.GetAssignUnitRolesArray());
            }
        }

        private static List<CustomRoles> GetCandidateRoleList(int availableRate)
        {
            var list = new List<CustomRoles>();
            foreach (var role in AllMainRoles)
            {
                if (!role.IsAssignable()) continue;
                if (!Options.IsCCMode && role.IsCCLeaderRoles()) continue;

                var chance = role.GetChance();
                var count = role.GetAssignCount();
                if (chance < availableRate || count == 0) continue;
                list.AddRange(Enumerable.Repeat(role, count));
            }
            return list;
        }

        public static RoleAssignInfo GetRoleAssignInfo(this CustomRoles role) =>
            CustomRoleManager.GetRoleInfo(role)?.AssignInfo;

        private static CustomRoleTypes GetAssignRoleType(this CustomRoles role) =>
            role.GetRoleAssignInfo()?.AssignRoleType ?? role.GetCustomRoleTypes();

        private static bool IsAssignable(this CustomRoles role)
            => role.GetRoleAssignInfo()?.IsInitiallyAssignable ?? true;

        /// <summary>
        /// アサインの抽選回数
        /// </summary>
        private static int GetAssignCount(this CustomRoles role)
        {
            int maximumCount = role.GetCount();
            int assignUnitCount = role.GetRoleAssignInfo()?.AssignUnitCount ??
                role switch
                {
                    CustomRoles.Lovers => 2,
                    _ => 1,
                };
            return maximumCount / assignUnitCount;
        }

        ///<summary>
        /// RoleOptionのKey => 実際にアサインされる役職の配列
        /// 両陣営役職、コンビ役職向け
        ///</summary>
        private static CustomRoles[] GetAssignUnitRolesArray(this CustomRoles role)
            => role.GetRoleAssignInfo()?.AssignUnitRoles ??
            role switch
            {
                CustomRoles.Lovers => new CustomRoles[2] { CustomRoles.Lovers, CustomRoles.Lovers },
                _ => new CustomRoles[1] { role },
            };

        public static bool IsPresent(this CustomRoles role) => AssignRoleList.Any(x => x == role);
        public static int GetRealCount(this CustomRoles role) => AssignRoleList.Count(x => x == role);
    }

    public class RoleAssignInfo
    {
        public RoleAssignInfo(CustomRoles role, CustomRoleTypes roleType)
        {
            AssignRoleType = roleType;
            IsInitiallyAssignableCallBack = () => true;
            AssignCountRule =
                roleType == CustomRoleTypes.Impostor ? new(1, 3, 1) : new(1, 15, 1);
            AssignUnitRoles =
                Enumerable.Repeat(role, AssignCountRule.Step).ToArray();
        }
        /// <summary>
        /// どのアサイン枠を消費するか
        /// </summary>
        public CustomRoleTypes AssignRoleType { get; init; }
        /// <summary>
        /// 試合開始時にアサインされるかどうか
        /// </summary>
        public Func<bool> IsInitiallyAssignableCallBack { get; init; }
        public bool IsInitiallyAssignable => IsInitiallyAssignableCallBack.Invoke();
        /// <summary>
        /// 人数設定の最小人数, 最大人数, 一単位数
        /// </summary>
        public IntegerValueRule AssignCountRule { get; init; }
        /// <summary>
        /// 一単位人数
        /// </summary>
        public int AssignUnitCount => AssignCountRule.Step;
        /// <summary>
        /// 実際にアサインされる役職の内訳
        /// </summary>
        public CustomRoles[] AssignUnitRoles { get; init; }
    }
}