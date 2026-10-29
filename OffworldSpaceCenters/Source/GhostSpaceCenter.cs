using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace OffworldSpaceCenters
{
    /// <summary>
    /// "Space Center view" for bases on bodies other than the home world.
    ///
    /// The stock Space Center scene can only ever load the home world, so instead we spawn a tiny invisible
    /// anchor vessel (part OffworldGhostAnchor) exactly at the base, load the flight scene focused on it, hide the
    /// flight UI, pin the anchor in place and give the camera Space-Center-like framing. KK's buildings load
    /// around the active vessel as usual, designated buildings are clickable, and the Offworld window provides
    /// the facility buttons. The anchor is removed again as soon as any other scene loads.
    /// </summary>
    public static class GhostSpaceCenter
    {
        public const string PartName = "OffworldGhostAnchor";
        public const string NamePrefix = "KSC Anchor: ";

        /// <summary>Name of the center whose view is being entered; consumed by GhostView on flight start.</summary>
        public static string PendingCenterName = "";

        /// <summary>Set to skip the Space Center redirect for the next LoadScene(SPACECENTER) call.</summary>
        public static bool SuppressRedirectOnce;

        public static bool IsGhost(ProtoVessel pv)
        {
            if (pv == null) return false;
            if (!string.IsNullOrEmpty(pv.vesselName) && pv.vesselName.StartsWith(NamePrefix)) return true;
            if (pv.protoPartSnapshots != null && pv.protoPartSnapshots.Count == 1 && pv.protoPartSnapshots[0].partName == PartName) return true;
            return false;
        }

        public static bool IsGhost(Vessel v)
        {
            if (v == null) return false;
            if (!string.IsNullOrEmpty(v.vesselName) && v.vesselName.StartsWith(NamePrefix)) return true;
            if (v.loaded && v.parts != null && v.parts.Count == 1 && v.parts[0].partInfo != null && v.parts[0].partInfo.name == PartName) return true;
            if (!v.loaded && v.protoVessel != null) return IsGhost(v.protoVessel);
            return false;
        }

        public static bool PartAvailable
        {
            get { return PartLoader.getPartInfoByName(PartName) != null; }
        }

        private static FieldInfo recoverSetField;

        /// <summary>Set by the recovery-request event; the stock recovery is processed in the real Space Center scene.</summary>
        public static bool RecoveryRequested;

        /// <summary>True while VesselRetrieval still has a vessel waiting to be recovered in the Space Center scene.</summary>
        public static bool RecoveryPending()
        {
            if (RecoveryRequested) return true;
            try
            {
                VesselRetrieval vr = UnityEngine.Object.FindObjectOfType<VesselRetrieval>();
                if (vr == null) return false;
                if (recoverSetField == null) recoverSetField = typeof(VesselRetrieval).GetField("IDsOfVesselsToRecover", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (recoverSetField == null) return false;
                ICollection set = recoverSetField.GetValue(vr) as ICollection;
                return set != null && set.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Should a LoadScene(SPACECENTER) be redirected into the selected base's Space Center view?</summary>
        public static bool ShouldRedirect(out OffworldCenter center)
        {
            center = null;
            if (SuppressRedirectOnce) { SuppressRedirectOnce = false; return false; }
            if (!OffworldSettings.RedirectSpaceCenter) return false;
            OffworldScenario sc = OffworldScenario.Instance;
            if (sc == null) return false;
            center = sc.SelectedCenter;
            if (center == null || center.IsOnHomeWorld) return false;
            if (NativeSpaceCenter.Handles(center)) return false; // the real Space Center scene will take it from here
            GameScenes s = HighLogic.LoadedScene;
            if (s != GameScenes.FLIGHT && s != GameScenes.TRACKSTATION && s != GameScenes.EDITOR && s != GameScenes.SPACECENTER) return false;
            if (GhostView.Active) return false; // from inside a base view, "Space Center" means the real KSC
            if (RecoveryPending()) return false;
            if (!center.IsVessel && !PartAvailable) return false;
            if (center.IsVessel && !center.AnchorExists) return false;
            return true;
        }

        /// <summary>Space Center view of a vessel-anchored center: simply focus the station itself.</summary>
        private static bool EnterVessel(OffworldCenter center)
        {
            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                Vessel active = FlightGlobals.ActiveVessel;
                if (active != null && center.IsAnchor(active))
                {
                    // already there: just switch the scene into Space Center mode
                    if (GhostView.Instance != null) GhostView.Instance.ActivateVesselView(active, center);
                    return true;
                }
                string reason;
                if (!SceneActions.CanLeaveFlight(out reason))
                {
                    ScreenMessages.PostScreenMessage(reason, 4f, ScreenMessageStyle.UPPER_CENTER);
                    return false;
                }
            }
            try
            {
                RemoveAllGhosts(false);
                int idx = -1;
                PendingCenterName = center.Name;
                GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
                if (HighLogic.LoadedSceneHasPlanetarium)
                {
                    Vessel v = center.AnchorVessel;
                    if (v != null) idx = FlightGlobals.Vessels.IndexOf(v);
                }
                if (idx < 0)
                {
                    List<ProtoVessel> pvs = HighLogic.CurrentGame.flightState.protoVessels;
                    for (int i = 0; i < pvs.Count; i++) if (center.IsAnchor(pvs[i])) { idx = i; break; }
                }
                if (idx < 0)
                {
                    PendingCenterName = "";
                    ScreenMessages.PostScreenMessage(center.Name + ": the station no longer exists.", 5f, ScreenMessageStyle.UPPER_CENTER);
                    return false;
                }
                Log.Info("Entering Space Center view at station " + center + " (vessel index " + idx + ")");
                FlightDriver.StartAndFocusVessel("persistent", idx);
                return true;
            }
            catch (Exception e)
            {
                Log.Error("Enter station view failed: " + e);
                PendingCenterName = "";
                return false;
            }
        }

        /// <summary>Enter the Space Center view of a center on another body. Works from Flight, TS, SC and the editors.</summary>
        public static bool Enter(OffworldCenter center)
        {
            if (center == null) return false;
            if (center.IsVessel) return EnterVessel(center);
            if (NativeSpaceCenter.Handles(center))
            {
                // native mode: select it and load the real Space Center scene
                if (OffworldScenario.Instance != null) OffworldScenario.Instance.SelectSpaceCenter(center.Name);
                if (HighLogic.LoadedScene == GameScenes.FLIGHT)
                {
                    string why;
                    if (!SceneActions.CanLeaveFlight(out why)) { ScreenMessages.PostScreenMessage(why, 4f, ScreenMessageStyle.UPPER_CENTER); return false; }
                    GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
                }
                NativeSpaceCenter.BaseOnce = true;
                HighLogic.LoadScene(GameScenes.SPACECENTER);
                return true;
            }
            CelestialBody body = center.Body;
            if (body == null)
            {
                ScreenMessages.PostScreenMessage("Unknown body for " + center.Name, 4f, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }
            if (!PartAvailable)
            {
                ScreenMessages.PostScreenMessage("OffworldGhostAnchor part is missing; reinstall Offworld Space Centers.", 6f, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }
            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                string reason;
                if (!SceneActions.CanLeaveFlight(out reason))
                {
                    ScreenMessages.PostScreenMessage(reason, 4f, ScreenMessageStyle.UPPER_CENTER);
                    return false;
                }
            }

            try
            {
                RemoveAllGhosts(false);

                uint flightId = ShipConstruction.GetUniqueFlightID(HighLogic.CurrentGame.flightState);
                ConfigNode partNode = ProtoVessel.CreatePartNode(PartName, flightId, null);

                double terrain = body.TerrainAltitude(center.Latitude, center.Longitude, true);
                double alt = Math.Max(terrain, 0d) + 0.6d;
                Orbit orbit = new Orbit(0d, 0d, body.Radius + alt, 0d, 0d, 0d, Planetarium.GetUniversalTime(), body);
                ConfigNode vesselNode = ProtoVessel.CreateVesselNode(NamePrefix + center.Name, VesselType.Base, orbit, 0, new ConfigNode[] { partNode }, new ConfigNode[0]);
                vesselNode.SetValue("sit", Vessel.Situations.LANDED.ToString(), true);
                vesselNode.SetValue("landed", true, true);
                vesselNode.SetValue("splashed", false, true);
                vesselNode.SetValue("lat", center.Latitude.ToString("R"), true);
                vesselNode.SetValue("lon", center.Longitude.ToString("R"), true);
                vesselNode.SetValue("alt", alt.ToString("R"), true);
                vesselNode.SetValue("hgt", "0.6", true);
                Vector3d nrm = body.GetRelSurfaceNVector(center.Latitude, center.Longitude);
                vesselNode.SetValue("nrm", KSPUtil.WriteVector(nrm), true);
                vesselNode.SetValue("rot", KSPUtil.WriteQuaternion(Quaternion.FromToRotation(Vector3.up, (Vector3)nrm)), true);
                vesselNode.SetValue("landedAt", "", true);

                ProtoVessel pv;
                int idx;
                if (HighLogic.LoadedSceneHasPlanetarium)
                {
                    pv = HighLogic.CurrentGame.AddVessel(vesselNode);
                    PendingCenterName = center.Name;
                    GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
                    idx = pv != null && pv.vesselRef != null ? FlightGlobals.Vessels.IndexOf(pv.vesselRef) : -1;
                }
                else
                {
                    pv = new ProtoVessel(vesselNode, HighLogic.CurrentGame);
                    HighLogic.CurrentGame.flightState.protoVessels.Add(pv);
                    PendingCenterName = center.Name;
                    GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
                    idx = HighLogic.CurrentGame.flightState.protoVessels.IndexOf(pv);
                }
                if (idx < 0)
                {
                    Log.Error("Anchor vessel index not found after save; falling back to name search.");
                    for (int i = 0; i < HighLogic.CurrentGame.flightState.protoVessels.Count; i++)
                    {
                        if (IsGhost(HighLogic.CurrentGame.flightState.protoVessels[i])) { idx = i; break; }
                    }
                }
                if (idx < 0)
                {
                    ScreenMessages.PostScreenMessage("Could not create the Space Center anchor.", 5f, ScreenMessageStyle.UPPER_CENTER);
                    PendingCenterName = "";
                    return false;
                }
                Log.Info("Entering Space Center view at " + center + " (anchor index " + idx + ")");
                FlightDriver.StartAndFocusVessel("persistent", idx);
                return true;
            }
            catch (Exception e)
            {
                Log.Error("Enter Space Center view failed: " + e);
                PendingCenterName = "";
                ScreenMessages.PostScreenMessage("Could not open the Space Center view: " + e.Message, 6f, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }
        }

        /// <summary>Delete every anchor vessel from the current game (unloaded vessels and proto vessels).</summary>
        public static int RemoveAllGhosts(bool save)
        {
            int removed = 0;
            try
            {
                if (HighLogic.LoadedSceneHasPlanetarium && FlightGlobals.Vessels != null)
                {
                    List<Vessel> doomed = new List<Vessel>();
                    foreach (Vessel v in FlightGlobals.Vessels)
                    {
                        if (v != null && v != FlightGlobals.ActiveVessel && IsGhost(v)) doomed.Add(v);
                    }
                    foreach (Vessel v in doomed)
                    {
                        try
                        {
                            if (v.loaded) v.Die();
                            else UnityEngine.Object.DestroyImmediate(v.gameObject);
                            removed++;
                        }
                        catch (Exception e) { Log.Warn("Could not remove anchor vessel: " + e.Message); }
                    }
                }
                if (HighLogic.CurrentGame != null && HighLogic.CurrentGame.flightState != null)
                {
                    List<ProtoVessel> pvs = HighLogic.CurrentGame.flightState.protoVessels;
                    for (int i = pvs.Count - 1; i >= 0; i--)
                    {
                        ProtoVessel pv = pvs[i];
                        if (!IsGhost(pv)) continue;
                        if (pv.vesselRef != null && pv.vesselRef == FlightGlobals.ActiveVessel) continue;
                        if (pv.vesselRef != null) continue; // handled above (its OnDestroy drops it from the list)
                        pvs.RemoveAt(i);
                        removed++;
                    }
                }
                if (removed > 0)
                {
                    Log.Info("Removed " + removed + " Space Center anchor vessel(s).");
                    if (save && HighLogic.LoadedSceneIsGame && HighLogic.LoadedScene != GameScenes.FLIGHT)
                    {
                        GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("RemoveAllGhosts: " + e.Message);
            }
            return removed;
        }
    }

    /// <summary>Tracks stock recovery requests so the Space Center redirect never swallows one.</summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class RecoveryRequestTracker : MonoBehaviour
    {
        private void Start()
        {
            GameEvents.OnVesselRecoveryRequested.Add(OnRequested);
            GameEvents.onVesselRecovered.Add(OnRecovered);
            GameEvents.onLevelWasLoaded.Add(OnLevel);
            DontDestroyOnLoad(this);
        }

        private void OnRequested(Vessel v) { GhostSpaceCenter.RecoveryRequested = true; }

        private void OnRecovered(ProtoVessel pv, bool quick) { GhostSpaceCenter.RecoveryRequested = false; }

        private void OnLevel(GameScenes s)
        {
            // the request is consumed by the Space Center scene; anything else means it was abandoned
            if (s != GameScenes.SPACECENTER) GhostSpaceCenter.RecoveryRequested = false;
            else StartCoroutine(ClearLater());
        }

        private IEnumerator ClearLater()
        {
            yield return new WaitForSeconds(3f);
            GhostSpaceCenter.RecoveryRequested = false;
        }
    }

    /// <summary>Lives on the anchor part: invisible, pinned, and always counted as landed.</summary>
    public class ModuleOffworldAnchor : PartModule
    {
        public override void OnStart(StartState state)
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            Pin();
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || part == null) return;
            part.PermanentGroundContact = true;
            Rigidbody rb = part.Rigidbody;
            if (rb != null && !rb.isKinematic)
            {
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }
        }

        private void Pin()
        {
            try
            {
                foreach (Renderer r in part.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                foreach (Collider c in part.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                part.PermanentGroundContact = true;
                Rigidbody rb = part.Rigidbody;
                if (rb != null) { rb.isKinematic = true; rb.detectCollisions = false; }
                if (vessel != null) vessel.Landed = true;
            }
            catch (Exception e) { Log.Warn("Anchor pin: " + e.Message); }
        }
    }

    /// <summary>Runs in flight: turns the anchor vessel's flight scene into the Space Center view.</summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class GhostView : MonoBehaviour
    {
        public static bool Active { get; private set; }
        public static OffworldCenter Center { get; private set; }
        public static GhostView Instance { get; private set; }

        private const string LockId = "OffworldGhostView";
        private const ControlTypes Locks = ControlTypes.ALL_SHIP_CONTROLS | ControlTypes.STAGING | ControlTypes.EVA_INPUT
            | ControlTypes.ACTIONS_ALL | ControlTypes.GROUPS_ALL | ControlTypes.VESSEL_SWITCHING | ControlTypes.QUICKLOAD;

        private Vessel ghost;
        private bool anchorMode; // true = invisible anchor part, false = a real vessel (station) is the center
        private readonly FlightUiHider uiHider = new FlightUiHider();
        private bool showHud = true;

        private void Awake()
        {
            Instance = this;
            Active = false;
            Center = null;
        }

        private void Start()
        {
            GameEvents.onFlightReady.Add(OnFlightReady);
            GameEvents.onGameSceneLoadRequested.Add(OnSceneLoadRequested);
            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnShowUI);
        }

        private void OnDestroy()
        {
            GameEvents.onFlightReady.Remove(OnFlightReady);
            GameEvents.onGameSceneLoadRequested.Remove(OnSceneLoadRequested);
            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnShowUI);
            Deactivate();
            if (Instance == this) Instance = null;
        }

        private void OnFlightReady()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (GhostSpaceCenter.IsGhost(v))
            {
                StartCoroutine(ActivateWhenReady(v));
            }
            else if (v != null && !string.IsNullOrEmpty(GhostSpaceCenter.PendingCenterName))
            {
                OffworldScenario sc = OffworldScenario.Instance;
                OffworldCenter c = sc != null ? sc.GetByName(GhostSpaceCenter.PendingCenterName) : null;
                GhostSpaceCenter.PendingCenterName = "";
                if (c != null && c.IsAnchor(v)) ActivateVesselView(v, c);
                GhostSpaceCenter.RemoveAllGhosts(false);
            }
            else
            {
                GhostSpaceCenter.PendingCenterName = "";
                // a leftover anchor somewhere else in the save: get rid of it quietly
                GhostSpaceCenter.RemoveAllGhosts(false);
            }
        }

        private IEnumerator ActivateWhenReady(Vessel v)
        {
            // the anchor stays packed on purpose (see Vessel.GoOffRails patch); just let the scene settle a little
            yield return new WaitForSeconds(0.25f);
            if (v != null) Activate(v);
        }

        /// <summary>Space Center mode on a real vessel (station): same presentation, nothing is pinned or hidden.</summary>
        public void ActivateVesselView(Vessel v, OffworldCenter center)
        {
            if (v == null) return;
            ghost = v;
            anchorMode = false;
            Center = center;
            Active = true;
            showHud = true;
            SpaceCenterHud.Reset();
            uiHider.Apply();
            InputLockManager.SetControlLock(Locks, LockId);
            try
            {
                FlightCamera cam = FlightCamera.fetch;
                if (cam != null)
                {
                    cam.setModeImmediate(FlightCamera.Modes.FREE);
                    float size = Mathf.Max(30f, v.vesselSize.magnitude * 2.5f);
                    cam.maxDistance = Mathf.Max(cam.maxDistance, size * 4f);
                    cam.SetDistanceImmediate(size);
                }
            }
            catch (Exception e) { Log.Warn("Camera setup: " + e.Message); }
            if (OffworldWindow.Instance != null && OffworldSettings.AutoOpenPanel) OffworldWindow.Instance.ShowBasePanel();
            ScreenMessages.PostScreenMessage("Space Center: " + (center != null ? center.Name : v.vesselName), 4f, ScreenMessageStyle.UPPER_CENTER);
            Log.Info("Space Center view active on vessel " + v.vesselName);
        }

        private void Activate(Vessel v)
        {
            ghost = v;
            anchorMode = true;
            OffworldScenario sc = OffworldScenario.Instance;
            Center = sc != null ? sc.GetByName(GhostSpaceCenter.PendingCenterName) : null;
            if (Center == null && sc != null)
            {
                double d;
                Center = sc.Nearest(v.mainBody, v.latitude, v.longitude, out d);
            }
            GhostSpaceCenter.PendingCenterName = "";
            Active = true;

            PinAndHide(v);

            // Space Center feel: no flight UI, no ship controls, a free camera that can pull back far enough to see the base.
            showHud = true;
            SpaceCenterHud.Reset();
            uiHider.Apply();
            InputLockManager.SetControlLock(Locks, LockId);
            try
            {
                FlightCamera cam = FlightCamera.fetch;
                if (cam != null)
                {
                    cam.setModeImmediate(FlightCamera.Modes.FREE);
                    cam.maxDistance = Mathf.Max(cam.maxDistance, 6000f);
                    cam.SetDistanceImmediate(260f);
                }
            }
            catch (Exception e) { Log.Warn("Camera setup: " + e.Message); }

            FlightDriver.SetPause(false, false);
            if (OffworldWindow.Instance != null && OffworldSettings.AutoOpenPanel) OffworldWindow.Instance.ShowBasePanel();
            ScreenMessages.PostScreenMessage("Space Center: " + (Center != null ? Center.Name : v.mainBody.displayName), 4f, ScreenMessageStyle.UPPER_CENTER);
            Log.Info("Space Center view active at " + (Center != null ? Center.ToString() : "unknown center"));
        }

        private static void PinAndHide(Vessel v)
        {
            try
            {
                foreach (Renderer r in v.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                foreach (Part p in v.parts)
                {
                    p.PermanentGroundContact = true; // keeps Vessel.Landed true without a physical contact
                    foreach (Collider c in p.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                    Rigidbody rb = p.Rigidbody;
                    if (rb != null)
                    {
                        rb.isKinematic = true;
                        rb.detectCollisions = false;
                    }
                }
                v.Landed = true;
            }
            catch (Exception e) { Log.Warn("PinAndHide: " + e.Message); }
        }

        private void Update()
        {
            if (!Active || ghost == null) return;
            if (FlightGlobals.ActiveVessel != ghost)
            {
                Deactivate();
                return;
            }
            uiHider.Apply();
            // physics may re-create rigidbodies on unpack; keep the anchor pinned
            if (anchorMode && !ghost.packed)
            {
                foreach (Part p in ghost.parts)
                {
                    p.PermanentGroundContact = true;
                    Rigidbody rb = p.Rigidbody;
                    if (rb != null && !rb.isKinematic) { rb.isKinematic = true; rb.detectCollisions = false; }
                }
                ghost.Landed = true;
            }
        }

        // F2 toggles our overlay together with the stock UI, so a screenshot can be clean
        private void OnHideUI() { showHud = false; }
        private void OnShowUI() { showHud = true; }

        private void OnGUI()
        {
            if (!Active || !showHud) return;
            try { SpaceCenterHud.Draw(Center, ghost); }
            catch (Exception e) { Log.Warn("HUD: " + e.Message); }
        }

        private void OnVesselChange(Vessel v)
        {
            if (Active && v != ghost) Deactivate();
        }

        private void OnSceneLoadRequested(GameScenes scene)
        {
            if (Active) Deactivate();
        }

        /// <summary>Leave Space Center mode but stay in flight (e.g. to actually fly the station).</summary>
        public void ExitView()
        {
            Deactivate();
        }

        private void Deactivate()
        {
            if (!Active) return;
            Active = false;
            uiHider.Restore();
            InputLockManager.RemoveControlLock(LockId);
            ghost = null;
        }

        /// <summary>Leave the base view for the real KSC scene (used for facilities that only exist there).</summary>
        public static void GoToRealSpaceCenter(FacilityRole openFacility)
        {
            string reason;
            if (!SceneActions.CanLeaveFlight(out reason))
            {
                ScreenMessages.PostScreenMessage(reason, 4f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            SceneActions.PendingFacility = openFacility;
            GhostSpaceCenter.SuppressRedirectOnce = true;
            NativeSpaceCenter.StockOnce = true;
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            HighLogic.LoadScene(GameScenes.SPACECENTER);
        }
    }

    /// <summary>Removes leftover anchor vessels whenever a non-flight game scene loads.</summary>
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class GhostCleaner : MonoBehaviour
    {
        private void Start()
        {
            GameScenes s = HighLogic.LoadedScene;
            if (s != GameScenes.TRACKSTATION && s != GameScenes.SPACECENTER && s != GameScenes.EDITOR)
            {
                Destroy(this);
                return;
            }
            StartCoroutine(Clean());
        }

        private IEnumerator Clean()
        {
            // first pass right away, second pass once the scene has settled (vessel lists are rebuilt by then)
            GhostSpaceCenter.RemoveAllGhosts(false);
            yield return new WaitForSeconds(1f);
            GhostSpaceCenter.RemoveAllGhosts(true);
            Destroy(this);
        }
    }
}
