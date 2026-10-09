#!/bin/bash
# Builds KolonySpaceCenters.dll with the Mono C# compiler (mcs) against a KSP install.
# Usage: KSPDIR="/path/to/Kerbal Space Program" ./build.sh
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
KSPDIR="${KSPDIR:-D:/steaminstall/steamapps/common/Kerbal Space Program}"
M="$KSPDIR/KSP_x64_Data/Managed"
OUT="$HERE/../GameData/KolonySpaceCenters/Plugins"
mkdir -p "$OUT"
MCS="${MCS:-mcs}"
"$MCS" -target:library -optimize+ -debug- -nowarn:1591,0618,0109 -langversion:7 \
  -nostdlib -noconfig \
  -r:"$M/mscorlib.dll" -r:"$M/System.dll" -r:"$M/System.Core.dll" \
  -r:"$M/Assembly-CSharp.dll" -r:"$M/Assembly-CSharp-firstpass.dll" \
  -r:"$M/UnityEngine.dll" -r:"$M/UnityEngine.CoreModule.dll" -r:"$M/UnityEngine.IMGUIModule.dll" \
  -r:"$M/UnityEngine.UI.dll" -r:"$M/UnityEngine.PhysicsModule.dll" -r:"$M/UnityEngine.TextRenderingModule.dll" \
  -r:"$M/UnityEngine.ImageConversionModule.dll" -r:"$M/UnityEngine.InputLegacyModule.dll" -r:"$M/UnityEngine.AnimationModule.dll" \
  -r:"$KSPDIR/GameData/000_Harmony/0Harmony.dll" \
  -r:"$KSPDIR/GameData/KerbalKonstructs/KerbalKonstructs.dll" \
  -out:"$OUT/KolonySpaceCenters.dll" "$HERE"/*.cs
echo "Built $OUT/KolonySpaceCenters.dll"
