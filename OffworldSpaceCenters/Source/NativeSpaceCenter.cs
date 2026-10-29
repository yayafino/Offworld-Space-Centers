using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace OffworldSpaceCenters
{
    /// <summary>
    /// Runs the real Space Center scene at a base on another body.
    ///
    /// Stock's PSystemSetup.SetSpaceCentre() is generic: it force-activates the PQS referenced by its private
    /// field 'cb', makes that body the rotating reference frame ('currentMainBody', inverse rotation, reversed
    /// orbit driver), activates the PQS in 'pqs' and puts the floating origin at 'scTransform'. Kerbin only ends up
    /// there because that is what system setup stored. Before the scene initialises we point those three fields at
    /// the target body and at an anchor transform we place at the base (a plain child of the body's PQS, exactly
    /// like the stock "SpaceCenterCamera" pivot under Kerbin's PQS), let stock do its normal setup, and put the
    /// fields back as soon as the next scene load is requested. The Space Center camera finds its PQS and pivot by
    /// name, so its two name fields are redirected too.
    ///
    /// The anchor must not be a PQSCity: PQSCity deactivates all of its child transforms every time the sphere
    /// resets and only re-enables them once a quad update completes. The stock camera nests the whole camera rig
    /// (SpaceCenterCamera2, CameraTransform, FlightCamera) under the pivot, so a PQSCity pivot switches the main
    /// camera off for a while after every reset. That is what broke Scatterer, the vessel markers and Camera.main.
    /// </summary>
    public static class NativeSpaceCenter
    {
        public static bool Active { get; private set; }
        public static OffworldCenter Center { get; private set; }
        public static Transform Anchor { get; private set; }
        public static CelestialBody Body { get; private set; }

        private static readonly FieldInfo fCb = AccessTools.Field(typeof(PSystemSetup), "cb");
        private static readonly FieldInfo fPqs = AccessTools.Field(typeof(PSystemSetup), "pqs");
        private static readonly FieldInfo fSc = AccessTools.Field(typeof(PSystemSetup), "scTransform");

        private static object savedCb, savedPqs, savedSc;
        private static PQSMod_CelestialBodyTransform targetCb;
        private static bool fieldsSwapped;

        public static bool ReflectionOk { get { return fCb != null && fPqs != null && fSc != null; } }

        /// <summary>True when this center will be shown through the real Space Center scene.</summary>
        public static bool Handles(OffworldCenter c)
        {
            if (c == null || !OffworldSettings.NativeOffworldScene || !ReflectionOk) return false;
            if (c.IsVessel || c.IsOnHomeWorld) return false;
            CelestialBody b = c.Body;
            return b != null && b.pqsController != null;
        }

        /// <summary>
        /// Snapshot of the selected center. PSystemSetup initialises the Space Center scene before the scenario
        /// modules of that scene exist, so the live scenario cannot be consulted at that moment.
        /// </summary>
        private static OffworldCenter remembered;

        public static void RememberSelection(OffworldCenter c)
        {
            remembered = c;
            rememberedIsAuthoritative = true;
        }

        /// <summary>
        /// Set right before a LoadScene(SPACECENTER) that must open the stock KSC even though a base is selected
        /// ("Go to stock KSC" buttons). Consumed by the load request that follows and applies to that whole load.
        /// </summary>
        public static bool StockOnce;
        private static bool stockThisLoad;

        /// <summary>
        /// Set right before a LoadScene(SPACECENTER) issued by the mod's own "open this base" buttons. Those work
        /// even when "Space Center button opens selected base" is off; stock Space Center buttons then stay stock.
        /// </summary>
        public static bool BaseOnce;
        private static bool baseThisLoad;

        private static OffworldCenter Target()
        {
            if (stockThisLoad) return null;
            if (!baseThisLoad && !OffworldSettings.RedirectSpaceCenter) return null;
            OffworldScenario sc = OffworldScenario.Instance;
            OffworldCenter c;
            if (sc != null) c = sc.SelectedCenter;
            else
            {
                c = remembered;
                if (c == null && !rememberedIsAuthoritative) c = FromSavedGame();
            }
            return Handles(c) ? c : null;
        }

        /// <summary>
        /// True once the running game has told us its selection (scenario loaded or changed). Cleared whenever a
        /// game state is loaded (resume, quickload), so the saved node is consulted instead of a stale snapshot.
        /// </summary>
        private static bool rememberedIsAuthoritative;

        // GameEvents.onGameStateLoad: a save was just read into HighLogic.CurrentGame
        public static void OnGameStateLoad(ConfigNode node)
        {
            remembered = null;
            rememberedIsAuthoritative = false;
        }

        /// <summary>
        /// The selection as stored in the game that is about to run. The Space Center scene is initialised before
        /// its scenario modules exist, so after "Resume" or a quickload this is the only source available.
        /// </summary>
        private static OffworldCenter FromSavedGame()
        {
            try
            {
                Game game = HighLogic.CurrentGame;
                if (game == null || game.scenarios == null) return null;
                string wanted = typeof(OffworldScenario).Name;
                ConfigNode node = null;
                ConfigNode legacy = null;
                for (int i = 0; i < game.scenarios.Count; i++)
                {
                    ProtoScenarioModule psm = game.scenarios[i];
                    if (psm == null) continue;
                    if (psm.moduleName == wanted) node = psm.GetData();
                    else if (psm.moduleName == OffworldScenario.LegacyScenarioName) legacy = psm.GetData();
                }
                // a save written by the mod under its previous name has not been migrated yet
                if (node == null || !node.HasValue("migrated")) node = legacy ?? node;
                if (node == null) return null;
                string selected = node.GetValue("selectedSpaceCenter");
                if (string.IsNullOrEmpty(selected)) return null;
                foreach (ConfigNode n in node.GetNodes("CENTER"))
                {
                    OffworldCenter c = OffworldCenter.Load(n);
                    if (c != null && c.Name == selected) return c;
                }
                return null;
            }
            catch (Exception e)
            {
                Log.Warn("Reading the saved Space Center selection failed: " + e.Message);
            }
            return null;
        }

        /// <summary>Swap PSystemSetup's references to the target body. Idempotent; safe to call from several hooks.</summary>
        public static void Prepare(PSystemSetup setup)
        {
            if (fieldsSwapped) return;
            OffworldCenter c = Target();
            if (c == null || setup == null) return;
            try
            {
                CelestialBody body = c.Body;
                PQS pqs = body.pqsController;
                PQSMod_CelestialBodyTransform cbMod = null;
                foreach (PQSMod_CelestialBodyTransform m in pqs.GetComponentsInChildren<PQSMod_CelestialBodyTransform>(true))
                {
                    if (m.sphere == pqs || cbMod == null) cbMod = m;
                }
                if (cbMod == null)
                {
                    Log.Warn("No PQSMod_CelestialBodyTransform on " + body.name + "; cannot use the native Space Center scene there.");
                    return;
                }
                Transform anchor = GetOrCreateAnchor(c, body, pqs);
                if (anchor == null) return;

                savedCb = fCb.GetValue(setup);
                savedPqs = fPqs.GetValue(setup);
                savedSc = fSc.GetValue(setup);
                fCb.SetValue(setup, cbMod);
                fPqs.SetValue(setup, pqs);
                fSc.SetValue(setup, anchor);

                targetCb = cbMod;
                Body = body;
                Anchor = anchor;
                Center = c;
                Active = true;
                fieldsSwapped = true;
                Log.Info("Space Center scene redirected to " + c + " (PQS " + pqs.gameObject.name + ")");
            }
            catch (Exception e)
            {
                Log.Error("Native Space Center prepare failed: " + e);
                Restore(setup);
            }
        }

        /// <summary>Put PSystemSetup back on the home world and release the target body from Space Center duty.</summary>
        public static void Restore(PSystemSetup setup)
        {
            bool restored = !fieldsSwapped;
            try
            {
                if (fieldsSwapped)
                {
                    if (setup == null) setup = PSystemSetup.Instance;
                    if (setup != null)
                    {
                        fCb.SetValue(setup, savedCb);
                        fPqs.SetValue(setup, savedPqs);
                        fSc.SetValue(setup, savedSc);
                        restored = true;
                    }
                    else
                    {
                        Log.Warn("PSystemSetup is not available; its references stay redirected until it is back.");
                    }
                }
                if (targetCb != null) targetCb.forceActivate = false;
                if (Body != null)
                {
                    Body.inverseRotation = false;
                    if (Body.orbitDriver != null) Body.orbitDriver.reverse = false;
                }
                if (Active && restored) Log.Info("Space Center scene references restored to the home world.");
            }
            catch (Exception e)
            {
                Log.Warn("Native Space Center restore: " + e.Message);
            }
            if (restored)
            {
                fieldsSwapped = false;
                savedCb = savedPqs = savedSc = null;
            }
            Active = false;
            Center = null;
            Anchor = null;
            Body = null;
            targetCb = null;
        }

        /// <summary>
        /// A plain transform under the target PQS, sitting on the terrain at the base and oriented with +Y up,
        /// positioned the same way PQSCity.Orientate does it (repositionToSphereSurface + reorientToSphere).
        /// Reused across scene loads when it survives; the stock camera's SurfaceObject pops it to the scene root,
        /// in which case it dies with the scene and is simply recreated here.
        /// </summary>
        private static Transform GetOrCreateAnchor(OffworldCenter c, CelestialBody body, PQS pqs)
        {
            string name = "OffworldSC_" + Sanitize(c.Name);
            Transform existing = pqs.transform.Find(name);
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
                // the previous scene's camera attached a SurfaceObject to keep the anchor on the surface; the new
                // camera adds its own, so do not leave two of them steering the same transform
                foreach (SurfaceObject so in go.GetComponents<SurfaceObject>()) UnityEngine.Object.DestroyImmediate(so);
            }
            else
            {
                go = new GameObject(name);
                go.transform.SetParent(pqs.transform, false);
            }

            Vector3d radial = body.GetRelSurfaceNVector(c.Latitude, c.Longitude).normalized;
            double height = pqs.radius;
            try
            {
                double h = pqs.GetSurfaceHeight(radial);
                if (!double.IsNaN(h) && !double.IsInfinity(h) && h > 0d) height = h;
            }
            catch (Exception e)
            {
                Log.Warn("Anchor terrain height unavailable, using the sphere radius: " + e.Message);
            }
            go.transform.localPosition = (Vector3)(radial * height);
            float yaw = c.IsKKSite ? KKBridge.GetSiteCameraRotation(c.KKSiteName) : 0f;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, go.transform.localPosition.normalized) * Quaternion.AngleAxis(yaw, Vector3.up);
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        private static string Sanitize(string s)
        {
            char[] buf = s.ToCharArray();
            for (int i = 0; i < buf.Length; i++)
            {
                if (!char.IsLetterOrDigit(buf[i])) buf[i] = '_';
            }
            return new string(buf);
        }

        // ------------------------------------------------------------------ Harmony entry points / events

        // GameEvents.onGameSceneLoadRequested: fired by HighLogic.LoadScene before the old scene is torn down.
        // Every scene change starts from clean home-world references; the Space Center load then re-applies
        // the redirect in OnLevelLoaded. This is also where the one-shot "stock KSC" request is latched.
        public static void OnSceneLoadRequested(GameScenes scene)
        {
            try
            {
                stockThisLoad = scene == GameScenes.SPACECENTER && StockOnce;
                baseThisLoad = scene == GameScenes.SPACECENTER && BaseOnce;
                StockOnce = false;
                BaseOnce = false;
                if (fieldsSwapped || Active) Restore(PSystemSetup.Instance);
            }
            catch (Exception e) { Log.Warn("Scene load request: " + e.Message); }
        }

        // PSystemSetup.SetSpaceCentre() prefix
        public static void SetSpaceCentre_Prefix(PSystemSetup __instance)
        {
            try { Prepare(__instance); }
            catch (Exception e) { Log.Warn("SetSpaceCentre prefix: " + e.Message); }
        }

        // PSystemSetup.OnLevelLoaded(GameScenes level) prefix
        public static void OnLevelLoaded_Prefix(PSystemSetup __instance, GameScenes level)
        {
            try
            {
                if (level == GameScenes.SPACECENTER)
                {
                    // a Space Center to Space Center reload must not keep the previous redirect
                    if (fieldsSwapped || Active) Restore(__instance);
                    Prepare(__instance);
                    return;
                }
                if (fieldsSwapped || Active) Restore(__instance);
                if (level == GameScenes.MAINMENU)
                {
                    // another save may be loaded next; never carry a selection across games
                    remembered = null;
                    rememberedIsAuthoritative = false;
                    stockThisLoad = false;
                    StockOnce = false;
                    baseThisLoad = false;
                    BaseOnce = false;
                }
            }
            catch (Exception e) { Log.Warn("OnLevelLoaded prefix: " + e.Message); }
        }

        // SpaceCenterCamera2.Start() prefix: the camera resolves its PQS and pivot by name
        public static void CameraStart_Prefix(SpaceCenterCamera2 __instance)
        {
            try
            {
                if (!Active && PSystemSetup.Instance != null) Prepare(PSystemSetup.Instance);
                if (!Active || Anchor == null || Body == null) return;
                ApplyCameraSettings(__instance);
            }
            catch (Exception e) { Log.Warn("Camera Start prefix: " + e.Message); }
        }

        public static void ApplyCameraSettings(SpaceCenterCamera2 cam)
        {
            if (cam == null || Body == null || Anchor == null || Body.pqsController == null) return;
            cam.pqsName = Body.pqsController.gameObject.name;
            cam.initialPositionTransformName = Anchor.name;
            cam.rotationInitial = Center != null && Center.IsKKSite ? KKBridge.GetSiteCameraRotation(Center.KKSiteName) : 90f;
            // The camera adds the terrain height at the pivot to 'altitude' and offsets from the anchor along its up
            // axis, so this puts the pivot ~45 m above the ground whatever the anchor's own height is (KK's convention).
            cam.altitudeInitial = 45f - (float)Body.GetAltitude(Anchor.position);
        }
    }
}
