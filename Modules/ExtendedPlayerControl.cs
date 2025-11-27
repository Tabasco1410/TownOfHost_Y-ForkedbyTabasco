using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using InnerNet;
using UnityEngine;

using TownOfHostY.Modules;
using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;
using TownOfHostY.Roles.Impostor;
using TownOfHostY.Roles.Crewmate;
using TownOfHostY.Roles.Neutral;
using TownOfHostY.Roles.AddOns.Impostor;
using static TownOfHostY.Translator;
using AmongUs.Data;

namespace TownOfHostY;

static class ExtendedPlayerControl
{
    public static void RpcSetCustomRole(this PlayerControl player, CustomRoles role)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (player.GetCustomRole() == role) return;

        Main.ShowRoleInfoAtMeeting.Add(player.PlayerId);

        if (role < CustomRoles.StartAddon)
        {
            PlayerState.GetByPlayerId(player.PlayerId).SetMainRole(role);
            Main.ShowChangeMainRole.Add(player.PlayerId);

            var roleClass = player.GetRoleClass();
            if (roleClass != null)
            {
                roleClass.Dispose();
                CustomRoleManager.CreateInstance(role, player);
            }
        }
        else
        {
            PlayerState.GetByPlayerId(player.PlayerId).SetSubRole(role);
        }

        {
            var writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SetCustomRole, SendOption.Reliable, -1);
            writer.Write(player.PlayerId);
            writer.WritePacked((int)role);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }

        if (role < CustomRoles.StartAddon)
        {
            var rc = player.GetRoleClass();
            bool needsKillButton = (rc as IKiller)?.CanUseKillButton() == true;
            bool isImpostorTeam = role.GetRoleInfo()?.CustomRoleType == CustomRoleTypes.Impostor;

            if (needsKillButton && !isImpostorTeam)
            {
                int clientId = player.GetClientId();
                if (clientId == AmongUsClient.Instance.ClientId)
                {
                    player.StartCoroutine(player.CoSetRole(RoleTypes.Impostor, true));
                }
                else
                {
                    var w = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)RpcCalls.SetRole, SendOption.Reliable, clientId);
                    w.Write((ushort)RoleTypes.Impostor);
                    w.Write(true);
                    AmongUsClient.Instance.FinishRpcImmediately(w);
                }

                foreach (var pc in Main.AllPlayerControls)
                {
                    if (pc.PlayerId == player.PlayerId) continue;
                    int cid = pc.GetClientId();
                    if (cid < 0) continue;
                    var showRole = pc.IsAlive() ? RoleTypes.Scientist : RoleTypes.CrewmateGhost;
                    var w2 = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)RpcCalls.SetRole, SendOption.Reliable, cid);
                    w2.Write((ushort)showRole);
                    w2.Write(true);
                    AmongUsClient.Instance.FinishRpcImmediately(w2);
                }
            }
            else
            {
                var writer2 = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)RpcCalls.SetRole, SendOption.Reliable, -1);
                writer2.Write((ushort)role.GetRoleTypes());
                writer2.Write(true);
                AmongUsClient.Instance.FinishRpcImmediately(writer2);
            }

            player.ResetKillCooldown();
            player.RpcResetAbilityCooldown();
        }
    }

    public static void RpcSetCustomRole(byte PlayerId, CustomRoles role)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        var writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SetCustomRole, SendOption.Reliable, -1);
        writer.Write(PlayerId);
        writer.WritePacked((int)role);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }

    public static void RpcExile(this PlayerControl player)
    {
        RPC.ExileAsync(player);
    }

    public static ClientData GetClient(this PlayerControl player)
    {
        if (player == null) return null;
        return AmongUsClient.Instance.allClients.ToArray().FirstOrDefault(cd => cd != null && cd.Character != null && cd.Character.PlayerId == player.PlayerId);
    }

    public static int GetClientId(this PlayerControl player)
    {
        var client = player.GetClient();
        return client == null ? -1 : client.Id;
    }

    public static CustomRoles GetCustomRole(this NetworkedPlayerInfo player)
    {
        return player == null || player.Object == null ? CustomRoles.Crewmate : player.Object.GetCustomRole();
    }

    public static CustomRoles GetCustomRole(this PlayerControl player)
    {
        if (player == null)
        {
            var caller = new System.Diagnostics.StackFrame(1, false);
            var callerMethod = caller.GetMethod();
            string callerMethodName = callerMethod.Name;
            string callerClassName = callerMethod.DeclaringType.FullName;
            Logger.Warn(callerClassName + "." + callerMethodName + "がCustomRoleを取得しようとしましたが、対象がnullでした。", "GetCustomRole");
            return CustomRoles.Crewmate;
        }
        var state = PlayerState.GetByPlayerId(player.PlayerId);
        return state?.GetNowMainRole() ?? CustomRoles.Crewmate;
    }

    public static List<CustomRoles> GetCustomSubRoles(this PlayerControl player)
    {
        if (player == null)
        {
            Logger.Warn("CustomSubRoleを取得しようとしましたが、対象がnullでした。", "getCustomSubRole");
            return new() { CustomRoles.NotAssigned };
        }
        return PlayerState.GetByPlayerId(player.PlayerId).SubRoles;
    }

    public static CountTypes GetCountTypes(this PlayerControl player)
    {
        if (player == null)
        {
            var caller = new System.Diagnostics.StackFrame(1, false);
            var callerMethod = caller.GetMethod();
            string callerMethodName = callerMethod.Name;
            string callerClassName = callerMethod.DeclaringType.FullName;
            Logger.Warn(callerClassName + "." + callerMethodName + "がCountTypesを取得しようとしましたが、対象がnullでした。", "GetCountTypes");
            return CountTypes.None;
        }

        return PlayerState.GetByPlayerId(player.PlayerId)?.CountType ?? CountTypes.None;
    }

    public static void RpcSetNameEx(this PlayerControl player, string name)
    {
        foreach (var seer in Main.AllPlayerControls)
        {
            Main.LastNotifyNames[(player.PlayerId, seer.PlayerId)] = name;
        }
        HudManagerPatch.LastSetNameDesyncCount++;
        Logger.Info($"Set:{player?.Data?.PlayerName}:{name} for All", "RpcSetNameEx");
        player.RpcSetName(name);
    }

    public static void RpcSetNamePrivate(this PlayerControl player, string name, bool DontShowOnModdedClient = false, PlayerControl seer = null, bool force = false)
    {
        if (player == null || name == null || !AmongUsClient.Instance.AmHost) return;
        if (seer == null) seer = player;
        if (!force && Main.LastNotifyNames[(player.PlayerId, seer.PlayerId)] == name) return;

        Main.LastNotifyNames[(player.PlayerId, seer.PlayerId)] = name;
        HudManagerPatch.LastSetNameDesyncCount++;

        var clientId = seer.GetClientId();
        var writer = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)RpcCalls.SetName, SendOption.Reliable, clientId);
        writer.Write(player.Data.NetId);
        writer.Write(name);
        writer.Write(DontShowOnModdedClient);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }

    public static void RpcSetRoleDesync(this PlayerControl player, RoleTypes role, int clientId, bool canOverrideRole = false)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (player == null) return;

        if (AmongUsClient.Instance.ClientId == clientId)
        {
            player.StartCoroutine(player.CoSetRole(role, canOverrideRole));
            return;
        }

        var writer = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)RpcCalls.SetRole, SendOption.Reliable, clientId);
        writer.Write((ushort)role);
        writer.Write(true);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
        Logger.Info($"RpcSetRoleDesync toClientId:{clientId}) player:{player?.name}({role})", "RpcSetRole");
    }

    public static void RpcSetRoleNormal(this PlayerControl player, RoleTypes role, bool canOverrideRole = false)
    {
        if (player == null) return;
        if (AmongUsClient.Instance?.AmClient == true)
        {
            try
            {
                player.StartCoroutine(player.CoSetRole(role, canOverrideRole));
            }
            catch (Exception ex)
            {
                Logger.Warn($"CoSetRole invoke failed: {ex.Message}", "RpcSetRoleNormal");
            }
        }
        try
        {
            var writer = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)RpcCalls.SetRole, SendOption.Reliable, -1);
            writer.Write((ushort)role);
            writer.Write(canOverrideRole);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
            Logger.Info($"RpcSetRoleNormal toClientId:All(-1) player:{player?.name}({role}) override:{canOverrideRole}", "RpcSetRole");
        }
        catch (Exception ex)
        {
            Logger.Error($"RpcSetRoleNormal failed: {ex.Message}", "RpcSetRoleNormal");
        }
    }

    public static void SetKillCooldown(this PlayerControl player, float time = -1f, bool ForceProtect = false)
    {
        if (player == null) return;
        if (!ForceProtect && !player.CanUseKillButton()) return;
        if (time >= 0f)
        {
            Main.AllPlayerKillCooldown[player.PlayerId] = time * 2;
        }
        else
        {
            Main.AllPlayerKillCooldown[player.PlayerId] *= 2;
        }
        player.SyncSettings();
        player.RpcProtectedMurderPlayer();
        player.ResetKillCooldown();
    }

    public static void RpcSpecificMurderPlayer(this PlayerControl killer, PlayerControl target = null)
    {
        if (target == null) target = killer;
        if (killer.AmOwner)
        {
            killer.MurderPlayer(target);
        }
        else
        {
            var messageWriter = AmongUsClient.Instance.StartRpcImmediately(killer.NetId, (byte)RpcCalls.MurderPlayer, SendOption.Reliable, killer.GetClientId());
            messageWriter.WriteNetObject(target);
            messageWriter.Write((int)SucceededFlags);
            AmongUsClient.Instance.FinishRpcImmediately(messageWriter);
        }
    }

    [Obsolete]
    public static void RpcSpecificProtectPlayer(this PlayerControl killer, PlayerControl target = null, int colorId = 0)
    {
        if (AmongUsClient.Instance.AmClient)
        {
            killer.ProtectPlayer(target, colorId);
        }
        var messageWriter = AmongUsClient.Instance.StartRpcImmediately(killer.NetId, (byte)RpcCalls.ProtectPlayer, SendOption.Reliable, killer.GetClientId());
        messageWriter.WriteNetObject(target);
        messageWriter.Write(colorId);
        AmongUsClient.Instance.FinishRpcImmediately(messageWriter);
    }

    public static void RpcResetAbilityCooldown(this PlayerControl target)
    {
        if (target == null || !AmongUsClient.Instance.AmHost) return;
        Logger.Info($"アビリティクールダウンのリセット:{target.name}({target.PlayerId})", "RpcResetAbilityCooldown");
        if (PlayerControl.LocalPlayer == target)
        {
            PlayerControl.LocalPlayer.Data.Role.SetCooldown();
        }
        else
        {
            var writer = AmongUsClient.Instance.StartRpcImmediately(target.NetId, (byte)RpcCalls.ProtectPlayer, SendOption.None, target.GetClientId());
            writer.WriteNetObject(target);
            writer.Write(0);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
    }

    public static void RpcSpecificShapeshift(this PlayerControl player, PlayerControl target, bool shouldAnimate)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (player.PlayerId == 0)
        {
            player.Shapeshift(target, shouldAnimate);
            return;
        }
        var messageWriter = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)RpcCalls.Shapeshift, SendOption.Reliable, player.GetClientId());
        messageWriter.WriteNetObject(target);
        messageWriter.Write(shouldAnimate);
        AmongUsClient.Instance.FinishRpcImmediately(messageWriter);
    }

    public static void RpcSpecificRejectShapeshift(this PlayerControl player, PlayerControl target, bool shouldAnimate)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        foreach (var seer in Main.AllPlayerControls)
        {
            if (seer != player)
            {
                var msg = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)RpcCalls.RejectShapeshift, SendOption.Reliable, seer.GetClientId());
                AmongUsClient.Instance.FinishRpcImmediately(msg);
            }
            else
            {
                player.RpcSpecificShapeshift(target, shouldAnimate);
            }
        }
    }

    public static void RpcDesyncUpdateSystem(this PlayerControl target, SystemTypes systemType, int amount)
    {
        if (!AmongUsClient.Instance.AmHost || target == null) return;
        var messageWriter = AmongUsClient.Instance.StartRpcImmediately(ShipStatus.Instance.NetId, (byte)RpcCalls.UpdateSystem, SendOption.Reliable, target.GetClientId());
        messageWriter.Write((byte)systemType);
        messageWriter.WriteNetObject(target);
        messageWriter.Write((byte)amount);
        AmongUsClient.Instance.FinishRpcImmediately(messageWriter);
    }

    public static void MarkDirtySettings(this PlayerControl player)
    {
        PlayerGameOptionsSender.SetDirty(player.PlayerId);
    }

    public static void SyncSettings(this PlayerControl player)
    {
        PlayerGameOptionsSender.SetDirty(player.PlayerId);
        GameOptionsSender.SendAllGameOptions();
    }

    public static TaskState GetPlayerTaskState(this PlayerControl player)
    {
        return PlayerState.GetByPlayerId(player.PlayerId).GetTaskState();
    }

    public static string GetAllRoleName(this PlayerControl player)
    {
        if (!player) return null;
        var text = Utils.GetRoleName(player.GetCustomRole());
        text += RoleText.GetSubRoleMarks(player.GetCustomSubRoles());
        return text.RemoveHtmlTags();
    }

    public static string GetNameWithRole(this PlayerControl player)
    {
        return $"{player?.Data?.PlayerName}" + (GameStates.IsInGame ? $"({player?.GetAllRoleName()})" : "");
    }

    public static string GetRoleColorCode(this PlayerControl player, bool temporaryRole = false)
    {
        var role = player.GetCustomRole();
        if (temporaryRole)
        {
            if (player.Is(CustomRoles.ChainShifterAddon))
                return Utils.GetRoleColorCode(CustomRoles.ChainShifter);
        }

        (Color c, string t) = (Color.clear, "");
        player.GetRoleClass()?.OverrideShowMainRoleText(ref c, ref t);
        if (c != Color.clear) return ColorUtility.ToHtmlStringRGB(c);
        else return Utils.GetRoleColorCode(role);
    }

    public static Color GetRoleColor(this PlayerControl player, bool temporaryRole = false)
    {
        var role = player.GetCustomRole();
        if (temporaryRole)
        {
            if (player.Is(CustomRoles.ChainShifterAddon))
                return Utils.GetRoleColor(CustomRoles.ChainShifter);
        }

        (Color c, string t) = (Color.clear, "");
        player.GetRoleClass()?.OverrideShowMainRoleText(ref c, ref t);
        if (c != Color.clear) return c;
        else return Utils.GetRoleColor(role);
    }

    public static void ResetPlayerCam(this PlayerControl pc, float delay = 0f)
    {
        if (pc == null || !AmongUsClient.Instance.AmHost || pc.AmOwner) return;

        var systemtypes = (MapNames)Main.NormalOptions.MapId switch
        {
            MapNames.Polus => SystemTypes.Laboratory,
            MapNames.Airship => SystemTypes.HeliSabotage,
            _ => SystemTypes.Reactor,
        };

        _ = new LateTask(() =>
        {
            pc.RpcDesyncUpdateSystem(systemtypes, 128);
        }, 0f + delay, "Reactor Desync");

        _ = new LateTask(() =>
        {
            pc.RpcSpecificMurderPlayer();
        }, 0.2f + delay, "Murder To Reset Cam");

        _ = new LateTask(() =>
        {
            pc.RpcDesyncUpdateSystem(systemtypes, 16);
            if (Main.NormalOptions.MapId == 4)
                pc.RpcDesyncUpdateSystem(systemtypes, 17);
        }, 0.4f + delay, "Fix Desync Reactor");
    }

    public static void ReactorFlash(this PlayerControl pc, float delay = 0f)
    {
        if (pc == null) return;
        var systemtypes = Utils.GetCriticalSabotageSystemType();
        float FlashDuration = Options.KillFlashDuration.GetFloat();

        pc.RpcDesyncUpdateSystem(systemtypes, 128);

        _ = new LateTask(() =>
        {
            pc.RpcDesyncUpdateSystem(systemtypes, 16);

            if ((MapNames)Main.NormalOptions.MapId == MapNames.Airship)
                pc.RpcDesyncUpdateSystem(systemtypes, 17);
        }, FlashDuration + delay, "Fix Desync Reactor");
    }

    public static string GetRealName(this PlayerControl player, bool isMeeting = false)
    {
        if (Options.GetNameChangeModes() == NameChange.Crew)
            return isMeeting ? Main.AllPlayerNames[player.PlayerId] : GetString("CustomRoleTypes.Crewmate");

        return isMeeting ? player?.Data?.PlayerName : player?.name;
    }

    public static bool CanUseKillButton(this PlayerControl pc)
    {
        if (!pc.IsAlive() || pc.Data.Role.Role == RoleTypes.GuardianAngel) return false;
        if (pc.GetCustomRole().IsCCLeaderRoles()) return true;

        var roleCanUse = (pc.GetRoleClass() as IKiller)?.CanUseKillButton();

        return roleCanUse ?? pc.Is(CustomRoleTypes.Impostor);
    }

    public static bool CanUseImpostorVentButton(this PlayerControl pc)
    {
        if (!pc.IsAlive()) return false;
        if (pc.GetCustomRole().IsCCLeaderRoles()) return !CatchCat.Option.LeaderIgnoreVent.GetBool();

        var roleCanUse = (pc.GetRoleClass() as IKiller)?.CanUseImpostorVentButton();
        return roleCanUse ?? false;
    }

    public static bool CanUseSabotageButton(this PlayerControl pc)
    {
        var roleCanUse = (pc.GetRoleClass() as IKiller)?.CanUseSabotageButton();

        return roleCanUse ?? false;
    }

    public static void ResetKillCooldown(this PlayerControl player)
    {
        Main.AllPlayerKillCooldown[player.PlayerId] = (player.GetRoleClass() as IKiller)?.CalculateKillCooldown() ?? Options.DefaultKillCooldown;
        if (Options.IsCCMode && player.GetCustomRole().IsCCLeaderRoles())
            Main.AllPlayerKillCooldown[player.PlayerId] = CatchCat.LeaderPlayer.CalculateKillCooldown(player);
        if (player.PlayerId == LastImpostor.currentId)
            LastImpostor.SetKillCooldown(player);
    }

    public static bool CanMakeMadmate(this PlayerControl player)
    {
        if (
            Options.CanMakeMadmateCount.GetInt() <= Main.SKMadmateNowCount ||
            player == null ||
            player.Data.Role.Role != RoleTypes.Shapeshifter)
        {
            return false;
        }

        var isSidekickableCustomRole = player.GetRoleClass() is ISidekickable sidekickable && sidekickable.CanMakeSidekick();

        return isSidekickableCustomRole ||
            player.GetCustomRole().CanMakeMadmate();
    }

    public static void RpcExileV2(this PlayerControl player)
    {
        player.Exiled();
        var writer = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)RpcCalls.Exiled, SendOption.None, -1);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }

    public static void MurderPlayer(this PlayerControl killer, PlayerControl target)
    {
        killer.MurderPlayer(target, SucceededFlags);
    }

    public const MurderResultFlags SucceededFlags = MurderResultFlags.Succeeded | MurderResultFlags.DecisionByHost;

    public static void RpcMurderPlayer(this PlayerControl killer, PlayerControl target)
    {
        killer.RpcMurderPlayer(target, true);
    }

    public static void RpcMurderPlayerV2(this PlayerControl killer, PlayerControl target)
    {
        if (target == null) target = killer;
        if (AmongUsClient.Instance.AmClient)
        {
            killer.MurderPlayer(target);
        }
        var messageWriter = AmongUsClient.Instance.StartRpcImmediately(killer.NetId, (byte)RpcCalls.MurderPlayer, SendOption.None, -1);
        messageWriter.WriteNetObject(target);
        messageWriter.Write((int)SucceededFlags);
        AmongUsClient.Instance.FinishRpcImmediately(messageWriter);
        Utils.NotifyRoles();
    }

    public static void RpcProtectedMurderPlayer(this PlayerControl killer, PlayerControl target = null)
    {
        if (!killer.IsAlive()) return;

        if (target == null) target = killer;
        if (killer.AmOwner)
        {
            killer.MurderPlayer(target, MurderResultFlags.FailedProtected);
        }
        if (killer.PlayerId != 0)
        {
            var writer = AmongUsClient.Instance.StartRpcImmediately(killer.NetId, (byte)RpcCalls.MurderPlayer, SendOption.Reliable, killer.GetClientId());
            writer.WriteNetObject(target);
            writer.Write((int)MurderResultFlags.FailedProtected);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
    }

    public static void NoCheckStartMeeting(this PlayerControl reporter, NetworkedPlayerInfo target)
    {
        MeetingRoomManager.Instance.AssignSelf(reporter, target);
        DestroyableSingleton<HudManager>.Instance.OpenMeetingRoom(reporter);
        reporter.RpcStartMeeting(target);
    }

    public static bool IsModClient(this PlayerControl player) => Main.playerVersion.ContainsKey(player.PlayerId);

    public static List<PlayerControl> GetPlayersInAbilityRangeSorted(this PlayerControl player, bool ignoreColliders = false) => GetPlayersInAbilityRangeSorted(player, pc => true, ignoreColliders);

    public static List<PlayerControl> GetPlayersInAbilityRangeSorted(this PlayerControl player, Predicate<PlayerControl> predicate, bool ignoreColliders = false)
    {
        var rangePlayersIL = RoleBehaviour.GetTempPlayerList();
        List<PlayerControl> rangePlayers = new();

        if (player?.Data?.Role == null)
            return rangePlayers;

        player.Data.Role.GetPlayersInAbilityRangeSorted(rangePlayersIL, ignoreColliders);
        foreach (var pc in rangePlayersIL)
        {
            if (predicate(pc)) rangePlayers.Add(pc);
        }
        return rangePlayers;
    }

    public static bool IsNeutralKiller(this PlayerControl player)
    {
        if (player.Is(CustomRoles.Opportunist) && Opportunist.CanKill) return true;
        return
            player.GetCustomRole() is
            CustomRoles.Egoist or
            CustomRoles.Jackal or
            CustomRoles.DarkHide;
    }

    public static bool IsCrewKiller(this PlayerControl player)
    {
        return
            player.GetCustomRole() is
            CustomRoles.Sheriff or
            CustomRoles.SillySheriff or
            CustomRoles.Hunter;
    }

    public static bool KnowDeathReason(this PlayerControl seer, PlayerControl seen)
    {
        if (seen.IsAlive())
        {
            return false;
        }
        if (EvilIgnition.CanBombTarget() &&
            PlayerState.GetByPlayerId(seen.PlayerId).DeathReason == CustomDeathReason.IgnitionBombed)
        {
            return true;
        }
        if (!seer.IsAlive() && Options.GhostCanSeeDeathReason.GetBool())
        {
            return true;
        }

        if (seer.GetRoleClass() is IDeathReasonSeeable deathReasonSeeable)
        {
            return deathReasonSeeable.CheckSeeDeathReason(seen);
        }
        return (seer.Is(CustomRoles.SKMadmate) && Options.MadmateCanSeeDeathReason.GetBool())
            || seer.Is(CustomRoles.Autopsy);
    }

    public static string GetRoleInfo(this PlayerControl player, bool InfoLong = false)
    {
        var roleClass = player.GetRoleClass();
        var role = player.GetCustomRole();

        role = role.VanillaRoleConversion();

        var Prefix = "";
        var text = role.ToString();
        var Info = "";
        switch (role)
        {
            case CustomRoles.Crewmate:
            case CustomRoles.Impostor:
                if (InfoLong)
                {
                    return "\n" + GetString($"{text}Blurb");
                }
                break;
            case CustomRoles.Mafia:
                if (InfoLong) break;
                if (roleClass is not Mafia mafia) break;
                Prefix = mafia.CanUseKillButton() ? "After" : "Before";
                break;
            case CustomRoles.MadSnitch:
            case CustomRoles.MadGuesser:
            case CustomRoles.MadGuardian:
                if (InfoLong) break;
                text = CustomRoles.Madmate.ToString();
                Prefix = player.GetPlayerTaskState().IsTaskFinished ? "" : "Before";
                break;
            case CustomRoles.Bakery:
                if (Bakery.IsNeutral(player))
                    text = "NBakery";
                break;
            case CustomRoles.BestieWolf:
                if (InfoLong) break;
                Prefix = Main.AliveImpostorCount >= 2 ? "" : "After";
                break;
        }

        if (role.IsVanilla())
        {
            if (InfoLong)
            {
                Info = "BlurbLong";
                return "\n" + GetString($"{Prefix}{text}{Info}");
            }

            Info = "Blurb";
        }
        else
        {
            Info = InfoLong ? "InfoLong" : "Info";
        }

        return GetString($"{Prefix}{text}{Info}");
    }

    public static void SetRoleEx(this PlayerControl player, RoleTypes role)
    {
        bool ghostRole = RoleManager.IsGhostRole(role);
        if (!player.Data || GameManager.Instance == null || !GameManager.Instance) return;

        DestroyableSingleton<RoleManager>.Instance.SetRole(player, role);
        player.Data.Role.SpawnTaskHeader(player);
        if (!ghostRole) player.MyPhysics.SetBodyType(player.BodyType);

        if (!player.AmOwner) return;

        if (ghostRole)
        {
            DestroyableSingleton<HudManager>.Instance.ReportButton.gameObject.SetActive(false);
        }
        else
        {
            if (player.Data.Role.IsImpostor)
            {
                DataManager.Player.Stats.IncrementStat(StatID.GamesAsImpostor);
                DataManager.Player.Stats.ResetStat(StatID.CrewmateStreak);
            }
            else
            {
                DataManager.Player.Stats.IncrementStat(StatID.GamesAsCrewmate);
                DataManager.Player.Stats.IncrementStat(StatID.CrewmateStreak);
            }
            DestroyableSingleton<HudManager>.Instance.MapButton.gameObject.SetActive(true);
            DestroyableSingleton<HudManager>.Instance.ReportButton.gameObject.SetActive(true);
            DestroyableSingleton<HudManager>.Instance.UseButton.gameObject.SetActive(true);
        }
    }

    public static void SetRealKiller(this PlayerControl target, PlayerControl killer, bool NotOverRide = false)
    {
        if (target == null)
        {
            Logger.Info("target=null", "SetRealKiller");
            return;
        }
        var State = PlayerState.GetByPlayerId(target.PlayerId);
        if (State.RealKiller.Item1 != DateTime.MinValue && NotOverRide) return;
        byte killerId = killer == null ? byte.MaxValue : killer.PlayerId;
        RPC.SetRealKiller(target.PlayerId, killerId);
    }

    public static PlayerControl GetRealKiller(this PlayerControl target)
    {
        var killerId = PlayerState.GetByPlayerId(target.PlayerId).GetRealKiller();
        return killerId == byte.MaxValue ? null : Utils.GetPlayerById(killerId);
    }

    public static PlainShipRoom GetPlainShipRoom(this PlayerControl pc)
    {
        if (!pc.IsAlive()) return null;
        var Rooms = ShipStatus.Instance.AllRooms;
        if (Rooms == null) return null;
        foreach (var room in Rooms)
        {
            if (!room.roomArea) continue;
            if (pc.Collider.IsTouching(room.roomArea))
                return room;
        }
        return null;
    }

    public static void RpcSnapTo(this PlayerControl pc, Vector2 position)
    {
        pc.NetTransform.RpcSnapTo(position);
    }

    public static void SnapToTeleport(this PlayerControl pc, Vector2 position)
    {
        var netTransform = pc.NetTransform;
        if (AmongUsClient.Instance.AmClient)
        {
            netTransform.SnapTo(position, (ushort)(netTransform.lastSequenceId + 128));
        }
        ushort newSid = (ushort)(netTransform.lastSequenceId + 2);
        var messageWriter = AmongUsClient.Instance.StartRpcImmediately(netTransform.NetId, (byte)RpcCalls.SnapTo, SendOption.Reliable);
        NetHelpers.WriteVector2(position, messageWriter);
        messageWriter.Write(newSid);
        AmongUsClient.Instance.FinishRpcImmediately(messageWriter);
    }

    public static void RpcSnapToDesync(this PlayerControl pc, PlayerControl target, Vector2 position)
    {
        var net = pc.NetTransform;
        var num = (ushort)(net.lastSequenceId + 2);
        var messageWriter = AmongUsClient.Instance.StartRpcImmediately(net.NetId, (byte)RpcCalls.SnapTo, SendOption.None, target.GetClientId());
        NetHelpers.WriteVector2(position, messageWriter);
        messageWriter.Write(num);
        AmongUsClient.Instance.FinishRpcImmediately(messageWriter);
    }

    public static bool IsProtected(this PlayerControl self) => self.protectedByGuardianId > -1;

    public static bool Is(this PlayerControl target, CustomRoles role) =>
        role > CustomRoles.StartAddon ? target.GetCustomSubRoles().Contains(role) : target.GetCustomRole() == role;
    public static bool Is(this PlayerControl target, CustomRoleTypes type) { return target.GetCustomRole().GetCustomRoleTypes() == type; }
    public static bool Is(this PlayerControl target, RoleTypes type) { return target.GetCustomRole().GetRoleTypes() == type; }
    public static bool Is(this PlayerControl target, CountTypes type) { return target.GetCountTypes() == type; }
    public static bool IsAlive(this PlayerControl target)
    {
        if (GameStates.IsLobby)
        {
            return true;
        }
        if (target == null)
        {
            return false;
        }
        if (PlayerState.GetByPlayerId(target.PlayerId) is not PlayerState state)
        {
            return true;
        }
        return !state.IsDead;
    }
}