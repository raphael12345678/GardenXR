using System.Collections.Generic;
using UnityEngine;

namespace GardenXR
{
    public enum GardenSound { Place, Error, Delete, Undo, MenuOpen, MenuClose, Click, Grab, Release }

    /// <summary>
    /// Sons d'interaction générés par code (aucun fichier audio à importer).
    /// Joués en 3D à la position de l'action (spatialisation, cf. section 3 du canevas).
    /// Si un AudioClip est fourni ailleurs dans l'inspecteur, il reste prioritaire.
    /// </summary>
    public static class GardenSounds
    {
        public static bool Enabled = true;
        public static float Volume = 0.6f;

        const int Rate = 44100;
        static readonly Dictionary<GardenSound, AudioClip> _clips = new Dictionary<GardenSound, AudioClip>();

        public static void Play(GardenSound sound, Vector3 position)
        {
            if (!Enabled) return;
            var clip = Get(sound);
            if (clip) AudioSource.PlayClipAtPoint(clip, position, Volume);
        }

        /// <summary>Joue près de la tête de l'utilisateur (sons d'interface).</summary>
        public static void PlayAtHead(GardenSound sound)
        {
            var cam = Camera.main;
            Play(sound, cam ? cam.transform.position : Vector3.zero);
        }

        static AudioClip Get(GardenSound sound)
        {
            if (_clips.TryGetValue(sound, out var clip) && clip) return clip;

            clip = sound switch
            {
                // Pop montant à deux notes : succès
                GardenSound.Place     => Build("Place",   new Seg(520, 700, 0.06f, 0.01f), new Seg(880, 900, 0.12f)),
                // Deux buzz graves : erreur
                GardenSound.Error     => Build("Error",   new Seg(190, 170, 0.09f, 0.04f, square: true), new Seg(160, 140, 0.12f, square: true)),
                // Souffle descendant : suppression
                GardenSound.Delete    => Build("Delete",  new Seg(700, 180, 0.22f, noise: 0.35f)),
                // Glissé descendant doux : annulation
                GardenSound.Undo      => Build("Undo",    new Seg(760, 420, 0.14f)),
                GardenSound.MenuOpen  => Build("Open",    new Seg(440, 470, 0.06f, 0.01f), new Seg(660, 680, 0.09f)),
                GardenSound.MenuClose => Build("Close",   new Seg(660, 640, 0.06f, 0.01f), new Seg(440, 420, 0.09f)),
                GardenSound.Click     => Build("Click",   new Seg(1300, 1100, 0.035f)),
                GardenSound.Grab      => Build("Grab",    new Seg(300, 430, 0.06f)),
                GardenSound.Release   => Build("Release", new Seg(430, 300, 0.06f)),
                _ => null
            };

            _clips[sound] = clip;
            return clip;
        }

        // ---------- Synthèse ----------

        readonly struct Seg
        {
            public readonly float f0, f1, dur, gap, noise;
            public readonly bool square;

            public Seg(float f0, float f1, float dur, float gap = 0f, float noise = 0f, bool square = false)
            {
                this.f0 = f0; this.f1 = f1; this.dur = dur; this.gap = gap; this.noise = noise; this.square = square;
            }
        }

        static AudioClip Build(string name, params Seg[] segs)
        {
            int total = 0;
            foreach (var s in segs) total += Mathf.CeilToInt((s.dur + s.gap) * Rate);

            var data = new float[total];
            var rng = new System.Random(7);
            int i = 0;

            foreach (var s in segs)
            {
                int n = Mathf.CeilToInt(s.dur * Rate);
                double phase = 0;
                for (int k = 0; k < n && i < total; k++, i++)
                {
                    float t = (float)k / n;
                    float freq = Mathf.Lerp(s.f0, s.f1, t);
                    phase += 2.0 * System.Math.PI * freq / Rate;

                    float v = Mathf.Sin((float)phase);
                    if (s.square) v = Mathf.Sign(v) * 0.45f;
                    if (s.noise > 0f) v = Mathf.Lerp(v, (float)(rng.NextDouble() * 2.0 - 1.0), s.noise);

                    float attack = Mathf.Min(1f, k / (0.004f * Rate)); // évite les clics
                    float decay = Mathf.Exp(-3.5f * t);
                    data[i] = v * attack * decay * 0.8f;
                }
                i = Mathf.Min(total, i + Mathf.CeilToInt(s.gap * Rate)); // silence entre deux notes
            }

            var clip = AudioClip.Create("GardenXR_" + name, total, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
