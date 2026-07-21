using System.IO;
using System.Reflection;
using HarmonyLib;

namespace LSTY.SevenDPanel.Compatibility
{
    [HarmonyPatch]
    internal static class AssemblyLocationPatch
    {
        [HarmonyTargetMethod]
        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertyGetter(
                typeof(int).Assembly.GetType(),
                nameof(Assembly.Location));
        }

        [HarmonyPostfix]
        private static void Postfix(Assembly __instance, ref string __result)
        {
            if (!string.IsNullOrEmpty(__result)) return;

            var modInstance = ModMain.ModInstance;
            if (modInstance == null || !modInstance.ContainsAssembly(__instance)) return;

            __result = Path.Combine(
                modInstance.Path,
                __instance.GetName().Name + ".dll");
        }
    }
}
