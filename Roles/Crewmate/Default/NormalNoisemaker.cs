using AmongUs.GameOptions;

using TownOfHostY.Roles.Core;

namespace TownOfHostY.Roles.Crewmate;
public sealed class NormalNoisemaker : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(NormalNoisemaker),
            player => new NormalNoisemaker(player),
            CustomRoles.NormalNoisemaker,
            () => RoleTypes.Noisemaker,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewDefault + 300,
            SetupOptionItem,
            "ノイズメーカー",
            "#8cffff"
        );
    public NormalNoisemaker(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        impostorAlert = OptionImpostorAlert.GetBool();
        alertDuration = OptionAlertDuration.GetFloat();
    }

    private static OptionItem OptionImpostorAlert;
    private static OptionItem OptionAlertDuration;

    enum OptionName
    {
        NoisemakerImpostorAlert,
        NoisemakerAlertDuration,
    }
    public static bool impostorAlert;
    public static float alertDuration;
    private static void SetupOptionItem()
    {
        OptionImpostorAlert = StringOptionItem.Create(RoleInfo, 3, OptionName.NoisemakerImpostorAlert, new string[] { "OFF", "ON" }, 1, false);
        OptionAlertDuration = FloatOptionItem.Create(RoleInfo, 4, OptionName.NoisemakerAlertDuration, new(1f, 30f, 1f), 10f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.NoisemakerImpostorAlert = impostorAlert;
        AURoleOptions.NoisemakerAlertDuration = alertDuration;
    }
}