using System;
using System.Collections;
using UnityEngine;

namespace OffworldSpaceCenters
{
    /// <summary>
    /// Experimental: when a flagged base on the home world is selected as the "Space Center view",
    /// the Space Center scene camera is re-parented onto that base (same trick Kerbal Konstructs uses
    /// for its "focus last launch site" option). Bases on other bodies cannot be shown here because the
    /// Space Center scene only ever loads the home world; those use the flight-based base view instead.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class SpaceCenterRelocator : MonoBehaviour
    {
        public static bool Relocated { get; private set; }
        public static OffworldCenter RelocatedTo { get; private set; }

        private GameObject anchor;

        private void Start()
        {
            Relocated = false;
            RelocatedTo = null;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            // Give KK a moment to do its own camera handling first.
            yield return new WaitForSeconds(0.6f);
            try
            {
                Relocate();
            }
            catch (Exception e)
            {
                Log.Warn("Relocation failed: " + e);
            }
            if (SceneActions.PendingFacility != FacilityRole.None)
            {
                yield return new WaitForSeconds(0.8f);
                FacilityRole role = SceneActions.PendingFacility;
                SceneActions.PendingFacility = FacilityRole.None;
                SceneActions.FireFacilityEvent(role);
            }
        }

        private void Relocate()
        {
            OffworldScenario sc = OffworldScenario.Instance;
            if (sc == null) return;
            OffworldCenter center = sc.SelectedCenter;
            if (center == null) return;

            if (NativeSpaceCenter.Active && NativeSpaceCenter.Center != null && NativeSpaceCenter.Center.Name == center.Name)
            {
                FinishNative(center);
                return;
            }
            if (!OffworldSettings.RelocateCamera) return;
            if (!center.IsOnHomeWorld)
            {
                // Off-world centers either run through the native scene (handled above) or the anchor view in flight.
                return;
            }

            Transform target = null;
            if (center.IsKKSite) target = KKBridge.GetSiteTransform(center.KKSiteName);
            if (target == null) target = CreateAnchor(center);
            if (target == null)
            {
                Log.Warn("Could not find or create a camera anchor for " + center);
                return;
            }

            float rotation = center.IsKKSite ? KKBridge.GetSiteCameraRotation(center.KKSiteName) : 90f;
            int moved = 0;
            foreach (SpaceCenterCamera2 cam in Resources.FindObjectsOfTypeAll<SpaceCenterCamera2>())
            {
                try
                {
                    if (cam == null || !cam.gameObject.scene.IsValid()) continue;
                    float nomHeight = 45f - (float)center.Body.GetAltitude(target.position);
                    cam.transform.parent = target;
                    cam.transform.position = target.position;
                    cam.initialPositionTransformName = target.name;
                    cam.pqsName = center.Body.name;
                    cam.rotationInitial = rotation;
                    cam.altitudeInitial = nomHeight;
                    cam.ResetCamera();
                    moved++;
                }
                catch (Exception e)
                {
                    Log.Warn("Camera move failed: " + e.Message);
                }
            }

            if (moved > 0)
            {
                Relocated = true;
                RelocatedTo = center;
                KKBridge.MarkSpaceCenterCamAltered(true);
                KKBridge.RefreshStatics();
                Log.Info("Space Center camera relocated to " + center);
                ScreenMessages.PostScreenMessage("Space Center view: " + center.Name, 4f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private static readonly System.Reflection.FieldInfo camPqsField = HarmonyLib.AccessTools.Field(typeof(SpaceCenterCamera2), "pqs");
        private static readonly System.Reflection.FieldInfo camInitialPosField = HarmonyLib.AccessTools.Field(typeof(SpaceCenterCamera2), "initialPosition");

        /// <summary>
        /// The real Space Center scene is already running on the target body (NativeSpaceCenter). Re-assert the camera
        /// in case Kerbal Konstructs moved it back to the KSC flagpole and point KK at the new body so its statics load.
        /// </summary>
        private void FinishNative(OffworldCenter center)
        {
            Transform anchor = NativeSpaceCenter.Anchor;
            CelestialBody body = NativeSpaceCenter.Body;
            if (anchor == null || body == null) return;
            int fixedCams = 0;
            foreach (SpaceCenterCamera2 cam in Resources.FindObjectsOfTypeAll<SpaceCenterCamera2>())
            {
                try
                {
                    // FindObjectsOfTypeAll also returns prefab assets; only touch cameras that live in the scene
                    if (cam == null || !cam.gameObject.scene.IsValid()) continue;
                    NativeSpaceCenter.ApplyCameraSettings(cam);
                    if (camPqsField != null) camPqsField.SetValue(cam, body.pqsController);
                    if (camInitialPosField != null) camInitialPosField.SetValue(cam, anchor);
                    if (cam.transform.parent != anchor)
                    {
                        cam.transform.parent = anchor;
                        cam.transform.position = anchor.position;
                    }
                    cam.ResetCamera();
                    fixedCams++;
                }
                catch (Exception e) { Log.Warn("Native camera fix: " + e.Message); }
            }
            KKBridge.SwitchBody(body);
            Relocated = true;
            RelocatedTo = center;
            Log.Info("Native Space Center scene ready at " + center + " (" + fixedCams + " camera(s))");
            ScreenMessages.PostScreenMessage(center.Name + " Space Center", 4f, ScreenMessageStyle.UPPER_CENTER);
        }

        private Transform CreateAnchor(OffworldCenter center)
        {
            CelestialBody body = center.Body;
            if (body == null) return null;
            anchor = new GameObject("OffworldAnchor_" + center.Name.Replace(' ', '_'));
            Transform parent = body.pqsController != null ? body.pqsController.transform : body.transform;
            anchor.transform.parent = parent;
            anchor.transform.position = center.WorldPosition;
            Vector3 up = (anchor.transform.position - body.position).normalized;
            anchor.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
            return anchor.transform;
        }

        private void OnDestroy()
        {
            if (anchor != null) Destroy(anchor);
            Relocated = false;
            RelocatedTo = null;
        }
    }
}
