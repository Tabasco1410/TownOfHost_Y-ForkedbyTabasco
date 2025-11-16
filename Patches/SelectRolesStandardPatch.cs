using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;

using TownOfHostY.Roles;
using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;

namespace TownOfHostY;

[HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
static class SelectRolesStandardPatch
{
    public static bool Prefix()
    {
        if (!AmongUsClient.Instance.AmHost) return true;
        if (Options.IsHASMode) return true;

        var senders = new Dictionary<byte, CustomRpcSender>();
        foreach (var pc in Main.AllPlayerControls)
        {
            var sender = CustomRpcSender.Create($"{pc.name}-SetRole", SendOption.None, false).StartMessage(pc.GetClientId());
            senders[pc.PlayerId] = sender;
        }

        RoleAssignManager.SelectAssignRoles();

        foreach (var player in Main.AllPlayerControls.Where(p => p != null && !p.Data.Disconnected))
        {
            var role = player.GetCustomRole();
            var info = role.GetRoleInfo();
            var killer = player.GetRoleClass() as IKiller;
            bool needsKill = killer?.CanUseKillButton() == true;
            bool isImpostorTeam = info?.CustomRoleType == CustomRoleTypes.Impostor;

            if (needsKill && !isImpostorTeam)
            {
                foreach (var seer in Main.AllPlayerControls.Where(s => s != null && !s.Data.Disconnected))
                {
                    var roleTypes = seer.PlayerId == player.PlayerId
                        ? RoleTypes.Impostor
                        : (seer.IsAlive() ? RoleTypes.Scientist : RoleTypes.CrewmateGhost);

                    senders[seer.PlayerId]
                        .StartRpc(player.NetId, (byte)RpcCalls.SetRole)
                        .Write((ushort)roleTypes)
                        .Write(true)
                        .EndRpc();
                }
            }
            else
            {
                foreach (var seer in Main.AllPlayerControls.Where(s => s != null && !s.Data.Disconnected))
                {
                    var roleTypes = role.GetRoleTypes();
                    senders[seer.PlayerId]
                        .StartRpc(player.NetId, (byte)RpcCalls.SetRole)
                        .Write((ushort)roleTypes)
                        .Write(true)
                        .EndRpc();
                }
            }
        }

        foreach (var sender in senders.Values)
        {
            sender.EndMessage().SendMessage();
        }

        foreach (var pair in PlayerState.AllPlayerStates)
        {
            ExtendedPlayerControl.RpcSetCustomRole(pair.Key, pair.Value.GetNowMainRole());
        }

        Utils.SyncAllSettings();
        return false;
    }
}