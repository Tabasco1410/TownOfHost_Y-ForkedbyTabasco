using System;
using System.Linq;
using System.Text;
using HarmonyLib;
using TownOfHostY.Modules;
using TownOfHostY.Roles;
using TownOfHostY.Roles.AddOns.Common;
using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Impostor;
using UnityEngine;

namespace TownOfHostY
{
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
    class FixedUpdatePatch
    {
        private static StringBuilder Mark = new(20);
        private static StringBuilder Suffix = new(120);
        public static void Postfix(PlayerControl __instance)
        {
            var player = __instance;

            if (!GameStates.IsModHost) return;

            TargetArrow.OnFixedUpdate(player);

            CustomRoleManager.OnFixedUpdate(player);

            if (AmongUsClient.Instance.AmHost)
            {//実行クライアントがホストの場合のみ実行
                if (GameStates.IsLobby && (ModUpdater.hasUpdate || ModUpdater.isBroken || !Main.AllowPublicRoom || !VersionChecker.IsSupported || !Main.IsPublicAvailableOnThisVersion) && AmongUsClient.Instance.IsGamePublic)
                    AmongUsClient.Instance.ChangeGamePublic(false);

                if (GameStates.IsInTask && ReportDeadBodyPatch.CanReport[__instance.PlayerId] && ReportDeadBodyPatch.WaitReport[__instance.PlayerId].Count > 0)
                {
                    var info = ReportDeadBodyPatch.WaitReport[__instance.PlayerId][0];
                    ReportDeadBodyPatch.WaitReport[__instance.PlayerId].Clear();
                    Logger.Info($"{__instance.GetNameWithRole()}:通報可能になったため通報処理を行います", "ReportDeadbody");
                    __instance.ReportDeadBody(info);
                }

                DoubleTrigger.OnFixedUpdate(player);

                //ターゲットのリセット
                if (GameStates.IsInTask && player.IsAlive() && Options.LadderDeath.GetBool())
                {
                    FallFromLadder.FixedUpdate(player);
                }

                

                if (GameStates.IsInGame && player.AmOwner)
                    DisableDevice.FixedUpdate();

                if (__instance.AmOwner)
                {
                    Utils.ApplySuffix();
                }
            }
            //LocalPlayer専用
            if (__instance.AmOwner)
            {
                //キルターゲットの上書き処理
                if (GameStates.IsInTask && !(__instance.Is(CustomRoleTypes.Impostor) || __instance.Is(CustomRoles.Egoist)) && __instance.CanUseKillButton() && !__instance.Data.IsDead)
                {
                    var players = __instance.GetPlayersInAbilityRangeSorted(false);
                    PlayerControl closest = players.Count <= 0 ? null : players[0];
                    HudManager.Instance.KillButton.SetTarget(closest);
                }
            }

            //役職テキストの表示
            var RoleTextTransform = __instance.cosmetics.nameText.transform.Find("RoleText");
            var RoleText = RoleTextTransform.GetComponent<TMPro.TextMeshPro>();
            if (RoleText != null && __instance != null)
            {
                if (GameStates.IsLobby)
                {
                    if (Main.playerVersion.TryGetValue(__instance.PlayerId, out var ver))
                    {
                        if (Main.ForkId != ver.forkId) // フォークIDが違う場合
                            __instance.cosmetics.nameText.text = $"<color=#ff0000><size=1.2>{ver.forkId}</size>\n{__instance?.name}</color>";
                        else if (Main.version.CompareTo(ver.version) == 0)
                            __instance.cosmetics.nameText.text = ver.tag == $"{ThisAssembly.Git.Commit}({ThisAssembly.Git.Branch})" ? $"<color=#87cefa>{__instance.name}</color>" : $"<color=#ffff00><size=1.2>{ver.tag}</size>\n{__instance?.name}</color>";
                        else __instance.cosmetics.nameText.text = $"<color=#ff0000><size=1.2>v{ver.version}</size>\n{__instance?.name}</color>";
                    }
                    else __instance.cosmetics.nameText.text = __instance?.Data?.PlayerName;
                }
                if (GameStates.IsInGame)
                {
                    //if (Options.CurrentGameMode == CustomGameMode.HideAndSeek)
                    //{
                    //    var hasRole = main.AllPlayerCustomRoles.TryGetValue(__instance.PlayerId, out var role);
                    //    if (hasRole) RoleTextData = Utils.GetRoleTextHideAndSeek(__instance.Data.Role.Role, role);
                    //}
                    (RoleText.enabled, RoleText.text) = Utils.GetRoleNameAndProgressTextData(PlayerControl.LocalPlayer, __instance);
                    if (!AmongUsClient.Instance.IsGameStarted && AmongUsClient.Instance.NetworkMode != NetworkModes.FreePlay)
                    {
                        RoleText.enabled = false; //ゲームが始まっておらずフリープレイでなければロールを非表示
                        if (!__instance.AmOwner) __instance.cosmetics.nameText.text = __instance?.Data?.PlayerName;
                    }

                    //変数定義
                    var seer = PlayerControl.LocalPlayer;
                    var seerRole = seer.GetRoleClass();
                    var target = __instance;
                    string RealName;
                    Mark.Clear();
                    Suffix.Clear();

                    //名前変更
                    RealName = target.GetRealName();

                    //NameColorManager準拠の処理
                    RealName = RealName.ApplyNameColorData(seer, target, false);

                    //seer役職が対象のMark
                    Mark.Append(seerRole?.GetMark(seer, target, false));
                    //seerに関わらず発動するMark
                    Mark.Append(CustomRoleManager.GetMarkOthers(seer, target, false));

                    //ハートマークを付ける(会議中MOD視点)
                    if (__instance.Is(CustomRoles.Lovers) && PlayerControl.LocalPlayer.Is(CustomRoles.Lovers))
                    {
                        Mark.Append($"<color={Utils.GetRoleColorCode(CustomRoles.Lovers)}>♡</color>");
                    }
                    else if (__instance.Is(CustomRoles.Lovers) && PlayerControl.LocalPlayer.Data.IsDead)
                    {
                        Mark.Append($"<color={Utils.GetRoleColorCode(CustomRoles.Lovers)}>♡</color>");
                    }

                    //seerに関わらず発動するLowerText
                    Suffix.Append(CustomRoleManager.GetLowerTextOthers(seer, target));

                    //seer役職が対象のSuffix
                    Suffix.Append(seerRole?.GetSuffix(seer, target));

                    //seerに関わらず発動するSuffix
                    Suffix.Append(CustomRoleManager.GetSuffixOthers(seer, target));

                    /*if(main.AmDebugger.Value && main.BlockKilling.TryGetValue(target.PlayerId, out var isBlocked)) {
                        Mark = isBlocked ? "(true)" : "(false)";
                    }*/
                    if (Utils.IsActive(SystemTypes.Comms) && Options.CommsCamouflage.GetBool())
                        RealName = $"<size=0>{RealName}</size> ";

                    string DeathReason = seer.Data.IsDead && seer.KnowDeathReason(target) ? $"({Utils.ColorString(Utils.GetRoleColor(CustomRoles.Doctor), Utils.GetVitalText(target.PlayerId))})" : "";
                    //Mark・Suffixの適用
                    target.cosmetics.nameText.text = $"{RealName}{DeathReason}{Mark}";

                    if (Suffix.ToString() != "")
                    {
                        //名前が2行になると役職テキストを上にずらす必要がある
                        RoleText.transform.SetLocalY(0.35f);
                        target.cosmetics.nameText.text += "\r\n" + Suffix.ToString();

                    }
                    else
                    {
                        //役職テキストの座標を初期値に戻す
                        RoleText.transform.SetLocalY(0.2f);
                    }
                }
                else
                {
                    //役職テキストの座標を初期値に戻す
                    RoleText.transform.SetLocalY(0.2f);
                }
            }
        }
        
    }
}