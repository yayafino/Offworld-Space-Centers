using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using KSP.UI.Screens;
using UnityEngine;

namespace OffworldSpaceCenters
{
    /// <summary>Applies the Harmony patches once, at the main menu. Each patch is applied separately so one failure does not take the rest down.</summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class OffworldPatcher : MonoBehaviour
    {
        private static bool patched;

        private void Start()
        {
            if (patched) return;
            patched = true;
            Harmony harmony = new Harmony("OffworldSpaceCenters");

            // 1. Vessel.IsRecoverable: used by the Tracking Station Recover button and by other mods.
            TryPatch(harmony, "Vessel.IsRecoverable",
                AccessTools.PropertyGetter(typeof(Vessel), "IsRecoverable"),
                null,
                AccessTools.Method(typeof(RecoveryPatches), "IsRecoverable_Postfix"),
                null);

            // 2. VesselRecovery.OnVesselRecovered: recovery value and dialog text.
            TryPatch(harmony, "VesselRecovery.OnVesselRecovered",
                AccessTools.Method(typeof(VesselRecovery), "OnVesselRecovered", new Type[] { typeof(ProtoVessel), typeof(bool) }),
                AccessTools.Method(typeof(RecoveryPatches), "OnVesselRecovered_Prefix"),
                null,
                AccessTools.Method(typeof(RecoveryPatches), "OnVesselRecovered_Finalizer"));

            // 3. Space Center button → selected off-world base (opt-in via settings, on by default)
            TryPatch(harmony, "HighLogic.LoadScene",
                AccessTools.Method(typeof(HighLogic), "LoadScene", new Type[] { typeof(GameScenes) }),
                AccessTools.Method(typeof(ScenePatches), "LoadScene_Prefix"),
                null,
                null);

            // 4. The Space Center anchor vessel never leaves rails: no physics, zero surface speed, always landed.
            TryPatch(harmony, "Vessel.GoOffRails",
                AccessTools.Method(typeof(Vessel), "GoOffRails"),
                AccessTools.Method(typeof(ScenePatches), "GoOffRails_Prefix"),
                null,
                null);

            // 5. Real Space Center scene on other bodies (PSystemSetup redirection)
            TryPatch(harmony, "PSystemSetup.SetSpaceCentre",
                AccessTools.Method(typeof(PSystemSetup), "SetSpaceCentre"),
                AccessTools.Method(typeof(NativeSpaceCenter), "SetSpaceCentre_Prefix"), null, null);
            TryPatch(harmony, "PSystemSetup.OnLevelLoaded",
                AccessTools.Method(typeof(PSystemSetup), "OnLevelLoaded", new Type[] { typeof(GameScenes) }),
                AccessTools.Method(typeof(NativeSpaceCenter), "OnLevelLoaded_Prefix"), null, null);
            TryPatch(harmony, "SpaceCenterCamera2.Start",
                AccessTools.Method(typeof(SpaceCenterCamera2), "Start"),
                AccessTools.Method(typeof(NativeSpaceCenter), "CameraStart_Prefix"), null, null);
            // every scene change starts from clean home-world references (and latches the one-shot stock KSC request)
            try
            {
                GameEvents.onGameSceneLoadRequested.Add(NativeSpaceCenter.OnSceneLoadRequested);
                GameEvents.onGameStateLoad.Add(NativeSpaceCenter.OnGameStateLoad);
            }
            catch (Exception e)
            {
                Log.Error("Could not subscribe to scene/game load events: " + e.Message);
            }

            // 5b. Vessel markers of the Space Center scene: at an off-world base, show the vessels parked there.
            TryPatch(harmony, "KSCVesselMarkers.SpawnVesselMarkers",
                AccessTools.Method(typeof(KSCVesselMarkers), "SpawnVesselMarkers"),
                AccessTools.Method(typeof(MarkerPatches), "SpawnVesselMarkers_Prefix"), null, null);

            // 5c. "Warp to next morning" measured at the base with that body's solar day.
            TryPatch(harmony, "UIWarpToNextMorning.Setup",
                AccessTools.Method(typeof(KSP.UI.UIWarpToNextMorning), "Setup", new Type[] { typeof(double).MakeByRefType() }),
                AccessTools.Method(typeof(MorningPatches), "Setup_Prefix"), null, null);

            // 6. Altimeter slider: the flight Recover button has its own isHomeWorld check.
            TryPatch(harmony, "AltimeterSliderButtons.UpdateForVessel",
                AccessTools.Method(typeof(AltimeterSliderButtons), "UpdateForVessel", new Type[] { typeof(Vessel) }),
                null,
                AccessTools.Method(typeof(AltimeterPatches), "UpdateForVessel_Postfix"),
                null);
        }

        private static void TryPatch(Harmony harmony, string label, MethodBase target, MethodInfo prefix, MethodInfo postfix, MethodInfo finalizer)
        {
            try
            {
                if (target == null)
                {
                    Log.Error("Patch target not found: " + label + ". That feature will be unavailable.");
                    return;
                }
                harmony.Patch(target,
                    prefix != null ? new HarmonyMethod(prefix) : null,
                    postfix != null ? new HarmonyMethod(postfix) : null,
                    null,
                    finalizer != null ? new HarmonyMethod(finalizer) : null);
                Log.Info("Patched " + label);
            }
            catch (Exception e)
            {
                Log.Error("Failed to patch " + label + ": " + e);
            }
        }
    }

    public static class ScenePatches
    {
        // Vessel.GoOffRails(): skip entirely for the invisible anchor
        public static bool GoOffRails_Prefix(Vessel __instance)
        {
            try
            {
                return !GhostSpaceCenter.IsGhost(__instance);
            }
            catch
            {
                return true;
            }
        }

        // HighLogic.LoadScene(GameScenes scene)
        public static bool LoadScene_Prefix(GameScenes scene)
        {
            if (scene != GameScenes.SPACECENTER) return true;
            try
            {
                OffworldCenter center;
                if (GhostSpaceCenter.ShouldRedirect(out center))
                {
                    Log.Info("Redirecting Space Center to " + center);
                    if (GhostSpaceCenter.Enter(center)) return false;
                }
            }
            catch (Exception e)
            {
                Log.Warn("LoadScene prefix: " + e.Message);
            }
            return true;
        }
    }

    public static class RecoveryPatches
    {
        public class RecoveryState
        {
            public ProtoVessel pv;
            public string landedAt;
            public string displayLandedAt;
            public SpaceCenter previousSpaceCenter;
            public bool swapped;
        }

        // Vessel.IsRecoverable getter
        public static void IsRecoverable_Postfix(Vessel __instance, ref bool __result)
        {
            if (__result) return;
            try
            {
                OffworldScenario sc = OffworldScenario.Instance;
                if (sc != null && sc.IsRecoverableAtCenter(__instance)) __result = true;
            }
            catch (Exception e)
            {
                Log.Warn("IsRecoverable postfix: " + e.Message);
            }
        }

        // VesselRecovery.OnVesselRecovered(ProtoVessel pv, bool quick)
        //
        // Stock computes the value factor from the distance to SpaceCenter.Instance, then overrides it to 100%
        // when pv.landedAt names a launch pad. We make the proto vessel look like it was recovered from a pad
        // and point the recovery location text at our base, then put everything back in the finalizer.
        public static void OnVesselRecovered_Prefix(ProtoVessel pv, out RecoveryState __state)
        {
            __state = null;
            try
            {
                OffworldScenario sc = OffworldScenario.Instance;
                if (sc == null || pv == null) return;
                OffworldCenter center;
                if (!sc.IsRecoverableAtCenter(pv, out center)) return;

                RecoveryState st = new RecoveryState();
                st.pv = pv;
                st.landedAt = pv.landedAt;
                st.displayLandedAt = pv.displaylandedAt;
                st.previousSpaceCenter = SpaceCenter.Instance;

                if (OffworldSettings.FullValue)
                {
                    // Any launch site name gives 100%; "LaunchPad" is matched by stock without touching PSystemSetup.
                    pv.landedAt = "LaunchPad";
                }
                pv.displaylandedAt = center.Name;
                st.swapped = true;
                __state = st;
                Log.Info("Recovering '" + pv.vesselName + "' at flagged base " + center + (OffworldSettings.FullValue ? " (full value)" : ""));
            }
            catch (Exception e)
            {
                Log.Warn("OnVesselRecovered prefix: " + e.Message);
                __state = null;
            }
        }

        public static Exception OnVesselRecovered_Finalizer(Exception __exception, RecoveryState __state)
        {
            try
            {
                if (__state != null && __state.swapped && __state.pv != null)
                {
                    __state.pv.landedAt = __state.landedAt;
                    __state.pv.displaylandedAt = __state.displayLandedAt;
                    if (__state.previousSpaceCenter != null) SpaceCenter.Instance = __state.previousSpaceCenter;
                }
            }
            catch (Exception e)
            {
                Log.Warn("OnVesselRecovered finalizer: " + e.Message);
            }
            return __exception;
        }
    }

    /// <summary>
    /// Mirrors the stock altimeter logic for vessels landed inside a flagged base on a non-home body:
    /// Recover + Space Center buttons when stopped, Space Center only while moving.
    /// </summary>
    public static class AltimeterPatches
    {
        private static readonly FieldInfo recoverCoroutineField = AccessTools.Field(typeof(AltimeterSliderButtons), "recoverCoroutine");
        private static readonly FieldInfo returnToKSCCoroutineField = AccessTools.Field(typeof(AltimeterSliderButtons), "returnToKSCCoroutine");
        private static readonly FieldInfo stateField = AccessTools.Field(typeof(AltimeterSliderButtons), "state");
        private static readonly FieldInfo ledField = AccessTools.Field(typeof(AltimeterSliderButtons), "led");
        private static readonly MethodInfo setUnlockMethod = AccessTools.Method(typeof(AltimeterSliderButtons), "setUnlock", new Type[] { typeof(int) });
        private static readonly MethodInfo updateForVesselMethod = AccessTools.Method(typeof(AltimeterSliderButtons), "UpdateForVessel", new Type[] { typeof(Vessel) });

        private const int StateLocked = 0;
        private const int StateSpaceCenterOnly = 1;
        private const int StateRecoverAndSpaceCenter = 2;

        public static bool ReflectionOk
        {
            get { return recoverCoroutineField != null && returnToKSCCoroutineField != null && stateField != null && setUnlockMethod != null; }
        }

        public static void UpdateForVessel_Postfix(AltimeterSliderButtons __instance, Vessel v)
        {
            try
            {
                if (!ReflectionOk) return;
                if (v == null || v != FlightGlobals.ActiveVessel) return;
                if (v.LandedOrSplashed && v.mainBody != null && v.mainBody.isHomeWorld) return; // stock already handles this
                OffworldScenario sc = OffworldScenario.Instance;
                if (sc == null || !sc.IsRecoverableAtCenter(v)) return;

                StopStockCoroutines(__instance);
                Coroutine mine = __instance.StartCoroutine(UnlockAtBase(__instance, v));
                recoverCoroutineField.SetValue(__instance, mine);
            }
            catch (Exception e)
            {
                Log.Warn("Altimeter postfix: " + e.Message);
            }
        }

        private static void StopStockCoroutines(AltimeterSliderButtons asb)
        {
            Coroutine c = recoverCoroutineField.GetValue(asb) as Coroutine;
            if (c != null) asb.StopCoroutine(c);
            c = returnToKSCCoroutineField.GetValue(asb) as Coroutine;
            if (c != null) asb.StopCoroutine(c);
        }

        private static void SetUnlock(AltimeterSliderButtons asb, int state)
        {
            setUnlockMethod.Invoke(asb, new object[] { state });
        }

        private static IEnumerator UnlockAtBase(AltimeterSliderButtons asb, Vessel v)
        {
            yield return null;
            OffworldScenario sc = OffworldScenario.Instance;
            OffworldCenter center;
            while (asb != null && v != null && v == FlightGlobals.ActiveVessel && sc != null && sc.IsRecoverableAtCenter(v, out center))
            {
                int state = (int)stateField.GetValue(asb);
                if (center.IsStopped(v) && !FlightDriver.Pause)
                {
                    if (state != StateRecoverAndSpaceCenter) SetUnlock(asb, StateRecoverAndSpaceCenter);
                }
                else if (state != StateSpaceCenterOnly)
                {
                    SetUnlock(asb, StateSpaceCenterOnly);
                }
                yield return new WaitForSeconds(0.25f);
            }
            if (asb == null) yield break;
            SetUnlock(asb, StateLocked);
            LED led = ledField != null ? ledField.GetValue(asb) as LED : null;
            if (led != null) led.setOff();
            recoverCoroutineField.SetValue(asb, null);
            // Hand control back to stock so it can offer "Return to Space Center" etc.
            if (v != null && v == FlightGlobals.ActiveVessel && updateForVesselMethod != null)
            {
                updateForVesselMethod.Invoke(asb, new object[] { v });
            }
        }

        /// <summary>Called by the flight monitor when a landed vessel drives into or out of a base radius.</summary>
        public static void Refresh(Vessel v)
        {
            if (v == null || updateForVesselMethod == null) return;
            AltimeterSliderButtons asb = UnityEngine.Object.FindObjectOfType<AltimeterSliderButtons>();
            if (asb == null) return;
            try { updateForVesselMethod.Invoke(asb, new object[] { v }); }
            catch (Exception e) { Log.Warn("Altimeter refresh: " + e.Message); }
        }
    }
}
