using System;
using UnityEngine;

namespace OffworldSpaceCenters
{
    /// <summary>
    /// The Space Center's "warp to next morning" button measures the local time at SpaceCenter.Instance (the KSC)
    /// and scales it with the home world's solar day. In an off-world Space Center scene both are wrong, so the
    /// time-to-daylight is computed here for the base itself, using that body's own solar day.
    /// </summary>
    public static class MorningPatches
    {
        /// <summary>Fraction of the local day the stock button aims for (the stock default; KK may change the static).</summary>
        private const double DawnFraction = 0.3d;

        // KSP.UI.UIWarpToNextMorning.Setup(ref double timeToDaylight) prefix
        public static bool Setup_Prefix(ref double timeToDaylight)
        {
            try
            {
                if (!NativeSpaceCenter.Active || HighLogic.LoadedScene != GameScenes.SPACECENTER) return true;
                CelestialBody body = NativeSpaceCenter.Body;
                OffworldCenter center = NativeSpaceCenter.Center;
                if (body == null || center == null || Sun.Instance == null) return true;

                double localTime = Sun.Instance.GetLocalTimeAtPosition(center.Latitude, center.Longitude, body);
                double day = SolarDay(body);
                if (double.IsNaN(localTime) || double.IsInfinity(localTime) || double.IsNaN(day) || double.IsInfinity(day) || day <= 0d) return true;
                timeToDaylight = UtilMath.WrapAround(DawnFraction - localTime, 0d, 1d) * day + 60d;
                return false;
            }
            catch (Exception e)
            {
                Log.Warn("Warp to morning: " + e.Message);
                return true;
            }
        }

        /// <summary>
        /// Length of a solar day: the sidereal rotation combined with the motion around the star (for a moon, its
        /// planet's motion around the star), 1 / (1/rotation - 1/year). Stock does the same for the home world.
        /// </summary>
        public static double SolarDay(CelestialBody body)
        {
            double rot = body.rotationPeriod;
            if (rot == 0d || double.IsNaN(rot)) return double.NaN;
            CelestialBody star = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
            CelestialBody b = body;
            int guard = 0;
            while (b != null && b.referenceBody != null && b.referenceBody != b && b.referenceBody != star && guard++ < 16)
            {
                b = b.referenceBody;
            }
            double year = (b != null && b.orbit != null && b.referenceBody != null && b.referenceBody != b) ? b.orbit.period : double.PositiveInfinity;
            double inv = 1d / rot - (double.IsInfinity(year) || year == 0d ? 0d : 1d / year);
            if (Math.Abs(inv) < 1e-12) return Math.Abs(rot);
            return Math.Abs(1d / inv);
        }
    }
}
