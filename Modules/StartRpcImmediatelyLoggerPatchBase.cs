using HarmonyLib;
using InnerNet;

namespace TownOfHostY
{
    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNet.InnerNetClient.StartRpcImmediately)), HarmonyPatch(typeof(InnerNetClient), nameof(InnerNet.InnerNetClient.StartRpcImmediately))]
    public static class StartRpcImmediatelyLoggerPatchBase
    {
    }
}