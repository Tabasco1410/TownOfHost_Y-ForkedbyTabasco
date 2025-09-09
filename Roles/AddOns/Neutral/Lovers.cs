using System.Collections.Generic;
using UnityEngine;
using TownOfHostY.Roles.Core;
using static TownOfHostY.Options;
using TownOfHostY.Attributes;

namespace TownOfHostY.Roles.AddOns.Common;

public static class Lovers
{
    private static readonly int Id = (int)offsetId.AddonNeu + 0;
    private static Color RoleColor = Utils.GetRoleColor(CustomRoles.Lovers);
    public static List<PlayerControl> playersList = new(2);
    private static bool isLoversDead = false;
    public static OptionItem LoversAddWin;

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.Lovers, (1, 1, 1));
        LoversAddWin = StringOptionItem.Create(Id + 10, "LoversAddWin", new string[] { "OFF", "ON" }, false, TabGroup.Addons, false).SetParent(CustomRoleSpawnChances[CustomRoles.Lovers]);
    }
    [GameModuleInitializer]
    public static void Init()
    {
        playersList = new();
        isLoversDead = false;
    }
    public static void Add(byte playerId)
    {
        var lover = Utils.GetPlayerById(playerId);
        if (!playersList.Contains(lover))
            playersList.Add(lover);
    }
    public static bool IsEnable => playersList.Count > 0;
    public static bool IsThisRole(byte playerId)
    {
        var pc = Utils.GetPlayerById(playerId);
        return playersList.Contains(pc);
    }

    public static void KillSuicide(byte deadTargetId)
    {
        var target = Utils.GetPlayerById(deadTargetId);
        if (!playersList.Contains(target) || isLoversDead) return;

        isLoversDead = true;

        foreach (var lover in playersList)
        {
            if (lover == target) continue;

            lover.RpcMurderPlayer(lover);
            lover.SetRealKiller(target);
            PlayerState.GetByPlayerId(lover.PlayerId).DeathReason = CustomDeathReason.FollowingSuicide;
            Logger.Info($"{target.GetNameWithRole()}のLover後追い:{lover.GetNameWithRole()}", "KillFollowingSuicide");
        }
    }
    public static void VoteSuicide(byte deadTargetId)
    {
        var target = Utils.GetPlayerById(deadTargetId);
        if (!playersList.Contains(target) || isLoversDead) return;

        isLoversDead = true;

        foreach (var lover in playersList)
        {
            if (lover == target) continue;

            MeetingHudPatch.TryAddAfterMeetingDeathPlayers(CustomDeathReason.FollowingSuicide, lover.PlayerId);
            lover.SetRealKiller(target);
            Logger.Info($"{target.GetNameWithRole()}のLover後追い:{lover.GetNameWithRole()}", "VoteFollowingSuicide");
        }
    }

    public static string GetMark(PlayerControl seer, PlayerControl seen = null)
    {
        //seenが省略の場合seer
        seen ??= seer;

        if (seer.Is(CustomRoles.Lovers) && seen.Is(CustomRoles.Lovers))
            return "♥".Color(RoleColor);
        if (!seer.IsAlive() && seen.Is(CustomRoles.Lovers))
            return "♥".Color(RoleColor);

        return string.Empty;
    }
}