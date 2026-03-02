using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using TownOfHostY.Roles;
using TownOfHostY.Roles.Core;

namespace TownOfHostY;

[HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
class SelectRolesHASModePatch
{
    public static bool Prefix(RoleManager __instance)
    {
        if (!AmongUsClient.Instance.AmHost) return true;

        if (!Options.IsHASMode) return true;


        Dictionary<byte, CustomRpcSender> senders = new();
        foreach (var pc in Main.AllPlayerControls)
        {
            senders[pc.PlayerId] = new CustomRpcSender($"{pc.name}'s SetRole Sender", SendOption.None, false)
                .StartMessage(pc.GetClientId());
        }
        RpcSetRoleReplacer.StartReplace(senders);

        RoleAssignManager.SelectAssignRoles();

        List<PlayerControl> AllPlayers = new();
        foreach (var pc in Main.AllPlayerControls)
            AllPlayers.Add(pc);

        //バニラの役職割り当て
        SelectRolesPatch.AssignRolesNormal(null, 0);

        return false;
    }

    public static void Postfix()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        if (!Options.IsHASMode) return;

        //MODの役職割り当て
        RpcSetRoleReplacer.Release();
        RpcSetRoleReplacer.senders.Do(kvp => kvp.Value.SendMessage());

        //リセット処理
        RpcSetRoleReplacer.senders = null;
        RpcSetRoleReplacer.OverriddenSenderList = null;
        RpcSetRoleReplacer.StoragedData = null;
        SelectRolesPatch.Disconnected.Clear();

        //HAS役職割り当て
        List<PlayerControl> Crewmates = new();
        List<PlayerControl> Impostors = new();

        foreach (var pc in Main.AllPlayerControls)
        {
            pc.Data.IsDead = false;
            var state = PlayerState.GetByPlayerId(pc.PlayerId);
            if (state.GetNowMainRole() != CustomRoles.NotAssigned) continue;

            var role = CustomRoles.NotAssigned;
            switch (pc.Data.Role.Role)
            {
                case RoleTypes.Crewmate: Crewmates.Add(pc); role = CustomRoles.Crewmate; break;
                case RoleTypes.Impostor: Impostors.Add(pc); role = CustomRoles.Impostor; break;
                default:
                    Logger.SendInGame(string.Format(Translator.GetString("Error.InvalidRoleAssignment"), pc?.Data?.PlayerName));
                    break;
            }
            if (role != CustomRoles.NotAssigned)
                state.SetMainRole(role);
        }

        //色設定処理
        SetColorPatch.IsAntiGlitchDisabled = true;
        foreach (var pc in Main.AllPlayerControls)
        {
            if (pc.Is(CustomRoleTypes.Impostor))
                pc.RpcSetColor(0);
            else if (pc.Is(CustomRoleTypes.Crewmate))
                pc.RpcSetColor(1);
        }

        var roleTypePlayers = SelectRolesPatch.GetRoleTypePlayers();
        //役職設定処理
        if (roleTypePlayers.TryGetValue(RoleTypes.Crewmate, out var list))
        {
            SelectRolesPatch.AssignCustomRolesFromList(CustomRoles.HASFox, list);
            SelectRolesPatch.AssignCustomRolesFromList(CustomRoles.HASTroll, list);
        }

        foreach (var pair in PlayerState.AllPlayerStates)
        {
            //RPCによる同期
            ExtendedPlayerControl.RpcSetCustomRole(pair.Key, pair.Value.GetNowMainRole());
        }

        SetColorPatch.IsAntiGlitchDisabled = false;

        GameEndChecker.SetPredicateToHideAndSeek();

        Utils.CountAlivePlayers(true);
        Utils.SyncAllSettings();
    }
}