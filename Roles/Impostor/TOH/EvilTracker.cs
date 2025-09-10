using System.Collections.Generic;
using System.Text;
using Hazel;
using UnityEngine;
using AmongUs.GameOptions;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;
using static TownOfHostY.Translator;

namespace TownOfHostY.Roles.Impostor;

public sealed class EvilTracker : RoleBase, IImpostor, IKillFlashSeeable, ISidekickable
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(EvilTracker),
            player => new EvilTracker(player),
            CustomRoles.EvilTracker,
            () => (TargetMode)OptionTargetMode.GetValue() == TargetMode.Never ? RoleTypes.Impostor : RoleTypes.Shapeshifter,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpTOH + 1300,
            SetupOptionItem,
            "イビルトラッカー",
            canMakeMadmate: () => OptionCanCreateMadmate.Bool
        );

    public EvilTracker(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        CanSeeKillFlash = OptionCanSeeKillFlash.Bool;
        CurrentTargetMode = (TargetMode)OptionTargetMode.GetValue();
        CanSeeLastRoomInMeeting = OptionCanSeeLastRoomInMeeting.Bool;
        CanCreateMadmate = OptionCanCreateMadmate.Bool && CurrentTargetMode != TargetMode.Never;

        TargetId = byte.MaxValue;
        CanSetTarget = CurrentTargetMode != TargetMode.Never;
        //ImpostorsIdはEvilTracker内で共有
        ImpostorsId.Clear();
        var playerId = player.PlayerId;
        foreach (var target in Main.AllAlivePlayerControls)
        {
            var targetId = target.PlayerId;
            if (targetId != playerId && target.Is(CustomRoleTypes.Impostor))
            {
                ImpostorsId.Add(targetId);
                TargetArrow.Add(playerId, targetId);
            }
        }
    }

    private static BooleanOptionItem OptionCanSeeKillFlash;
    private static StringOptionItem OptionTargetMode;
    private static BooleanOptionItem OptionCanSeeLastRoomInMeeting;
    private static BooleanOptionItem OptionCanCreateMadmate;

    enum OptionName
    {
        EvilTrackerCanSeeKillFlash,
        EvilTrackerTargetMode,
        EvilTrackerCanSeeLastRoomInMeeting,
    }
    public static bool CanSeeKillFlash;
    private static TargetMode CurrentTargetMode;
    public static bool CanSeeLastRoomInMeeting;
    private static bool CanCreateMadmate;

    public byte TargetId;
    public bool CanSetTarget;
    private HashSet<byte> ImpostorsId = new(3);

    private enum TargetMode
    {
        Never,
        OnceInGame,
        EveryMeeting,
        Always,
    };
    private static readonly string[] TargetModeText =
    {
        "EvilTrackerTargetMode.Never",
        "EvilTrackerTargetMode.OnceInGame",
        "EvilTrackerTargetMode.EveryMeeting",
        "EvilTrackerTargetMode.Always",
    };
    private enum TargetOperation : byte
    {
        /// <summary>
        /// ターゲット再設定可能にする
        /// </summary>
        ReEnableTargeting,
        /// <summary>
        /// ターゲットを削除する
        /// </summary>
        RemoveTarget,
        /// <summary>
        /// ターゲットを設定する
        /// </summary>
        SetTarget,
    }

    private static void SetupOptionItem()
    {
        OptionCanSeeKillFlash = BooleanOptionItem.Create(RoleInfo, 10, OptionName.EvilTrackerCanSeeKillFlash, true, false);
        OptionTargetMode = StringOptionItem.Create(RoleInfo, 11, OptionName.EvilTrackerTargetMode, TargetModeText, 2, false);
        OptionCanCreateMadmate = BooleanOptionItem.Create(RoleInfo, 12, GeneralOption.CanCreateMadmate, false, false);
        OptionCanCreateMadmate.SetParent(OptionTargetMode);
        OptionCanSeeLastRoomInMeeting = BooleanOptionItem.Create(RoleInfo, 13, OptionName.EvilTrackerCanSeeLastRoomInMeeting, false, false);
    }
    public bool CheckKillFlash(MurderInfo info) // IKillFlashSeeable
    {
        if (!CanSeeKillFlash) return false;

        PlayerControl killer = info.AppearanceKiller, target = info.AttemptTarget;

        //インポスターによるキルかどうかの判別
        var realKiller = target.GetRealKiller() ?? killer;
        return realKiller.Is(CustomRoleTypes.Impostor) && realKiller != target;
    }
    public bool CanMakeSidekick() => CanCreateMadmate; // ISidekickable

    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    {
        if (rpcType != CustomRPC.SetEvilTrackerTarget) return;

        var operation = (TargetOperation)reader.ReadByte();

        switch (operation)
        {
            case TargetOperation.ReEnableTargeting: ReEnableTargeting(); break;
            case TargetOperation.RemoveTarget: RemoveTarget(); break;
            case TargetOperation.SetTarget: SetTarget(reader.ReadByte()); break;
            default: Logger.Warn($"不明なオペレーション: {operation}", nameof(EvilTracker)); break;
        }
    }
    private void ReEnableTargeting()
    {
        CanSetTarget = true;
        if (AmongUsClient.Instance.AmHost)
        {
            using var sender = CreateSender(CustomRPC.SetEvilTrackerTarget);
            sender.Writer.Write((byte)TargetOperation.ReEnableTargeting);
        }
    }
    private void RemoveTarget()
    {
        TargetId = byte.MaxValue;
        if (AmongUsClient.Instance.AmHost)
        {
            using var sender = CreateSender(CustomRPC.SetEvilTrackerTarget);
            sender.Writer.Write((byte)TargetOperation.RemoveTarget);
        }
    }
    private void SetTarget(byte targetId)
    {
        TargetId = targetId;
        if (CurrentTargetMode != TargetMode.Always)
        {
            CanSetTarget = false;
        }
        TargetArrow.Add(Player.PlayerId, targetId);
        if (AmongUsClient.Instance.AmHost)
        {
            using var sender = CreateSender(CustomRPC.SetEvilTrackerTarget);
            sender.Writer.Write((byte)TargetOperation.SetTarget);
            sender.Writer.Write(targetId);
        }
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.ShapeshifterCooldown = CanTarget() ? 1f : 255f;
        AURoleOptions.ShapeshifterDuration = 1f;
    }
    public override string GetAbilityButtonText() => GetString("EvilTrackerChangeButtonText");
    public override bool CanUseAbilityButton() => CanTarget();

    // 値取得の関数
    private bool CanTarget() => Player.IsAlive() && CanSetTarget;
    private bool IsTrackTarget(PlayerControl target)
        => Player.IsAlive() && target.IsAlive() && !Is(target)
        && (target.Is(CustomRoleTypes.Impostor) || TargetId == target.PlayerId);

    // 各所で呼ばれる処理
    public override bool OnCheckShapeshift(PlayerControl target, ref bool animate)
    {
        //ターゲット出来ない、もしくはターゲットが味方の場合は処理しない
        //※どちらにしろシェイプシフトは出来ない
        if (!CanTarget() || target.Is(CustomRoleTypes.Impostor)) return false;

        SetTarget(target.PlayerId);
        Logger.Info($"{Player.GetNameWithRole()}のターゲットを{target.GetNameWithRole()}に設定", "EvilTrackerTarget");
        Player.MarkDirtySettings();
        Utils.NotifyRoles();
        return false;
    }
    public override void AfterMeetingTasks()
    {
        if (CurrentTargetMode == TargetMode.EveryMeeting)
        {
            ReEnableTargeting();
            Player.MarkDirtySettings();
        }
        var target = Utils.GetPlayerById(TargetId);
        if (!Player.IsAlive() || !target.IsAlive())
        {
            RemoveTarget();
        }
        Player.SyncSettings();
        Player.RpcResetAbilityCooldown();
    }

    // 表示系の関数群
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (TargetId != seen.PlayerId) return string.Empty;

        string mark = "";
        if (isForMeeting)
        {
            mark = "\n<size=80%>" + GetLastRoom(seen) + "</size>";
        }
        return Utils.ColorString(Palette.ImpostorRed, "◀") + mark;
    }
    public override string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (isForMeeting) return string.Empty;

        return GetArrows(seen);
    }
    private string GetArrows(PlayerControl seen)
    {
        if (!Is(seen)) return "";

        var trackerId = Player.PlayerId;

        ImpostorsId.RemoveWhere(id => PlayerState.GetByPlayerId(id).IsDead);

        var sb = new StringBuilder(80);
        if (ImpostorsId.Count > 0)
        {
            sb.Append($"<color={Utils.GetRoleColorCode(CustomRoles.Impostor)}>");
            foreach (var impostorId in ImpostorsId)
            {
                sb.Append(TargetArrow.GetArrows(Player, impostorId));
            }
            sb.Append($"</color>");
        }

        if (TargetId != byte.MaxValue)
        {
            sb.Append(Utils.ColorString(Color.white, TargetArrow.GetArrows(Player, TargetId)));
        }
        return sb.ToString();
    }
    public string GetLastRoom(PlayerControl seen)
    {
        if (!(CanSeeLastRoomInMeeting && IsTrackTarget(seen))) return "";

        string text = Utils.ColorString(Palette.ImpostorRed, TargetArrow.GetArrows(Player, seen.PlayerId));
        var room = PlayerState.GetByPlayerId(seen.PlayerId).LastRoom;
        if (room == null) text += Utils.ColorString(Color.gray, "@" + GetString("FailToTrack"));
        else
        {
            text += Utils.ColorString(Palette.ImpostorRed, "@" + GetString(room.RoomId.ToString()));
        }

        return text;
    }
}