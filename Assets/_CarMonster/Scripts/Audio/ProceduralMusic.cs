using System;
using UnityEngine;

namespace CarMonster
{
    public enum MusicGenre
    {
        Synthwave,
        Phonk,
        Techno
    }

    public enum ScaleType
    {
        Minor,
        Major,
        Phrygian,
        Dorian
    }

    /// <summary>Параметры процедурного трека локации.</summary>
    [Serializable]
    public class MusicStyle
    {
        public MusicGenre genre = MusicGenre.Synthwave;
        public float bpm = 116f;
        [Tooltip("MIDI-нота тоники (45 = A2).")]
        public int rootMidi = 45;
        public ScaleType scale = ScaleType.Minor;
        [Tooltip("Ступени лада для аккордов, по аккорду на 2 такта.")]
        public int[] progression = { 0, 5, 2, 6 };
        [Tooltip("Длина петли в тактах (кратно 8).")]
        public int bars = 32;
        public int seed = 1;
        [Range(0f, 0.6f)] public float swing;
    }

    /// <summary>
    /// Генератор зацикленного трека: барабаны, бас, пэды, арпеджио/ковбелл, всё синтезом.
    /// Чистый C# без Unity API — можно звать из фонового потока.
    /// </summary>
    public static class ProceduralMusic
    {
        private static readonly int[] MinorScale = { 0, 2, 3, 5, 7, 8, 10 };
        private static readonly int[] MajorScale = { 0, 2, 4, 5, 7, 9, 11 };
        private static readonly int[] PhrygianScale = { 0, 1, 3, 5, 7, 8, 10 };
        private static readonly int[] DorianScale = { 0, 2, 3, 5, 7, 9, 10 };

        private struct Section
        {
            public bool kick, bass, snare, hats, pad, lead;
        }

        private sealed class Ctx
        {
            public float[] l;
            public float[] r;
            public int sr;
            public int frames;
            public uint noise = 2463534242u;

            public float Noise()
            {
                noise ^= noise << 13;
                noise ^= noise >> 17;
                noise ^= noise << 5;
                return (noise & 0xFFFFFF) / 8388608f - 1f;
            }
        }

        /// <summary>Возвращает стерео-сэмплы (interleaved) длиной ровно в bars тактов.</summary>
        public static float[] Generate(MusicStyle style, int sampleRate)
        {
            double beat = 60.0 / Math.Max(40f, style.bpm);
            double step = beat / 4.0;
            int bars = Math.Max(8, style.bars);
            int frames = (int)(bars * 4 * beat * sampleRate);

            var c = new Ctx { l = new float[frames], r = new float[frames], sr = sampleRate, frames = frames };
            var rng = new System.Random(style.seed);
            int[] scale = ScaleOf(style.scale);
            int[] progression = style.progression != null && style.progression.Length > 0 ? style.progression : new[] { 0 };
            int[] motif = new int[16];
            bool[] motifMask = new bool[16];

            for (int bar = 0; bar < bars; bar++)
            {
                Section sec = SectionAt(bar, bars, style.genre);
                int degree = progression[(bar / 2) % progression.Length];
                int chordRoot = style.rootMidi + Note(scale, degree);
                int third = style.rootMidi + Note(scale, degree + 2);
                int fifth = style.rootMidi + Note(scale, degree + 4);

                if (bar % 2 == 0)
                {
                    // Новый мотив ковбелла/лида на каждые 2 такта, вторая половина петли повторяет первую.
                    var motifRng = new System.Random(style.seed * 31 + (bar / 2) % 4);
                    for (int i = 0; i < 16; i++)
                    {
                        motif[i] = degree + motifRng.Next(0, 5) * (motifRng.NextDouble() > 0.5 ? 1 : 2) % 7;
                        motifMask[i] = motifRng.NextDouble() < (style.genre == MusicGenre.Phonk ? 0.55 : 0.4) || i == 0;
                    }
                    if (sec.pad)
                    {
                        double padStart = bar * 4 * beat;
                        double padLen = 8 * beat;
                        float padAmp = style.genre == MusicGenre.Techno ? 0.05f : 0.075f;
                        Pad(c, padStart, padLen, chordRoot + 12, padAmp);
                        Pad(c, padStart, padLen, third + 12, padAmp);
                        Pad(c, padStart, padLen, fifth + 12, padAmp);
                    }
                }

                for (int st = 0; st < 16; st++)
                {
                    double t = (bar * 16 + st) * step;
                    if (st % 2 == 1) t += style.swing * step * 0.5;

                    switch (style.genre)
                    {
                        case MusicGenre.Synthwave:
                            if (sec.kick && st % 4 == 0) Kick(c, t, 0.9f);
                            if (sec.snare && (st == 4 || st == 12)) Snare(c, t, 0.45f);
                            if (sec.hats && st % 2 == 0) Hat(c, t, st % 4 == 2 ? 0.16f : 0.1f, st % 8 == 6);
                            if (sec.bass && st % 2 == 0)
                                Bass(c, t, step * 1.8, chordRoot - 12 + (st % 4 == 2 ? 12 : 0), 0.32f, 900f);
                            if (sec.lead)
                            {
                                int[] arp = { chordRoot + 12, third + 12, fifth + 12, chordRoot + 24 };
                                Pluck(c, t, step * 0.9, arp[st % 4], 0.1f, st % 2 == 0 ? -0.4f : 0.4f);
                            }
                            break;

                        case MusicGenre.Phonk:
                            if (sec.kick && (st == 0 || st == 10 || (bar % 2 == 1 && st == 7)))
                                Bass808(c, t, step * (st == 0 ? 6 : 4), chordRoot - 12, 0.55f);
                            if (sec.snare && st == 8) Clap(c, t, 0.5f);
                            if (sec.hats)
                            {
                                bool roll = bar % 4 == 3 && st >= 12;
                                if (st % 2 == 0 || roll) Hat(c, t, 0.09f, false);
                                if (roll) Hat(c, t + step * 0.5, 0.07f, false);
                            }
                            if (sec.lead && motifMask[st])
                                Cowbell(c, t, style.rootMidi + 24 + Note(scale, motif[st]), 0.16f);
                            break;

                        case MusicGenre.Techno:
                            if (sec.kick && st % 4 == 0) Kick(c, t, 1f);
                            if (sec.snare && (st == 4 || st == 12)) Clap(c, t, 0.32f);
                            if (sec.hats)
                            {
                                if (st % 4 == 2) Hat(c, t, 0.17f, true);
                                else Hat(c, t, 0.05f, false);
                            }
                            if (sec.bass && (st % 4 == 2 || st % 4 == 3))
                                Bass(c, t, step * 0.8, chordRoot - 12, 0.3f, 650f);
                            if (sec.lead && (st == 3 || st == 11 || (bar % 2 == 1 && st == 14)))
                            {
                                Stab(c, t, step * 1.5, chordRoot + 12, 0.07f);
                                Stab(c, t, step * 1.5, third + 12, 0.07f);
                                Stab(c, t, step * 1.5, fifth + 12, 0.07f);
                            }
                            break;
                    }
                }
            }

            return Master(c);
        }

        private static Section SectionAt(int bar, int bars, MusicGenre genre)
        {
            float p = bar / (float)bars;
            var s = new Section { pad = true, hats = true };
            if (p < 0.125f) // интро
            {
                s.lead = genre == MusicGenre.Phonk;
                return s;
            }
            if (p >= 0.625f && p < 0.75f) // брейк
            {
                s.lead = true;
                s.hats = genre == MusicGenre.Techno;
                return s;
            }
            s.kick = true;
            s.bass = true;
            s.snare = true;
            s.lead = p >= 0.375f;
            return s;
        }

        private static int[] ScaleOf(ScaleType type) => type switch
        {
            ScaleType.Major => MajorScale,
            ScaleType.Phrygian => PhrygianScale,
            ScaleType.Dorian => DorianScale,
            _ => MinorScale
        };

        private static int Note(int[] scale, int degree)
        {
            int octave = Math.DivRem(degree, 7, out int idx);
            if (idx < 0)
            {
                idx += 7;
                octave--;
            }
            return scale[idx] + octave * 12;
        }

        private static float Freq(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        private static void Write(Ctx c, int i, float v, float pan)
        {
            c.l[i] += v * (1f - Mathf.Max(0f, pan));
            c.r[i] += v * (1f + Mathf.Min(0f, pan));
        }

        private static int StartIndex(Ctx c, double t) => (int)(t * c.sr);

        private static void Kick(Ctx c, double t, float amp)
        {
            int start = StartIndex(c, t);
            int len = (int)(0.42f * c.sr);
            double phase = 0;
            for (int n = 0; n < len && start + n < c.frames; n++)
            {
                float tau = n / (float)c.sr;
                float f = 45f + 110f * Mathf.Exp(-tau * 28f);
                phase += f / c.sr;
                float env = Mathf.Exp(-tau * 7f);
                float v = (float)Math.Sin(phase * 2 * Math.PI) * env * amp;
                if (tau < 0.003f) v += c.Noise() * 0.3f * amp;
                Write(c, start + n, v, 0f);
            }
        }

        private static void Bass808(Ctx c, double t, double dur, int midi, float amp)
        {
            int start = StartIndex(c, t);
            int len = (int)(dur * c.sr);
            float f0 = Freq(midi);
            double phase = 0;
            for (int n = 0; n < len && start + n < c.frames; n++)
            {
                float tau = n / (float)c.sr;
                float f = f0 * (1f + 1.5f * Mathf.Exp(-tau * 35f));
                phase += f / c.sr;
                float env = Mathf.Exp(-tau * 1.4f) * Mathf.Clamp01((len - n) / (0.02f * c.sr));
                float v = (float)Math.Tanh(Math.Sin(phase * 2 * Math.PI) * 2.2) * env * amp;
                Write(c, start + n, v, 0f);
            }
        }

        private static void Snare(Ctx c, double t, float amp)
        {
            int start = StartIndex(c, t);
            int len = (int)(0.25f * c.sr);
            float lp = 0f;
            for (int n = 0; n < len && start + n < c.frames; n++)
            {
                float tau = n / (float)c.sr;
                float noise = c.Noise();
                lp += 0.25f * (noise - lp);
                float body = Mathf.Sin(2f * Mathf.PI * 185f * tau) * Mathf.Exp(-tau * 30f) * 0.5f;
                float v = ((noise - lp) * Mathf.Exp(-tau * 18f) + body) * amp;
                Write(c, start + n, v, 0.05f);
            }
        }

        private static void Clap(Ctx c, double t, float amp)
        {
            int start = StartIndex(c, t);
            int len = (int)(0.3f * c.sr);
            float lp = 0f;
            for (int n = 0; n < len && start + n < c.frames; n++)
            {
                float tau = n / (float)c.sr;
                float noise = c.Noise();
                lp += 0.35f * (noise - lp);
                float burst = tau < 0.03f ? Mathf.Exp(-(tau % 0.01f) * 300f) : Mathf.Exp(-(tau - 0.03f) * 14f);
                Write(c, start + n, (noise - lp) * burst * amp, -0.05f);
            }
        }

        private static void Hat(Ctx c, double t, float amp, bool open)
        {
            int start = StartIndex(c, t);
            int len = (int)((open ? 0.22f : 0.06f) * c.sr);
            float lp = 0f;
            float decay = open ? 13f : 70f;
            for (int n = 0; n < len && start + n < c.frames; n++)
            {
                float tau = n / (float)c.sr;
                float noise = c.Noise();
                lp += 0.6f * (noise - lp);
                Write(c, start + n, (noise - lp) * Mathf.Exp(-tau * decay) * amp, 0.25f);
            }
        }

        private static void Bass(Ctx c, double t, double dur, int midi, float amp, float cutoff)
        {
            int start = StartIndex(c, t);
            int len = (int)(dur * c.sr);
            float f = Freq(midi);
            double phase = 0;
            float y = 0f;
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / c.sr);
            for (int n = 0; n < len && start + n < c.frames; n++)
            {
                phase += f / c.sr;
                float saw = (float)(2.0 * (phase - Math.Floor(phase + 0.5)));
                y += a * (saw - y);
                float env = Mathf.Clamp01(n / (0.004f * c.sr)) * Mathf.Clamp01((len - n) / (0.01f * c.sr));
                Write(c, start + n, y * env * amp, 0f);
            }
        }

        private static void Pad(Ctx c, double t, double dur, int midi, float amp)
        {
            int start = StartIndex(c, t);
            int len = (int)(dur * c.sr);
            float f = Freq(midi);
            float detune = Mathf.Pow(2f, 0.12f / 12f);
            double p1 = 0, p2 = 0.37, p3 = 0.71;
            float yl = 0f, yr = 0f;
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * 1400f / c.sr);
            float attack = 0.35f * c.sr;
            float release = 0.5f * c.sr;
            for (int n = 0; n < len && start + n < c.frames; n++)
            {
                p1 += f / c.sr;
                p2 += f * detune / c.sr;
                p3 += f / detune / c.sr;
                float s1 = (float)(2.0 * (p1 - Math.Floor(p1 + 0.5)));
                float s2 = (float)(2.0 * (p2 - Math.Floor(p2 + 0.5)));
                float s3 = (float)(2.0 * (p3 - Math.Floor(p3 + 0.5)));
                yl += a * ((s1 + s2) * 0.5f - yl);
                yr += a * ((s1 + s3) * 0.5f - yr);
                float env = Mathf.Clamp01(n / attack) * Mathf.Clamp01((len - n) / release);
                int i = start + n;
                c.l[i] += yl * env * amp;
                c.r[i] += yr * env * amp;
            }
        }

        private static void Pluck(Ctx c, double t, double dur, int midi, float amp, float pan)
        {
            int start = StartIndex(c, t);
            int len = (int)(dur * c.sr);
            float f = Freq(midi);
            double phase = 0;
            float y = 0f;
            for (int n = 0; n < len && start + n < c.frames; n++)
            {
                float tau = n / (float)c.sr;
                phase += f / c.sr;
                float sq = phase - Math.Floor(phase) < 0.5 ? 1f : -1f;
                float cutoff = 400f + 4000f * Mathf.Exp(-tau * 18f);
                y += (1f - Mathf.Exp(-2f * Mathf.PI * cutoff / c.sr)) * (sq - y);
                float env = Mathf.Exp(-tau * 9f) * Mathf.Clamp01((len - n) / (0.005f * c.sr));
                Write(c, start + n, y * env * amp, pan);
            }
        }

        private static void Cowbell(Ctx c, double t, int midi, float amp)
        {
            int start = StartIndex(c, t);
            int len = (int)(0.35f * c.sr);
            float f1 = Freq(midi);
            float f2 = f1 * 1.48f;
            double p1 = 0, p2 = 0;
            float lp = 0f, hp = 0f;
            for (int n = 0; n < len && start + n < c.frames; n++)
            {
                float tau = n / (float)c.sr;
                p1 += f1 / c.sr;
                p2 += f2 / c.sr;
                float sq = (p1 - Math.Floor(p1) < 0.5 ? 1f : -1f) + (p2 - Math.Floor(p2) < 0.5 ? 1f : -1f);
                lp += 0.35f * (sq - lp);
                hp += 0.05f * (lp - hp);
                float env = Mathf.Exp(-tau * 10f);
                Write(c, start + n, (lp - hp) * env * amp, 0.15f);
            }
        }

        private static void Stab(Ctx c, double t, double dur, int midi, float amp)
        {
            int start = StartIndex(c, t);
            int len = (int)(dur * c.sr);
            float f = Freq(midi);
            double phase = 0;
            float y = 0f;
            for (int n = 0; n < len && start + n < c.frames; n++)
            {
                float tau = n / (float)c.sr;
                phase += f / c.sr;
                float saw = (float)(2.0 * (phase - Math.Floor(phase + 0.5)));
                float cutoff = 300f + 2500f * Mathf.Exp(-tau * 12f);
                y += (1f - Mathf.Exp(-2f * Mathf.PI * cutoff / c.sr)) * (saw - y);
                float env = Mathf.Exp(-tau * 6f) * Mathf.Clamp01((len - n) / (0.008f * c.sr));
                Write(c, start + n, y * env * amp, -0.2f);
            }
        }

        private static float[] Master(Ctx c)
        {
            float peak = 0.0001f;
            for (int i = 0; i < c.frames; i++)
            {
                peak = Mathf.Max(peak, Mathf.Abs(c.l[i]));
                peak = Mathf.Max(peak, Mathf.Abs(c.r[i]));
            }
            float gain = 1.1f / peak;
            var output = new float[c.frames * 2];
            int fade = Math.Min(c.frames / 2, c.sr / 200);
            for (int i = 0; i < c.frames; i++)
            {
                // Мягкий лимитер + микрофейды на стыке петли (без щелчков).
                float edge = Mathf.Min(1f, Mathf.Min(i, c.frames - 1 - i) / (float)Math.Max(1, fade));
                output[i * 2] = (float)Math.Tanh(c.l[i] * gain) * 0.85f * edge;
                output[i * 2 + 1] = (float)Math.Tanh(c.r[i] * gain) * 0.85f * edge;
            }
            return output;
        }
    }
}
