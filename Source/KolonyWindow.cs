using System;
using System.Collections.Generic;
using KSP.UI.Screens;
using UnityEngine;

namespace KolonySpaceCenters
{
    /// <summary>Toolbar button and the main window (base panel, base management, Space Center view selector).</summary>
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class KolonyWindow : MonoBehaviour
    {
        public static KolonyWindow Instance { get; private set; }

        /// <summary>True while the mouse is over the Kolony window (so scene clicks underneath are ignored).</summary>
        public static bool MouseOverWindow { get; private set; }

        private const string IconPath = "KolonySpaceCenters/Textures/icon";
        private static readonly int WindowId = "KolonySpaceCenters".GetHashCode();

        private ApplicationLauncherButton button;
        private bool visible;
        private Rect windowRect = new Rect(200f, 120f, 470f, 460f);
        private Vector2 scroll;
        private int tab;
        private string customName = "";
        private List<KKBridge.SiteInfo> kkSites;
        private bool showHomeSites;
        private bool showAllVesselTypes;
        private KolonyCenter focusCenter;
        private KolonyCenter facilityEditCenter;
        private FacilityRole pickListRole = FacilityRole.None;
        private List<KKBridge.StaticInfo> pickList;
        private GUIStyle headStyle, smallStyle, greenStyle, redStyle, greyStyle;
        private bool stylesReady;

        private static readonly string[] Tabs = { "Base", "Manage bases", "Space Center view" };

        private void Awake()
        {
            GameScenes s = HighLogic.LoadedScene;
            if (s != GameScenes.FLIGHT && s != GameScenes.TRACKSTATION && s != GameScenes.SPACECENTER)
            {
                enabled = false; // prevents Start/OnGUI from running before the deferred Destroy
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(RemoveButton);
            if (ApplicationLauncher.Ready) AddButton();
        }

        private void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(RemoveButton);
            RemoveButton();
            if (Instance == this) Instance = null;
        }

        private void AddButton()
        {
            if (button != null || ApplicationLauncher.Instance == null) return;
            Texture2D tex = GameDatabase.Instance.GetTexture(IconPath, false);
            if (tex == null)
            {
                tex = new Texture2D(38, 38, TextureFormat.ARGB32, false);
                Color[] px = new Color[38 * 38];
                for (int i = 0; i < px.Length; i++) px[i] = new Color(0.18f, 0.55f, 0.85f, 1f);
                tex.SetPixels(px);
                tex.Apply();
            }
            button = ApplicationLauncher.Instance.AddModApplication(
                () => { visible = true; RefreshKK(); },
                () => { visible = false; },
                null, null, null, null,
                ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW | ApplicationLauncher.AppScenes.TRACKSTATION | ApplicationLauncher.AppScenes.SPACECENTER,
                tex);
        }

        private void RemoveButton()
        {
            if (button != null && ApplicationLauncher.Instance != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(button);
            }
            button = null;
        }

        public void ShowBasePanel()
        {
            tab = 0;
            visible = true;
            if (button != null) button.SetTrue(false);
        }

        /// <summary>Open the window on a specific center (used by the map icons).</summary>
        public void ShowCenter(KolonyCenter c)
        {
            focusCenter = c;
            ShowBasePanel();
        }

        private void RefreshKK()
        {
            kkSites = KKBridge.GetLaunchSites();
            kkSites.Sort((a, b) =>
            {
                int c = string.Compare(a.BodyName, b.BodyName, StringComparison.Ordinal);
                return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });
        }

        private void EnsureStyles()
        {
            if (stylesReady) return;
            headStyle = new GUIStyle(HighLogic.Skin.label) { fontStyle = FontStyle.Bold, fontSize = 14 };
            smallStyle = new GUIStyle(HighLogic.Skin.label) { fontSize = 11, wordWrap = true };
            greenStyle = new GUIStyle(HighLogic.Skin.label) { normal = { textColor = new Color(0.55f, 1f, 0.55f) } };
            redStyle = new GUIStyle(HighLogic.Skin.label) { normal = { textColor = new Color(1f, 0.6f, 0.5f) } };
            greyStyle = new GUIStyle(HighLogic.Skin.label) { normal = { textColor = new Color(0.75f, 0.75f, 0.75f) }, fontSize = 11, wordWrap = true };
            stylesReady = true;
        }

        private void OnGUI()
        {
            if (!visible || KolonyScenario.Instance == null)
            {
                MouseOverWindow = false;
                return;
            }
            EnsureStyles();
            GUI.skin = HighLogic.Skin;
            windowRect = GUILayout.Window(WindowId, windowRect, DrawWindow, "Kolony Space Centers", GUILayout.MinWidth(470f));
            Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            MouseOverWindow = windowRect.Contains(m);
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            int newTab = GUILayout.Toolbar(tab, Tabs);
            if (newTab != tab)
            {
                tab = newTab;
                if (tab == 1) RefreshKK();
            }
            GUILayout.Space(6f);

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(370f));
            try
            {
                switch (tab)
                {
                    case 0: DrawBasePanel(); break;
                    case 1: DrawManage(); break;
                    case 2: DrawSpaceCenterView(); break;
                }
            }
            catch (Exception e)
            {
                GUILayout.Label("UI error: " + e.Message, redStyle);
            }
            GUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            FacilityBinder fb = FacilityBinder.Instance;
            if (fb != null && fb.PendingAssignRole != FacilityRole.None)
            {
                GUILayout.Label("Click a building to assign: " + FacilityRoles.DisplayName(fb.PendingAssignRole), greenStyle);
                if (GUILayout.Button("Cancel", GUILayout.Width(70f))) fb.CancelAssign();
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Width(80f)))
            {
                visible = false;
                if (button != null) button.SetFalse(false);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        // ------------------------------------------------------------------ shared pieces

        private void DrawFacilityButtons(KolonyCenter c, bool inRealSpaceCenterScene)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Vehicle Assembly Building")) SceneActions.OpenFacility(c, FacilityRole.VAB);
            if (GUILayout.Button("Spaceplane Hangar")) SceneActions.OpenFacility(c, FacilityRole.SPH);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Tracking Station")) SceneActions.OpenFacility(c, FacilityRole.TrackingStation);
            if (GUILayout.Button("Astronaut Complex")) SceneActions.OpenFacility(c, FacilityRole.AstronautComplex);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Research & Development")) SceneActions.OpenFacility(c, FacilityRole.ResearchAndDevelopment);
            if (GUILayout.Button("Mission Control")) SceneActions.OpenFacility(c, FacilityRole.MissionControl);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Administration")) SceneActions.OpenFacility(c, FacilityRole.Administration);
            if (!inRealSpaceCenterScene && GUILayout.Button("Kerbin Space Center (KSC)"))
            {
                if (GhostView.Active) GhostView.GoToRealSpaceCenter(FacilityRole.None);
                else SceneActions.OpenSpaceCenter(true);
            }
            GUILayout.EndHorizontal();
            if (!inRealSpaceCenterScene && !c.IsOnHomeWorld)
            {
                GUILayout.Label("Astronaut Complex, R&D, Mission Control and Administration are stock screens that only exist in the KSC scene; those buttons take you there and open the screen.", greyStyle);
            }
        }

        private void DrawParkedList(KolonyCenter c, Vessel active)
        {
            List<Vessel> here = SceneActions.VesselsAt(c);
            int crew = 0;
            foreach (Vessel hv in here) crew += hv.GetCrewCount();
            GUILayout.Label(here.Count + " vessel(s) parked, " + crew + " kerbal(s) present", smallStyle);
            foreach (Vessel hv in here)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label((hv == active ? "> " : "   ") + hv.vesselName + "  [" + hv.vesselType + "]" + (hv.GetCrewCount() > 0 ? "  crew " + hv.GetCrewCount() : ""), smallStyle);
                GUILayout.FlexibleSpace();
                if (HighLogic.LoadedScene == GameScenes.FLIGHT && hv != active && hv.loaded && GUILayout.Button("Switch", GUILayout.Width(60f)))
                {
                    FlightGlobals.SetActiveVessel(hv);
                }
                GUILayout.EndHorizontal();
            }
        }

        // ------------------------------------------------------------------ Tab 0: base panel

        private void DrawBasePanel()
        {
            KolonyScenario sc = KolonyScenario.Instance;
            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                if (GhostView.Active) DrawGhostSpaceCenterPanel(sc);
                else DrawFlightBasePanel(sc);
            }
            else
            {
                DrawRemoteBasePanel(sc);
            }
        }

        private void DrawGhostSpaceCenterPanel(KolonyScenario sc)
        {
            KolonyCenter c = GhostView.Center;
            if (c == null)
            {
                GUILayout.Label("Space Center view (unknown base)", headStyle);
                if (GUILayout.Button("Tracking Station")) SceneActions.OpenTrackingStation();
                return;
            }
            GUILayout.Label(c.Name + " Space Center", headStyle);
            GUILayout.Label(c.BodyName + "  |  recovery radius " + FlightMonitor.FormatDistance(c.RadiusMeters) + (c.IsKKSite ? "  |  launch site " + c.KKSiteName : "") + (c.IsVessel ? "  |  station" : ""), greyStyle);
            GUILayout.Label(c.IsVessel
                ? "Right-drag to look around, scroll to zoom. Designated parts light up when hovered and open when clicked. F2 hides the overlay for screenshots."
                : "Right-drag to look around, scroll to zoom. Designated buildings light up when hovered and open when clicked. F2 hides the overlay for screenshots.", greyStyle);
            GUILayout.Space(6f);
            GUILayout.Label("Facilities", headStyle);
            DrawFacilityButtons(c, false);
            if (c.IsVessel)
            {
                GUILayout.Label("A station has no launch pad: craft built in the VAB/SPH launch from the launch site chosen in the editor. Vessels arriving within " + FlightMonitor.FormatDistance(c.RadiusMeters) + " and matching velocity can be recovered; undock first, a docked craft is part of the station.", greyStyle);
                if (GhostView.Instance != null && GUILayout.Button("Leave Space Center mode and fly this vessel")) GhostView.Instance.ExitView();
            }
            GUILayout.Space(8f);
            GUILayout.Label("At this base", headStyle);
            DrawParkedList(c, null);
        }

        private void DrawFlightBasePanel(KolonyScenario sc)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            FlightMonitor fm = FlightMonitor.Instance;
            if (v == null || fm == null)
            {
                GUILayout.Label("No active vessel.");
                return;
            }

            KolonyCenter inside = fm.CurrentCenter;
            KolonyCenter nearest = fm.NearestCenter;
            KolonyCenter own = sc.GetByVessel(v.id);
            if (own != null && (nearest == null || inside == null)) nearest = own;

            if (nearest == null)
            {
                GUILayout.Label("No flagged space center on " + v.mainBody.displayName.Replace("^N", "") + ".", headStyle);
                GUILayout.Label("Flag a Kerbal Konstructs launch site or drop a marker at this location in the 'Manage bases' tab.", smallStyle);
                return;
            }

            KolonyCenter c = inside ?? nearest;
            GUILayout.Label(c.Name, headStyle);
            GUILayout.Label(c.BodyName + "  |  radius " + FlightMonitor.FormatDistance(c.RadiusMeters) + "  |  " + c.KindLabel, greyStyle);

            if (inside != null)
            {
                GUILayout.Label("Inside recovery zone, " + FlightMonitor.FormatDistance(fm.CurrentDistance) + " from center", greenStyle);
                if (!sc.IsActive(c)) GUILayout.Label("This base is disabled or its launch site is closed; recovery is unavailable.", redStyle);
                else if (!KolonySettings.RecoveryEnabled) GUILayout.Label("Recovery at bases is turned off in difficulty settings.", redStyle);
            }
            else if (c.IsAnchor(v))
            {
                GUILayout.Label("This vessel is the station itself. Nearby craft can be recovered here; the station cannot recover itself.", greenStyle);
            }
            else
            {
                string why = v.LandedOrSplashed || c.IsVessel ? "" : " (not landed)";
                GUILayout.Label("Outside recovery zone: " + FlightMonitor.FormatDistance(fm.NearestDistance) + " away" + why, redStyle);
            }

            if (c.IsKKSite)
            {
                bool open = KKBridge.IsSiteOpen(c.KKSiteName);
                GUILayout.Label("Launch site " + c.KKSiteName + ": " + (open ? "open" : "closed"), open ? greenStyle : redStyle);
            }

            GUILayout.Space(8f);
            GUILayout.Label("Facilities", headStyle);

            bool stopped = c.IsStopped(v);
            bool canRecover = inside != null && v.IsRecoverable && stopped && sc.IsActive(c);
            GUI.enabled = canRecover;
            if (GUILayout.Button("Recover Vessel" + (canRecover ? "" : (inside == null ? "  (out of range)" : !stopped ? (c.IsVessel ? "  (match velocity first)" : "  (stop first)") : ""))))
            {
                SceneActions.RecoverActiveVessel();
            }
            GUI.enabled = true;
            DrawFacilityButtons(c, false);
            if (!c.IsOnHomeWorld && GUILayout.Button("Open " + c.Name + " Space Center view"))
            {
                GhostSpaceCenter.Enter(c);
            }
            if (c.IsKKSite)
            {
                GUILayout.Label("Opening an editor from here pre-selects this base's launch site in Kerbal Konstructs.", greyStyle);
            }
            else if (c.IsVessel)
            {
                GUILayout.Label("A station has no launch pad: new craft launch from the launch site selected in the editor. Undock before recovering; a docked craft is part of the station.", greyStyle);
            }
            else
            {
                GUILayout.Label("This base is a free marker (no KK launch site), so new craft still launch from the launch site selected in the editor.", greyStyle);
            }

            GUILayout.Space(8f);
            GUILayout.Label("At this base", headStyle);
            DrawParkedList(c, v);
        }

        private void DrawRemoteBasePanel(KolonyScenario sc)
        {
            bool inSC = HighLogic.LoadedScene == GameScenes.SPACECENTER;
            if (inSC && SpaceCenterRelocator.Relocated && SpaceCenterRelocator.RelocatedTo != null)
            {
                KolonyCenter rc = SpaceCenterRelocator.RelocatedTo;
                GUILayout.Label(rc.Name + " Space Center", headStyle);
                GUILayout.Label("Designated buildings open when clicked; everything is also here:", greyStyle);
                DrawFacilityButtons(rc, true);
                GUILayout.Space(10f);
            }

            GUILayout.Label("Flagged space centers", headStyle);
            if (sc.Centers.Count == 0)
            {
                GUILayout.Label("None yet. Use 'Manage bases' to flag a Kerbal Konstructs launch site.", smallStyle);
                return;
            }
            foreach (KolonyCenter c in sc.Centers)
            {
                bool focused = focusCenter == c;
                GUILayout.BeginVertical(HighLogic.Skin.box);
                GUILayout.BeginHorizontal();
                GUILayout.Label(c.Name, focused ? greenStyle : headStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label(c.BodyName, greyStyle);
                GUILayout.EndHorizontal();
                List<Vessel> here = SceneActions.VesselsAt(c);
                GUILayout.Label(here.Count + " vessel(s), " + SceneActions.CrewAt(c) + " kerbal(s), radius " + FlightMonitor.FormatDistance(c.RadiusMeters)
                    + ", " + c.Facilities.Count + " designated building(s)" + (c.Enabled ? "" : "  [disabled]"), smallStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Open Space Center view")) OpenSpaceCenterView(c);
                if (GUILayout.Button("Vehicle Assembly Building", GUILayout.Width(190f))) SceneActions.OpenFacility(c, FacilityRole.VAB);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
        }

        /// <summary>Select the center as the Space Center and open its view (camera relocation or anchor view).</summary>
        private void OpenSpaceCenterView(KolonyCenter c)
        {
            KolonyScenario sc = KolonyScenario.Instance;
            sc.SelectSpaceCenter(c.Name);
            if (c.IsOnHomeWorld || NativeSpaceCenter.Handles(c))
            {
                if (HighLogic.LoadedScene == GameScenes.FLIGHT) SceneActions.OpenSpaceCenter(true);
                else
                {
                    GhostSpaceCenter.SuppressRedirectOnce = true;
                    HighLogic.LoadScene(GameScenes.SPACECENTER);
                }
            }
            else
            {
                GhostSpaceCenter.Enter(c);
            }
        }

        // ------------------------------------------------------------------ Tab 1: manage

        private void DrawManage()
        {
            KolonyScenario sc = KolonyScenario.Instance;

            GUILayout.Label("Flagged space centers", headStyle);
            if (sc.Centers.Count == 0) GUILayout.Label("None yet.", smallStyle);
            KolonyCenter toRemove = null;
            foreach (KolonyCenter c in sc.Centers)
            {
                GUILayout.BeginVertical(HighLogic.Skin.box);
                GUILayout.BeginHorizontal();
                GUILayout.Label(c.Name + "  (" + c.BodyName + ")", headStyle);
                GUILayout.FlexibleSpace();
                bool en = GUILayout.Toggle(c.Enabled, "Enabled");
                if (en != c.Enabled) { c.Enabled = en; sc.FireChanged(); }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Radius " + FlightMonitor.FormatDistance(c.RadiusMeters), smallStyle, GUILayout.Width(110f));
                bool changed = false;
                if (GUILayout.Button("-5", GUILayout.Width(36f))) { c.RadiusMeters = Math.Max(500d, c.RadiusMeters - 5000d); changed = true; }
                if (GUILayout.Button("-1", GUILayout.Width(36f))) { c.RadiusMeters = Math.Max(500d, c.RadiusMeters - 1000d); changed = true; }
                if (GUILayout.Button("+1", GUILayout.Width(36f))) { c.RadiusMeters = Math.Min(500000d, c.RadiusMeters + 1000d); changed = true; }
                if (GUILayout.Button("+5", GUILayout.Width(36f))) { c.RadiusMeters = Math.Min(500000d, c.RadiusMeters + 5000d); changed = true; }
                if (changed) sc.FireChanged();
                GUILayout.FlexibleSpace();
                GUILayout.Label(c.IsKKSite ? "KK: " + c.KKSiteName : c.IsVessel ? "vessel" : "marker", greyStyle);
                if (GUILayout.Button(facilityEditCenter == c ? (c.IsVessel ? "Hide parts" : "Hide buildings") : (c.IsVessel ? "Parts" : "Buildings"), GUILayout.Width(110f)))
                {
                    facilityEditCenter = facilityEditCenter == c ? null : c;
                    pickListRole = FacilityRole.None;
                    pickList = null;
                }
                if (GUILayout.Button("Remove", GUILayout.Width(70f))) toRemove = c;
                GUILayout.EndHorizontal();
                if (c.IsVessel)
                {
                    Vessel av = c.AnchorVessel;
                    if (av != null) GUILayout.Label("Vessel: " + av.vesselName + "  [" + av.vesselType + "]  " + Vessel.GetSituationString(av.situation).ToLower() + " " + (av.mainBody != null ? av.mainBody.displayName.Replace("^N", "") : ""), greyStyle);
                    else if (c.AnchorProto != null) GUILayout.Label("Vessel: " + c.AnchorProto.vesselName, greyStyle);
                    else GUILayout.Label("Vessel no longer exists (destroyed or recovered). Remove this center.", redStyle);
                }
                else
                {
                    GUILayout.Label(string.Format("lat {0:F3}  lon {1:F3}  alt {2:N0} m", c.Latitude, c.Longitude, c.Altitude), greyStyle);
                }
                if (facilityEditCenter == c) DrawFacilityEditor(c);
                GUILayout.EndVertical();
            }
            if (toRemove != null) sc.Remove(toRemove);

            GUILayout.Space(10f);
            if (HighLogic.LoadedScene == GameScenes.FLIGHT && FlightGlobals.ActiveVessel != null && !GhostView.Active)
            {
                Vessel v = FlightGlobals.ActiveVessel;
                GUILayout.Label("Drop a marker at this vessel", headStyle);
                GUILayout.Label("For bases built from parts or statics without a KK launch site. Uses the vessel's current position.", greyStyle);
                GUILayout.BeginHorizontal();
                customName = GUILayout.TextField(customName, 40, GUILayout.Width(220f));
                GUI.enabled = v.LandedOrSplashed;
                if (GUILayout.Button(v.LandedOrSplashed ? "Flag this location" : "Land first"))
                {
                    string name = string.IsNullOrEmpty(customName.Trim()) ? v.vesselName + " Base" : customName.Trim();
                    sc.AddCustom(name, v.mainBody, v.latitude, v.longitude, v.altitude);
                    customName = "";
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Space(10f);
            }

            DrawVesselFlagging(sc);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Kerbal Konstructs launch sites", headStyle);
            GUILayout.FlexibleSpace();
            showHomeSites = GUILayout.Toggle(showHomeSites, "show " + FlightGlobals.GetHomeBodyName());
            if (GUILayout.Button("Refresh", GUILayout.Width(70f))) RefreshKK();
            GUILayout.EndHorizontal();

            if (!KKBridge.Available)
            {
                GUILayout.Label("Kerbal Konstructs is not installed; only markers are available.", redStyle);
                return;
            }
            if (kkSites == null) RefreshKK();
            string home = FlightGlobals.GetHomeBodyName();
            string lastBody = null;
            int shown = 0;
            foreach (KKBridge.SiteInfo site in kkSites)
            {
                if (site.IsStockKSC) continue;
                if (!showHomeSites && site.BodyName == home) continue;
                if (site.BodyName != lastBody)
                {
                    lastBody = site.BodyName;
                    GUILayout.Space(4f);
                    GUILayout.Label(lastBody, headStyle);
                }
                shown++;
                KolonyCenter flagged = sc.GetByKKSite(site.Name);
                GUILayout.BeginHorizontal(HighLogic.Skin.box);
                GUILayout.Label(site.Name, flagged != null ? greenStyle : HighLogic.Skin.label);
                GUILayout.Label(site.Category + (site.IsOpen ? "" : ", closed"), greyStyle, GUILayout.Width(110f));
                GUILayout.FlexibleSpace();
                if (flagged == null)
                {
                    if (GUILayout.Button("Set as Space Center", GUILayout.Width(150f))) sc.FlagKKSite(site);
                }
                else
                {
                    if (GUILayout.Button("Unflag", GUILayout.Width(150f))) sc.Remove(flagged);
                }
                GUILayout.EndHorizontal();
            }
            if (shown == 0) GUILayout.Label("No off-world KK launch sites found. Build one with the KK editor (Ctrl+K) and give it a LaunchSite, or tick 'show " + home + "'.", smallStyle);
        }

        private void DrawVesselFlagging(KolonyScenario sc)
        {
            GUILayout.Label("Stations and ships", headStyle);
            GUILayout.Label("A flagged vessel is a Space Center that moves with it: recover craft that come within its radius, open its Space Center view, designate its parts as facilities.", greyStyle);
            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                Vessel v = FlightGlobals.ActiveVessel;
                if (v != null && !GhostSpaceCenter.IsGhost(v))
                {
                    KolonyCenter existing = sc.GetByVessel(v.id);
                    GUILayout.BeginHorizontal(HighLogic.Skin.box);
                    GUILayout.Label(v.vesselName + "  [" + v.vesselType + "]", existing != null ? greenStyle : HighLogic.Skin.label);
                    GUILayout.FlexibleSpace();
                    if (existing == null)
                    {
                        if (GUILayout.Button("Set as Space Center", GUILayout.Width(150f))) sc.FlagVessel(v, v.vesselName);
                    }
                    else if (GUILayout.Button("Unflag", GUILayout.Width(150f))) sc.Remove(existing);
                    GUILayout.EndHorizontal();
                }
            }
            else if (HighLogic.LoadedSceneHasPlanetarium && FlightGlobals.Vessels != null)
            {
                showAllVesselTypes = GUILayout.Toggle(showAllVesselTypes, "show all vessel types (not just stations and bases)");
                int shown = 0;
                foreach (Vessel v in FlightGlobals.Vessels)
                {
                    if (v == null || GhostSpaceCenter.IsGhost(v)) continue;
                    VesselType t = v.vesselType;
                    if (t == VesselType.Debris || t == VesselType.SpaceObject || t == VesselType.Unknown || t == VesselType.EVA || t == VesselType.Flag || t == VesselType.DeployedScienceController || t == VesselType.DeployedSciencePart) continue;
                    if (!showAllVesselTypes && t != VesselType.Station && t != VesselType.Base) continue;
                    shown++;
                    KolonyCenter existing = sc.GetByVessel(v.id);
                    GUILayout.BeginHorizontal(HighLogic.Skin.box);
                    GUILayout.Label(v.vesselName, existing != null ? greenStyle : HighLogic.Skin.label);
                    GUILayout.Label(t + ", " + (v.mainBody != null ? v.mainBody.displayName.Replace("^N", "") : "?"), greyStyle, GUILayout.Width(150f));
                    GUILayout.FlexibleSpace();
                    if (existing == null)
                    {
                        if (GUILayout.Button("Set as Space Center", GUILayout.Width(150f))) sc.FlagVessel(v, v.vesselName);
                    }
                    else if (GUILayout.Button("Unflag", GUILayout.Width(150f))) sc.Remove(existing);
                    GUILayout.EndHorizontal();
                }
                if (shown == 0) GUILayout.Label("No stations or bases found" + (showAllVesselTypes ? "." : "; tick 'show all vessel types' to see ships and landers."), smallStyle);
            }
            GUILayout.Space(10f);
        }

        private void DrawFacilityEditor(KolonyCenter c)
        {
            KolonyScenario sc = KolonyScenario.Instance;
            GUILayout.Space(4f);
            GUILayout.Label(c.IsVessel ? "Designated parts" : "Designated buildings", headStyle);
            if (!c.IsVessel && !KKBridge.Available)
            {
                GUILayout.Label("Needs Kerbal Konstructs.", redStyle);
                return;
            }
            bool canClickAssign = FacilityBinder.Instance != null && FacilityBinder.ViewedCenter == c;
            string thing = c.IsVessel ? "part" : "building";
            GUILayout.Label(canClickAssign
                ? "Use 'Click' then click a " + thing + " in the scene, or 'List' to pick one."
                : "Open this center's Space Center view (or be there) to assign by clicking; 'List' works from anywhere.", greyStyle);

            foreach (FacilityRole role in FacilityRoles.Assignable)
            {
                string uuid = c.UuidFor(role);
                string title = uuid != null ? FacilityBinder.FacilityTitle(c, uuid) : "(none)";
                GUILayout.BeginHorizontal();
                GUILayout.Label(FacilityRoles.ShortName(role), GUILayout.Width(90f));
                GUILayout.Label(title, uuid != null ? greenStyle : greyStyle);
                GUILayout.FlexibleSpace();
                GUI.enabled = canClickAssign;
                if (GUILayout.Button("Click", GUILayout.Width(50f)))
                {
                    FacilityBinder fb = FacilityBinder.Instance;
                    if (fb.PendingAssignRole == role && fb.AssignCenter == c) fb.CancelAssign();
                    else fb.BeginAssign(c, role);
                }
                GUI.enabled = true;
                if (GUILayout.Button(pickListRole == role ? "Hide" : "List", GUILayout.Width(50f)))
                {
                    if (pickListRole == role) { pickListRole = FacilityRole.None; pickList = null; }
                    else
                    {
                        pickListRole = role;
                        if (c.IsVessel)
                        {
                            pickList = new List<KKBridge.StaticInfo>();
                            foreach (KeyValuePair<string, string> kv in FacilityBinder.PartCandidates(c))
                            {
                                KKBridge.StaticInfo si = new KKBridge.StaticInfo();
                                si.UUID = kv.Key;
                                si.Title = kv.Value;
                                si.Latitude = c.Latitude;
                                si.Longitude = c.Longitude;
                                pickList.Add(si);
                            }
                        }
                        else
                        {
                            pickList = KKBridge.GetStaticsNear(c.BodyName, c.Latitude, c.Longitude, c.RadiusMeters);
                        }
                        pickList.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
                    }
                }
                GUI.enabled = uuid != null;
                if (GUILayout.Button("Clear", GUILayout.Width(55f)))
                {
                    c.Assign(uuid, FacilityRole.None);
                    sc.FireChanged();
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();

                if (pickListRole == role && pickList != null)
                {
                    if (pickList.Count == 0) GUILayout.Label(c.IsVessel ? "   Station parts are not available right now." : "   No KK buildings inside the radius.", greyStyle);
                    CelestialBody body = c.Body;
                    foreach (KKBridge.StaticInfo si in pickList)
                    {
                        string dist = "";
                        if (!c.IsVessel && body != null) dist = "  (" + FlightMonitor.FormatDistance(KolonyCenter.GreatCircle(body.Radius, c.Latitude, c.Longitude, si.Latitude, si.Longitude)) + ")";
                        FacilityRole current = c.RoleOf(si.UUID);
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(20f);
                        GUILayout.Label(si.Title + dist + (current != FacilityRole.None ? "  [" + FacilityRoles.ShortName(current) + "]" : ""), smallStyle);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("Use", GUILayout.Width(50f)))
                        {
                            c.Assign(si.UUID, role);
                            sc.FireChanged();
                            pickListRole = FacilityRole.None;
                            pickList = null;
                        }
                        GUILayout.EndHorizontal();
                    }
                }
            }
        }

        // ------------------------------------------------------------------ Tab 2: Space Center view

        private void DrawSpaceCenterView()
        {
            KolonyScenario sc = KolonyScenario.Instance;
            GUILayout.Label("Which base is your Space Center?", headStyle);
            GUILayout.Label("The selected base is what every 'Space Center' button opens. "
                + FlightGlobals.GetHomeBodyName() + " bases: the Space Center scene camera moves onto the base. "
                + "Bases on other bodies: the real Space Center scene runs on that body, with its terrain and your KK buildings (stock UI, camera, warp to morning). "
                + "Stations open a station view in flight.", greyStyle);
            GUILayout.Space(6f);

            bool stock = string.IsNullOrEmpty(sc.SelectedSpaceCenter) || sc.SelectedCenter == null;
            if (GUILayout.Toggle(stock, "  Stock " + FlightGlobals.GetHomeBodyName() + " Space Center (KSC)", HighLogic.Skin.toggle) && !stock)
            {
                sc.SelectSpaceCenter("");
            }
            foreach (KolonyCenter c in sc.Centers)
            {
                bool sel = sc.SelectedSpaceCenter == c.Name;
                string tag = c.IsOnHomeWorld ? "camera relocation" : c.IsVessel ? "station view, " + c.BodyName : NativeSpaceCenter.Handles(c) ? "Space Center scene on " + c.BodyName : "anchor view on " + c.BodyName;
                if (GUILayout.Toggle(sel, "  " + c.Name + "   (" + tag + ")", HighLogic.Skin.toggle) && !sel)
                {
                    sc.SelectSpaceCenter(c.Name);
                }
            }

            GUILayout.Space(10f);
            KolonyCenter selected = sc.SelectedCenter;
            GUILayout.BeginHorizontal();
            if (selected == null)
            {
                if (GUILayout.Button(HighLogic.LoadedScene == GameScenes.SPACECENTER ? "Reload Space Center" : "Go to Space Center"))
                {
                    if (HighLogic.LoadedScene == GameScenes.FLIGHT) SceneActions.OpenSpaceCenter(true);
                    else { GhostSpaceCenter.SuppressRedirectOnce = true; HighLogic.LoadScene(GameScenes.SPACECENTER); }
                }
            }
            else
            {
                if (GUILayout.Button("Go to " + selected.Name + " Space Center")) OpenSpaceCenterView(selected);
                if (GUILayout.Button("Go to stock KSC", GUILayout.Width(140f)))
                {
                    if (GhostView.Active) GhostView.GoToRealSpaceCenter(FacilityRole.None);
                    else if (HighLogic.LoadedScene == GameScenes.FLIGHT) SceneActions.OpenSpaceCenter(true);
                    else { GhostSpaceCenter.SuppressRedirectOnce = true; HighLogic.LoadScene(GameScenes.SPACECENTER); }
                }
            }
            GUILayout.EndHorizontal();
            if (!KolonySettings.RedirectSpaceCenter)
            {
                GUILayout.Label("'Space Center button opens selected base' is off in difficulty settings, so stock Space Center buttons still go to the KSC.", redStyle);
            }
        }
    }
}
