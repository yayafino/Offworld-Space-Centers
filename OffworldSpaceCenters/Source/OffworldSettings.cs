using System.Reflection;

namespace OffworldSpaceCenters
{
    /// <summary>Per-save settings, shown under Difficulty Options → Offworld Space Centers.</summary>
    public class OffworldSettings : GameParameters.CustomParameterNode
    {
        public override string Title { get { return "Offworld Space Centers"; } }
        public override string DisplaySection { get { return "Offworld Space Centers"; } }
        public override string Section { get { return "Offworld Space Centers"; } }
        public override int SectionOrder { get { return 1; } }
        public override GameParameters.GameMode GameMode { get { return GameParameters.GameMode.ANY; } }
        public override bool HasPresets { get { return false; } }

        [GameParameters.CustomFloatParameterUI("Default recovery radius (km)", minValue = 1f, maxValue = 250f, stepCount = 250, displayFormat = "N0",
            toolTip = "New flagged bases get this recovery radius. You can change it per base afterwards.")]
        public float defaultRadiusKm = 10f;

        [GameParameters.CustomParameterUI("Enable recovery at flagged bases",
            toolTip = "Allows the stock Recover button (flight altimeter and Tracking Station) for vessels landed within a flagged base's radius, on any body.")]
        public bool enableRecovery = true;

        [GameParameters.CustomParameterUI("Full recovery value inside radius",
            toolTip = "Pay 100% of part value when recovering inside a flagged base's radius, like landing on the KSC pad. Off = stock distance formula.")]
        public bool fullValueInRadius = true;

        [GameParameters.CustomParameterUI("Require the KK launch site to be open",
            toolTip = "Career only: a base anchored to a Kerbal Konstructs launch site must be purchased/open before it recovers vessels.")]
        public bool requireOpenKKSite = false;

        [GameParameters.CustomParameterUI("Relocate Space Center camera (experimental)",
            toolTip = "When a flagged base on the home world is selected as the Space Center view, the Space Center scene camera opens at that base instead of the KSC.")]
        public bool relocateSpaceCenterCamera = true;

        [GameParameters.CustomParameterUI("Off-world bases use the real Space Center scene",
            toolTip = "On: a selected base on another body opens in the actual Space Center scene (stock UI, camera and warp-to-morning), with that body's terrain and your KK buildings. Off: the older anchor-vessel view in the flight scene is used instead.")]
        public bool nativeOffworldScene = true;

        [GameParameters.CustomParameterUI("Space Center button opens selected base",
            toolTip = "When a base on another body is selected as the Space Center view, every 'Space Center' button (flight, editor, Tracking Station) opens that base's Space Center view instead of the KSC. Pending recoveries still go to the KSC.")]
        public bool redirectSpaceCenter = true;

        [GameParameters.CustomParameterUI("Save immediately when bases change",
            toolTip = "Write the save file as soon as you flag/unflag a base or change the Space Center view, so a reload never loses the change.")]
        public bool saveOnChange = true;

        [GameParameters.CustomParameterUI("Show range messages",
            toolTip = "Post a screen message when a vessel enters or leaves a flagged base's recovery zone.")]
        public bool showRangeMessages = true;

        [GameParameters.CustomParameterUI("Open base panel on arrival",
            toolTip = "Automatically open the base panel when you arrive at a base via the Tracking Station 'Open base view' button.")]
        public bool autoOpenPanel = true;

        public static OffworldSettings Get()
        {
            if (HighLogic.CurrentGame == null) return null;
            try
            {
                return HighLogic.CurrentGame.Parameters.CustomParams<OffworldSettings>();
            }
            catch
            {
                return null;
            }
        }

        public static double DefaultRadiusMeters
        {
            get
            {
                OffworldSettings s = Get();
                return (s != null ? s.defaultRadiusKm : 10f) * 1000d;
            }
        }

        public static bool RecoveryEnabled { get { OffworldSettings s = Get(); return s == null || s.enableRecovery; } }
        public static bool FullValue { get { OffworldSettings s = Get(); return s == null || s.fullValueInRadius; } }
        public static bool RequireOpenKKSite { get { OffworldSettings s = Get(); return s != null && s.requireOpenKKSite; } }
        public static bool NativeOffworldScene { get { OffworldSettings s = Get(); return s == null || s.nativeOffworldScene; } }
        public static bool RedirectSpaceCenter { get { OffworldSettings s = Get(); return s == null || s.redirectSpaceCenter; } }
        public static bool SaveOnChange { get { OffworldSettings s = Get(); return s == null || s.saveOnChange; } }
        public static bool RelocateCamera { get { OffworldSettings s = Get(); return s == null || s.relocateSpaceCenterCamera; } }
        public static bool RangeMessages { get { OffworldSettings s = Get(); return s == null || s.showRangeMessages; } }
        public static bool AutoOpenPanel { get { OffworldSettings s = Get(); return s == null || s.autoOpenPanel; } }

        public override bool Enabled(MemberInfo member, GameParameters parameters) { return true; }
        public override bool Interactible(MemberInfo member, GameParameters parameters) { return true; }
    }
}
