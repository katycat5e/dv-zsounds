using HarmonyLib;
using UnityEngine;

namespace DvMod.ZSounds
{
    public static class DieselAudio
    {
        private static float originalMinPitch;
        private static float originalMaxPitch;
        private static AudioClip? originalEngineOnClip;
        private static AudioClip? originalEngineOffClip;

        public static void SetEngineClip(LayeredAudio engineAudio, string? name, float startPitch)
        {
            LayeredAudioUtils.SetClip(engineAudio, name, startPitch);
            if (name == null)
            {
                engineAudio.minPitch = originalMinPitch;
                engineAudio.maxPitch = originalMaxPitch;
            }
            else
            {
                engineAudio.minPitch = 275f / 575f;
                engineAudio.maxPitch = 825f / 575f;
            }
        }

        public static void ResetAudio(LocoAudioDiesel __instance)
        {
            var engineAudio = __instance.engineAudio;
            if (originalMinPitch == default)
            {
                originalMinPitch = engineAudio.minPitch;
                originalMaxPitch = engineAudio.maxPitch;
                originalEngineOnClip = __instance.engineOnClip;
                originalEngineOffClip = __instance.engineOffClip;
            }

            if (Main.settings.dieselStartupSound != null)
                __instance.engineOnClip = FileAudio.Load(Main.settings.dieselStartupSound);
            else
                __instance.engineOnClip = originalEngineOnClip;

            SetEngineClip(engineAudio, Main.settings.dieselEngineSound, Main.settings.dieselEnginePitch);

            if (Main.settings.dieselShutdownSound != null)
                __instance.engineOffClip = FileAudio.Load(Main.settings.dieselShutdownSound);
            else
                __instance.engineOffClip = originalEngineOffClip;
        }

        public static void ResetAllAudio()
        {
            foreach (var audio in Component.FindObjectsOfType<LocoAudioDiesel>())
                ResetAudio(audio);
        }

        [HarmonyPatch(typeof(LocoAudioDiesel), nameof(LocoAudioDiesel.SetupForCar))]
        public static class SetupForCarPatch
        {
            public static void Postfix(LocoAudioDiesel __instance)
            {
                ResetAudio(__instance);
            }
        }
    }
}