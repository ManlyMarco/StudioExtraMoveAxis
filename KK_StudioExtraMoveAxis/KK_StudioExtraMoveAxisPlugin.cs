using BepInEx;

namespace StudioExtraMoveAxis
{
    [BepInProcess("CharaStudio")]
    [BepInIncompatibility("ShortcutsKoi")]
    [BepInIncompatibility("shortcutsKoi.guideObjectPort")]
    public partial class StudioExtraMoveAxisPlugin : BaseUnityPlugin
    {
    }
}