using AmongUs.GameOptions;
using TownOfHost.Roles.Core;

namespace TownOfHost.Roles.Vanilla;

public sealed class Detective : RoleBase
{
    public Detective(PlayerControl player) : base(RoleInfo, player) { }
    public readonly static SimpleRoleInfo RoleInfo = SimpleRoleInfo.CreateForVanilla(typeof(Detective), player => new Detective(player), RoleTypes.Detective, "#8cffff");
}
