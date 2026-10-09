using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KolonySpaceCenters
{
    /// <summary>
    /// Runs the real Space Center scene at a base on another body.
    ///
    /// Stock's PSystemSetup.SetSpaceCentre() is generic: it force-activates the PQS referenced by its private
    /// field 'cb', makes that body the rotating reference frame ('currentMainBody', inverse rotation, reversed
    /// orbit driver), activates the PQS in 'pqs' and puts the floating origin at 'scTransform'. Kerbin only ends up
    /// there because that is what system setup stored. Before the scene initialises we point those three fields at
    /// the target body and at an anchor PQSCity we create at the base (the same component stock uses for the KSC
    /// and KK uses for its group centers), let stock do its normal setup, and put the fields back as soon as any
    /// other scene starts initialising. The Space Center camera finds its PQS by name, so its two name fields are
    /// redirected too.
    /// </summary>
    public static class NativeSpaceCenter
    {
        public static bool Active { get; private set; }
        public static KolonyCenter Center { get; private set; }
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
        public static bool Handles(KolonyCenter c)
        {
            if (c == null || !KolonySettings.NativeOffworldScene || !ReflectionOk) return false;
            if (c.IsVessel || c.IsOnHomeWorld) return false;
            CelestialBody b = c.Body;
            return b != null && b.pqsController != null;
        }

        /// <summary>
        /// Snapshot of the selected center. PSystemSetup initialises the Space Center scene before the scenario
        /// modules of that scene exist, so the live scenario cannot be consulted at that moment.
        /// </summary>
        private static KolonyCenter remembered;

        public static void RememberSelection(KolonyCenter c)
        {
            remembered = c;
        }

        private static KolonyCenter Target()
        {
            KolonyScenario sc = KolonyScenario.Instance;
            KolonyCenter c = sc != null ? sc.SelectedCenter : remembered;
            if (c == null) c = remembered;
            return Handles(c) ? c : null;
        }

        /// <summary>Swap PSystemSetup's references to the target body. Idempotent; safe to call from several hooks.</summary>
        public static void Prepare(PSystemSetup setup)
        {
            if (fieldsSwapped) return;
            KolonyCenter c = Target();
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
            try
            {
                if (fieldsSwapped && setup != null)
                {
                    fCb.SetValue(setup, savedCb);
                    fPqs.SetValue(setup, savedPqs);
                    fSc.SetValue(setup, savedSc);
                }
                if (targetCb != null) targetCb.forceActivate = false;
                if (Body != null)
                {
                    Body.inverseRotation = false;
                    if (Body.orbitDriver != null) Body.orbitDriver.reverse = false;
                }
                if (Active) Log.Info("Space Center scene references restored to the home world.");
            }
            catch (Exception e)
            {
                Log.Warn("Native Space Center restore: " + e.Message);
            }
            fieldsSwapped = false;
            Active = false;
            Center = null;
            Anchor = null;
            Body = null;
            targetCb = null;
            savedCb = savedPqs = savedSc = null;
        }

        /// <summary>A PQSCity on the target PQS, sitting on the terrain at the base. Reused across scene loads.</summary>
        private static Transform GetOrCreateAnchor(KolonyCenter c, CelestialBody body, PQS pqs)
        {
            string name = "KolonySC_" + Sanitize(c.Name);
            Transform existing = pqs.transform.Find(name);
            PQSCity city;
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
                city = go.GetComponent<PQSCity>();
                if (city == null) city = go.AddComponent<PQSCity>();
            }
            else
            {
                go = new GameObject(name);
                go.transform.SetParent(pqs.transform, false);
                city = go.AddComponent<PQSCity>();
            }

            PQSCity.LODRange range = new PQSCity.LODRange();
            range.renderers = new GameObject[0];
            range.objects = new GameObject[0];
            range.visibleRange = 25000f;
            city.lod = new PQSCity.LODRange[] { range };
            city.frameDelta = 10000f;
            city.repositionRadial = (Vector3)(body.GetRelSurfaceNVector(c.Latitude, c.Longitude).normalized * pqs.radius);
            city.repositionRadiusOffset = 0d;
            city.repositionToSphere = false;
            city.repositionToSphereSurface = true;
            city.repositionToSphereSurfaceAddHeight = true;
            city.reorientToSphere = true;
            city.reorientInitialUp = Vector3.up;
            city.reorientFinalAngle = c.IsKKSite ? KKBridge.GetSiteCameraRotation(c.KKSiteName) : 0f;
            city.sphere = pqs;
            city.order = 100;
            city.modEnabled = true;
            try
            {
                city.OnSetup();
                city.Orientate(true);
            }
            catch (Exception e)
            {
                Log.Warn("Anchor PQSCity orientate: " + e.Message);
            }
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

        // ------------------------------------------------------------------ Harmony entry points

        // PSystemSetup.SetSpaceCentre() prefix
        public static void SetSpaceCentre_Prefix(PSystemSetup __instance)
        {
            try { Prepare(__instance); }
            catch (Exception e) { Log.Warn("SetSpaceCentre prefix: " + e.Message); }
        }

        // PSystemSetup.OnLevelLoaded(GameScenes level) prefix: any non-Space-Center scene puts things back first
        public static void OnLevelLoaded_Prefix(PSystemSetup __instance, GameScenes level)
        {
            try
            {
                if (level == GameScenes.SPACECENTER)
                {
                    Prepare(__instance);
                    return;
                }
                if (fieldsSwapped || Active) Restore(__instance);
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
            cam.pqsName = Body.pqsController.gameObject.name;
            cam.initialPositionTransformName = Anchor.name;
            cam.rotationInitial = Center != null && Center.IsKKSite ? KKBridge.GetSiteCameraRotation(Center.KKSiteName) : 90f;
            // same convention KK uses: the pivot ends up ~45 m above the anchor regardless of terrain height
            cam.altitudeInitial = 45f - (float)Body.GetAltitude(Anchor.position);
        }

        /// <summary>Fraction-of-day dawn estimate for the stock "warp to next morning" button (KK's formula).</summary>
        public static void TuneNextMorning()
        {
            if (Body == null || Center == null) return;
            try
            {
                double timeOfDawn = ((0.95d * (0.25d - Body.initialRotation / 360d) - (Center.Longitude / 360d) + 1d) % 1d);
                KSP.UI.UIWarpToNextMorning.timeOfDawn = timeOfDawn + 0.05d;
            }
            catch (Exception e) { Log.Warn("Next morning tune: " + e.Message); }
        }
    }
}
