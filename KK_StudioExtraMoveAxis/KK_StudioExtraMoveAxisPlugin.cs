using BepInEx;
using KKAPI;

namespace StudioExtraMoveAxis
{
    [BepInProcess(KoikatuAPI.StudioProcessName)]
    [BepInIncompatibility("ShortcutsKoi")]
    [BepInIncompatibility("shortcutsKoi.guideObjectPort")]
    public partial class StudioExtraMoveAxisPlugin : BaseUnityPlugin
    {
    }
}