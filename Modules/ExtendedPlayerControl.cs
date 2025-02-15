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

namespace TownOfHostY;

static class ExtendedPlayerControl
{
    public static void RpcSetCustomRole(this PlayerControl player, CustomRoles role)
    {
        if (player.GetCustomRole() == role) return;

        // 役職の変更・属性の追加タイミングの次回会議に説明が表示される
        Main.ShowRoleInfoAtMeeting.Add(player.PlayerId);

        // MainRole/役職
        if (role < CustomRoles.StartAddon)
        {
            PlayerState.GetByPlayerId(player.PlayerId).SetMainRole(role);
            Main.ShowChangeMainRole.Add(player.PlayerId);
        }
        // SubRole/属性
        else if (role > CustomRoles.StartAddon)
        {
            PlayerState.GetByPlayerId(player.PlayerId).SetSubRole(role);
            return;
        }
        if (AmongUsClient.Instance.AmHost)
        {
            if (role < CustomRoles.StartAddon)
            {
                var roleClass = player.GetRoleClass();
                if (roleClass != null)
                {
                    roleClass.Dispose();
                    CustomRoleManager.CreateInstance(role, player);
                }
            }

            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SetCustomRole, Hazel.SendOption.Reliable, -1);
            writer.Write(player.PlayerId);
            writer.WritePacked((int)role);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
    }
    public static void RpcSetCustomRole(byte PlayerId, CustomRoles role)
    {
        if (AmongUsClient.Instance.AmHost)
        {
            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SetCustomRole, Hazel.SendOption.Reliable, -1);
            writer.Write(PlayerId);
            writer.WritePacked((int)role);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
    }

    public static void RpcExile(this PlayerControl player)
    {
        RPC.ExileAsync(player);
    }
    public static InnerNet.ClientData GetClient(this PlayerControl player)
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
    /// <summary>
    /// ※サブロールは取得できません。
    /// </summary>
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
        //player: 名前の変更対象
        //seer: 上の変更を確認することができるプレイヤー
        if (player == null || name == null || !AmongUsClient.Instance.AmHost) return;
        if (seer == null) seer = player;
        if (!force && Main.LastNotifyNames[(player.PlayerId, seer.PlayerId)] == name)
        {
            //Logger.info($"Cancel:{player.name}:{name} for {seer.name}", "RpcSetNamePrivate");
            return;
        }
        Main.LastNotifyNames[(player.PlayerId, seer.PlayerId)] = name;
        HudManagerPatch.LastSetNameDesyncCount++;
        //Logger.Info($"Set:{player?.Data?.PlayerName}:{name.RemoveHtmlTags()} for {seer.GetNameWithRole()}", "RpcSetNamePrivate");

        var clientId = seer.GetClientId();
        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)RpcCalls.SetName, Hazel.SendOption.Reliable, clientId);
        writer.Write(player.Data.NetId);
        writer.Write(name);
        writer.Write(DontShowOnModdedClient);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }
    public static void RpcSetRoleDesync(this PlayerControl player, RoleTypes role, int clientId, bool canOverrideRole = false)
    {
        //player: 名前の変更対象

        if (player == null) return;
        if (AmongUsClient.Instance.ClientId == clientId)
        {
            player.StartCoroutine(player.CoSetRole(role, false));
            return;
        }
        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)RpcCalls.SetRole, Hazel.SendOption.Reliable, clientId);
        writer.Write((ushort)role);
        writer.Write(true); //canOverrideRole
        AmongUsClient.Instance.FinishRpcImmediately(writer);

        Logger.Info($"RpcSetRoleDesync toClientId:{clientId}) player:{player?.name}({role})", "RpcSetRole");
    }
    public static void RpcSetRoleNormal(this PlayerControl player, RoleTypes role, bool canOverrideRole = false)
    {
        if (player == null) return;
        if (AmongUsClient.Instance.AmClient)
        {
            player.StartCoroutine(player.CoSetRole(role, canOverrideRole));
        }
        MessageWriter messageWriter = AmongUsClient.Instance.StartRpc(player.NetId, (byte)RpcCalls.SetRole, Hazel.SendOption.Reliable);
        messageWriter.Write((ushort)role);
        messageWriter.Write(true); //canOverrideRole
        messageWriter.EndMessage();

        Logger.Info($"RpcSetRoleNormal toClientId:All{-1}) player:{player?.name}({role})", "RpcSetRole");
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
            MessageWriter messageWriter = AmongUsClient.Instance.StartRpcImmediately(killer.NetId, (byte)RpcCalls.MurderPlayer, SendOption.Reliable, killer.GetClientId());
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
        MessageWriter messageWriter = AmongUsClient.Instance.StartRpcImmediately(killer.NetId, (byte)RpcCalls.ProtectPlayer, SendOption.Reliable, killer.GetClientId());
        messageWriter.WriteNetObject(target);
        messageWriter.Write(colorId);
        AmongUsClient.Instance.FinishRpcImmediately(messageWriter);
    }
    public static void RpcResetAbilityCooldown(this PlayerControl target)
    {
        if (target == null || !AmongUsClient.Instance.AmHost)
        {
            return;
        }
        Logger.Info($"アビリティクールダウンのリセット:{target.name}({target.PlayerId})", "RpcResetAbilityCooldown");
        if (PlayerControl.LocalPlayer == target)
        {
            //targetがホストだった場合
            PlayerControl.LocalPlayer.Data.Role.SetCooldown();
        }
        else
        {
            //targetがホスト以外だった場合
            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(target.NetId, (byte)RpcCalls.ProtectPlayer, SendOption.None, target.GetClientId());
            writer.WriteNetObject(target);
            writer.Write(0);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
        /*
            プレイヤーがバリアを張ったとき、そのプレイヤーの役職に関わらずアビリティーのクールダウンがリセットされます。
            ログの追加により無にバリアを張ることができなくなったため、代わりに自身に0秒バリアを張るように変更しました。
            この変更により、役職としての守護天使が無効化されます。
            ホストのクールダウンは直接リセットします。
        */
    }
    public static void RpcSpecificShapeshift(this PlayerControl player, PlayerControl target, bool shouldAnimate)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (player.PlayerId == 0)
        {
            player.Shapeshift(target, shouldAnimate);
            return;
        }
        MessageWriter messageWriter = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)RpcCalls.Shapeshift, SendOption.Reliable, player.GetClientId());
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
                MessageWriter msg = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)RpcCalls.RejectShapeshift, SendOption.Reliable, seer.GetClientId());
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
        MessageWriter messageWriter = AmongUsClient.Instance.StartRpcImmediately(ShipStatus.Instance.NetId, (byte)RpcCalls.UpdateSystem, SendOption.Reliable, target.GetClientId());
        messageWriter.Write((byte)systemType);
        messageWriter.WriteNetObject(target);
        messageWriter.Write((byte)amount);
        AmongUsClient.Instance.FinishRpcImmediately(messageWriter);
    }

    /*public static void RpcBeKilled(this PlayerControl player, PlayerControl KilledBy = null) {
        if(!AmongUsClient.Instance.AmHost) return;
        byte KilledById;
        if(KilledBy == null)
            KilledById = byte.MaxValue;
        else
            KilledById = KilledBy.PlayerId;

        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)CustomRPC.BeKilled, Hazel.SendOption.Reliable, -1);
        writer.Write(player.PlayerId);
        writer.Write(KilledById);
        AmongUsClient.Instance.FinishRpcImmediately(writer);

        RPC.BeKilled(player.PlayerId, KilledById);
    }*/
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

    /*public static GameOptionsData DeepCopy(this GameOptionsData opt)
    {
        var optByte = opt.ToBytes(5);
        return GameOptionsData.FromBytes(optByte);
    }*/

    public static string GetAllRoleName(this PlayerControl player)
    {
        if (!player) return null;
        var text = Utils.GetRoleName(player.GetCustomRole());
        //text += player.GetSubRoleName();
        text += RoleText.GetSubRoleMarks(player.GetCustomSubRoles());  //Markに変更
        // Logでしか使用していないのでここにRemoveTag
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
        //trueRoleNameでColor上書きあればそれになる
        player.GetRoleClass()?.OverrideShowMainRoleText(ref c, ref t);//colorのみ
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
        //trueRoleNameでColor上書きあればそれになる
        player.GetRoleClass()?.OverrideShowMainRoleText(ref c, ref t);//colorのみ
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
            if (Main.NormalOptions.MapId == 4) //Airship用
                pc.RpcDesyncUpdateSystem(systemtypes, 17);
        }, 0.4f + delay, "Fix Desync Reactor");
    }
    public static void ReactorFlash(this PlayerControl pc, float delay = 0f)
    {
        if (pc == null) return;
        // Logger.Info($"{pc}", "ReactorFlash");
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
        // CC
        if (pc.GetCustomRole().IsCCLeaderRoles()) return true;

        var roleCanUse = (pc.GetRoleClass() as IKiller)?.CanUseKillButton();

        return roleCanUse ?? pc.Is(CustomRoleTypes.Impostor);
    }
    public static bool CanUseImpostorVentButton(this PlayerControl pc)
    {
        if (!pc.IsAlive()) return false;
        // CC
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
        Main.AllPlayerKillCooldown[player.PlayerId] = (player.GetRoleClass() as IKiller)?.CalculateKillCooldown() ?? Options.DefaultKillCooldown; //キルクールをデフォルトキルクールに変更
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
            player.GetCustomRole().CanMakeMadmate(); // ISideKickable対応前の役職はこちら
    }
    public static void RpcExileV2(this PlayerControl player)
    {
        player.Exiled();
        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)RpcCalls.Exiled, SendOption.None, -1);
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
        MessageWriter messageWriter = AmongUsClient.Instance.StartRpcImmediately(killer.NetId, (byte)RpcCalls.MurderPlayer, SendOption.None, -1);
        messageWriter.WriteNetObject(target);
        messageWriter.Write((int)SucceededFlags);
        AmongUsClient.Instance.FinishRpcImmediately(messageWriter);
        Utils.NotifyRoles();
    }
    public static void RpcProtectedMurderPlayer(this PlayerControl killer, PlayerControl target = null)
    {
        //killerが死んでいる場合は実行しない
        if (!killer.IsAlive()) return;

        if (target == null) target = killer;
        // Host
        if (killer.AmOwner)
        {
            killer.MurderPlayer(target, MurderResultFlags.FailedProtected);
        }
        // Other Clients
        if (killer.PlayerId != 0)
        {
            var writer = AmongUsClient.Instance.StartRpcImmediately(killer.NetId, (byte)RpcCalls.MurderPlayer, SendOption.Reliable, killer.GetClientId());
            writer.WriteNetObject(target);
            writer.Write((int)MurderResultFlags.FailedProtected);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
    }
    public static void NoCheckStartMeeting(this PlayerControl reporter, NetworkedPlayerInfo target)
    { /*サボタージュ中でも関係なしに会議を起こせるメソッド
        targetがnullの場合はボタンとなる*/
        MeetingRoomManager.Instance.AssignSelf(reporter, target);
        DestroyableSingleton<HudManager>.Instance.OpenMeetingRoom(reporter);
        reporter.RpcStartMeeting(target);
    }
    public static bool IsModClient(this PlayerControl player) => Main.playerVersion.ContainsKey(player.PlayerId);
    ///<summary>
    ///プレイヤーのRoleBehaviourのGetPlayersInAbilityRangeSortedを実行し、戻り値を返します。
    ///</summary>
    ///<param name="ignoreColliders">trueにすると、壁の向こう側のプレイヤーが含まれるようになります。守護天使用</param>
    ///<returns>GetPlayersInAbilityRangeSortedの戻り値</returns>
    public static List<PlayerControl> GetPlayersInAbilityRangeSorted(this PlayerControl player, bool ignoreColliders = false) => GetPlayersInAbilityRangeSorted(player, pc => true, ignoreColliders);
    ///<summary>
    ///プレイヤーのRoleBehaviourのGetPlayersInAbilityRangeSortedを実行し、predicateの条件に合わないものを除外して返します。
    ///</summary>
    ///<param name="predicate">リストに入れるプレイヤーの条件 このpredicateに入れてfalseを返すプレイヤーは除外されます。</param>
    ///<param name="ignoreColliders">trueにすると、壁の向こう側のプレイヤーが含まれるようになります。守護天使用</param>
    ///<returns>GetPlayersInAbilityRangeSortedの戻り値から条件に合わないプレイヤーを除外したもの。</returns>
    public static List<PlayerControl> GetPlayersInAbilityRangeSorted(this PlayerControl player, Predicate<PlayerControl> predicate, bool ignoreColliders = false)
    {
        var rangePlayersIL = RoleBehaviour.GetTempPlayerList();
        List<PlayerControl> rangePlayers = new();
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
        // targetが生きてたらfalse
        if (seen.IsAlive())
        {
            return false;
        }
        if (EvilIgnition.CanBombTarget() &&
            PlayerState.GetByPlayerId(seen.PlayerId).DeathReason == CustomDeathReason.IgnitionBombed)
        {
            return true;
        }
        // seerが死亡済で，霊界から死因が見える設定がON
        if (!seer.IsAlive() && Options.GhostCanSeeDeathReason.GetBool())
        {
            return true;
        }

        // 役職による仕分け
        if (seer.GetRoleClass() is IDeathReasonSeeable deathReasonSeeable)
        {
            return deathReasonSeeable.CheckSeeDeathReason(seen);
        }
        // IDeathReasonSeeable未対応役職はこちら
        return (seer.Is(CustomRoles.SKMadmate) && Options.MadmateCanSeeDeathReason.GetBool())
            || seer.Is(CustomRoles.Autopsy);
    }
    public static string GetRoleInfo(this PlayerControl player, bool InfoLong = false)
    {
        var roleClass = player.GetRoleClass();
        var role = player.GetCustomRole();

        role = role.VanillaRoleConversion();//変換

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
                StatsManager.Instance.IncrementStat(StringNames.StatsGamesImpostor);
                StatsManager.Instance.ResetStat(StringNames.StatsCrewmateStreak);
            }
            else
            {
                StatsManager.Instance.IncrementStat(StringNames.StatsGamesCrewmate);
                StatsManager.Instance.IncrementStat(StringNames.StatsCrewmateStreak);
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
        if (State.RealKiller.Item1 != DateTime.MinValue && NotOverRide) return; //既に値がある場合上書きしない
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
        MessageWriter messageWriter = AmongUsClient.Instance.StartRpcImmediately(netTransform.NetId, (byte)RpcCalls.SnapTo, SendOption.Reliable);
        NetHelpers.WriteVector2(position, messageWriter);
        messageWriter.Write(newSid);
        AmongUsClient.Instance.FinishRpcImmediately(messageWriter);
    }
    public static void RpcSnapToDesync(this PlayerControl pc, PlayerControl target, Vector2 position)
    {
        var net = pc.NetTransform;
        var num = (ushort)(net.lastSequenceId + 2);
        MessageWriter messageWriter = AmongUsClient.Instance.StartRpcImmediately(net.NetId, (byte)RpcCalls.SnapTo, SendOption.None, target.GetClientId());
        NetHelpers.WriteVector2(position, messageWriter);
        messageWriter.Write(num);
        AmongUsClient.Instance.FinishRpcImmediately(messageWriter);
    }
    /// <summary>現在守護されているかどうか</summary>
    public static bool IsProtected(this PlayerControl self) => self.protectedByGuardianId > -1;

    //汎用
    public static bool Is(this PlayerControl target, CustomRoles role) =>
        role > CustomRoles.StartAddon ? target.GetCustomSubRoles().Contains(role) : target.GetCustomRole() == role;
    public static bool Is(this PlayerControl target, CustomRoleTypes type) { return target.GetCustomRole().GetCustomRoleTypes() == type; }
    public static bool Is(this PlayerControl target, RoleTypes type) { return target.GetCustomRole().GetRoleTypes() == type; }
    public static bool Is(this PlayerControl target, CountTypes type) { return target.GetCountTypes() == type; }
    public static bool IsAlive(this PlayerControl target)
    {
        //ロビーなら生きている
        if (GameStates.IsLobby)
        {
            return true;
        }
        //targetがnullならば切断者なので生きていない
        if (target == null)
        {
            return false;
        }
        //targetがnullでなく取得できない場合は登録前なので生きているとする
        if (PlayerState.GetByPlayerId(target.PlayerId) is not PlayerState state)
        {
            return true;
        }
        return !state.IsDead;
    }
}