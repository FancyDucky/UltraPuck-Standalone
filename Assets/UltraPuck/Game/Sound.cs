using System;
using System.Collections.Generic;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>Every sound effect the game makes.</summary>
    public enum Sfx
    {
        UiMove, UiSelect, UiBack, UiDeny,
        Dash, Shoot, Hit, BigHit, Goal,
        /// <summary>A loop, played while someone charges a shot (higher as the charge grows).</summary>
        Charge,
        CardDeal, CardPick, CardAbsorb,
        /// <summary>Swap utility: a sharp clap when you teleport with the rock.</summary>
        Swap,
    }

    /// <summary>
    /// The sound effects, made here from tones and noise (no sound files), in the chip-tune spirit of the music.
    /// <see cref="Render"/> gives one effect as samples at <see cref="Rate"/>, always the same ones.
    /// To use a recorded sound instead, put a file named after the effect (Goal.wav, Dash.wav...) in
    /// Resources/UltraPuck/Sounds: <see cref="Sound"/> plays that in place of the one made here.
    /// </summary>
    public static class SoundBank
    {
        public const int Rate = 44100;
        private const float Tau = Mathf.PI * 2f;

        private enum Wave { Sine, Triangle, Square }
        private delegate float Curve(float t);

        /// <summary>Played round and round (its end runs into its start without a click).</summary>
        public static bool Loops(Sfx id) { return id == Sfx.Charge; }

        public static float[] Render(Sfx id)
        {
            float[] s;
            switch (id)
            {
                case Sfx.UiMove:
                    // a soft tick
                    s = New(0.06f);
                    Tone(s, 0f, 0.06f, 0.8f, Wave.Triangle, t => 1320f, t => Pluck(t, 0.002f, 75f));
                    Tone(s, 0f, 0.03f, 0.2f, Wave.Sine, t => 2640f, t => Pluck(t, 0.001f, 120f));
                    break;

                case Sfx.UiSelect:
                    // two notes going up
                    s = New(0.2f);
                    Tone(s, 0f, 0.07f, 0.7f, Wave.Triangle, t => 988f, t => Pluck(t, 0.002f, 30f));
                    Tone(s, 0.06f, 0.14f, 0.8f, Wave.Triangle, t => 1480f, t => Pluck(t, 0.002f, 24f));
                    Tone(s, 0.06f, 0.14f, 0.12f, Wave.Square, t => 1480f, t => Pluck(t, 0.002f, 40f));
                    break;

                case Sfx.UiBack:
                    // two notes going down
                    s = New(0.18f);
                    Tone(s, 0f, 0.07f, 0.7f, Wave.Triangle, t => 880f, t => Pluck(t, 0.002f, 30f));
                    Tone(s, 0.06f, 0.12f, 0.8f, Wave.Triangle, t => 587f, t => Pluck(t, 0.002f, 26f));
                    break;

                case Sfx.UiDeny:
                    // a low double buzz: "can't do that"
                    s = New(0.22f);
                    Tone(s, 0f, 0.08f, 0.6f, Wave.Square, t => 150f, t => Pluck(t, 0.003f, 12f));
                    Tone(s, 0.11f, 0.09f, 0.6f, Wave.Square, t => 132f, t => Pluck(t, 0.003f, 12f));
                    Lowpass(s, 1400f);
                    break;

                case Sfx.Dash:
                    // a cartoon sword-swing whoosh, low in pitch: wind squeezed into a narrow band that sweeps up fast as
                    // it rushes past and drops away again, with a deeper band under it for weight
                    s = New(0.42f);
                    Wind(s, 0f, 0.42f, 1f, t => Rush(t, 260f, 1150f, 300f), 4f, t => Gust(t), 11u);
                    Wind(s, 0f, 0.42f, 0.7f, t => Rush(t, 130f, 520f, 150f), 2.5f, t => Gust(t), 12u);
                    Wind(s, 0f, 0.42f, 0.22f, t => Rush(t, 600f, 2600f, 700f), 1.5f, t => Gust(t) * Gust(t), 13u);
                    break;

                case Sfx.Shoot:
                    // the crack of the stick, a note that drops fast, a hiss behind it and a thump under it
                    s = New(0.24f);
                    Noise(s, 0f, 0.012f, 0.9f, t => 9000f, 800f, t => 1f, 21u);
                    Tone(s, 0f, 0.14f, 0.8f, Wave.Triangle, t => Glide(1150f, 190f, t, 0.028f), t => Pluck(t, 0.001f, 26f));
                    Tone(s, 0f, 0.1f, 0.25f, Wave.Square, t => Glide(1150f, 190f, t, 0.028f), t => Pluck(t, 0.001f, 40f));
                    Noise(s, 0f, 0.2f, 0.45f, t => Glide(8000f, 1500f, t, 0.05f), 1200f, t => Pluck(t, 0.002f, 24f), 22u);
                    Tone(s, 0f, 0.12f, 0.6f, Wave.Sine, t => Glide(190f, 75f, t, 0.03f), t => Pluck(t, 0.002f, 30f));
                    break;

                case Sfx.Hit:
                    // two players bumping: a short low thud
                    s = New(0.22f);
                    Tone(s, 0f, 0.2f, 1f, Wave.Sine, t => Glide(170f, 52f, t, 0.035f), t => Pluck(t, 0.002f, 20f));
                    Noise(s, 0f, 0.09f, 0.7f, t => Glide(2200f, 600f, t, 0.03f), 0f, t => Pluck(t, 0.001f, 48f), 31u);
                    Noise(s, 0f, 0.006f, 0.5f, t => 8000f, 1500f, t => 1f, 32u);
                    Drive(s, 1.4f);
                    break;

                case Sfx.BigHit:
                    // a heavy check (the ones with lightning): deeper and longer, with a crackle
                    s = New(0.55f);
                    Tone(s, 0f, 0.5f, 1f, Wave.Sine, t => Glide(120f, 36f, t, 0.09f), t => Pluck(t, 0.002f, 8f));
                    Tone(s, 0f, 0.2f, 0.4f, Wave.Square, t => Glide(240f, 60f, t, 0.05f), t => Pluck(t, 0.002f, 22f));
                    Noise(s, 0f, 0.45f, 0.8f, t => Glide(6000f, 350f, t, 0.08f), 0f, t => Pluck(t, 0.001f, 11f), 41u);
                    Crackle(s, 0.02f, 0.35f, 0.45f, 9f, 42u);
                    Drive(s, 2.2f);
                    break;

                case Sfx.Goal:
                    // the puck blowing up in the net: a crack, a boom that drops away, a rumble closing down, sparks
                    s = New(1.9f);
                    Noise(s, 0f, 0.03f, 0.9f, t => 10000f, 500f, t => 1f, 51u);
                    Tone(s, 0f, 1.5f, 1f, Wave.Sine, t => Glide(150f, 30f, t, 0.28f), t => Pluck(t, 0.003f, 2.8f));
                    Tone(s, 0f, 0.5f, 0.35f, Wave.Square, t => Glide(300f, 45f, t, 0.12f), t => Pluck(t, 0.003f, 9f));
                    Noise(s, 0f, 1.9f, 0.9f, t => Glide(5500f, 160f, t, 0.38f), 0f, t => Pluck(t, 0.004f, 2.6f), 52u);
                    Noise(s, 0.11f, 0.8f, 0.35f, t => Glide(3000f, 300f, t, 0.2f), 200f, t => Pluck(t, 0.01f, 6f), 53u);
                    Crackle(s, 0.03f, 0.9f, 0.3f, 4f, 54u);
                    for (var k = 0; k < 3; k++)
                    {
                        var f = new[] { 1760f, 2217f, 2637f }[k];
                        var phase = k * 2.1f;
                        Tone(s, 0.05f, 1.4f, 0.07f, Wave.Sine, t => f * (1f - 0.12f * t), t => Pluck(t, 0.02f, 3.2f) * (0.6f + 0.4f * Mathf.Sin(Tau * 7f * t + phase)));
                    }
                    Drive(s, 1.8f);
                    break;

                case Sfx.Charge:
                    // a buzzing hum, exactly one second long so it joins up with itself; two notes a touch apart make it throb
                    s = New(1f);
                    for (var h = 1; h <= 8; h++)
                    {
                        var n = h;
                        Tone(s, 0f, 1f, 0.5f / n, Wave.Sine, t => 165f * n, t => 1f, false);
                        Tone(s, 0f, 1f, 0.35f / n, Wave.Sine, t => 167f * n, t => 1f, false);
                    }
                    break;

                case Sfx.CardDeal:
                    // a card flicked onto the table: a swish, then a soft tap as it lands
                    s = New(0.16f);
                    Noise(s, 0f, 0.1f, 1f, t => Glide(9000f, 2200f, t, 0.04f), 900f, t => Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.1f)), 1.5f), 61u);
                    Noise(s, 0.1f, 0.03f, 0.5f, t => 2500f, 300f, t => Pluck(t, 0.001f, 90f), 62u);
                    Tone(s, 0.1f, 0.04f, 0.3f, Wave.Sine, t => Glide(420f, 200f, t, 0.01f), t => Pluck(t, 0.001f, 70f));
                    break;

                case Sfx.CardPick:
                    // one low, warm chime that lands with a soft knock: a deep note, the note a fifth above it and the
                    // octave on top, struck a hair apart like a strum, ringing out together
                    s = New(0.6f);
                    Tone(s, 0f, 0.09f, 0.55f, Wave.Sine, t => Glide(190f, 98f, t, 0.025f), t => Pluck(t, 0.002f, 34f));
                    Tone(s, 0f, 0.6f, 1f, Wave.Sine, t => 293.7f, t => Pluck(t, 0.005f, 5.5f));
                    Tone(s, 0f, 0.3f, 0.22f, Wave.Triangle, t => 293.7f, t => Pluck(t, 0.004f, 14f));
                    Tone(s, 0.014f, 0.58f, 0.6f, Wave.Sine, t => 440f, t => Pluck(t, 0.005f, 6.5f));
                    Tone(s, 0.028f, 0.56f, 0.45f, Wave.Sine, t => 587.3f, t => Pluck(t, 0.005f, 7.5f));
                    Tone(s, 0.028f, 0.3f, 0.07f, Wave.Sine, t => 1174.7f, t => Pluck(t, 0.004f, 16f));
                    break;

                case Sfx.CardAbsorb:
                    // the card drawn into the player (a rising rush), a drop landing in water, then ripples ringing out
                    s = New(1f);
                    Tone(s, 0f, 0.3f, 0.6f, Wave.Sine, t => 160f * Mathf.Pow(8.5f, t / 0.3f), t => Sq(t / 0.3f));
                    Tone(s, 0f, 0.3f, 0.15f, Wave.Triangle, t => 80f * Mathf.Pow(8.5f, t / 0.3f), t => Sq(t / 0.3f));
                    Noise(s, 0f, 0.3f, 0.25f, t => 600f + 6000f * t / 0.3f, 300f, t => Sq(t / 0.3f), 81u);
                    Tone(s, 0.3f, 0.2f, 0.9f, Wave.Sine, t => 300f + 650f * Mathf.Clamp01(t / 0.06f), t => Pluck(t, 0.002f, 16f));
                    Tone(s, 0.3f, 0.35f, 0.5f, Wave.Sine, t => Glide(110f, 60f, t, 0.08f), t => Pluck(t, 0.003f, 10f));
                    for (var k = 0; k < 3; k++)
                    {
                        var f = new[] { 1568f, 2093f, 2637f }[k];
                        var phase = k * 1.7f;
                        Tone(s, 0.32f, 0.68f, 0.14f, Wave.Sine, t => f, t => Pluck(t, 0.03f, 4.5f) * (0.65f + 0.35f * Mathf.Sin(Tau * 9f * t + phase)));
                    }
                    Noise(s, 0.3f, 0.6f, 0.15f, t => Glide(3500f, 500f, t, 0.2f), 200f, t => Pluck(t, 0.01f, 5.5f), 82u);
                    break;

                case Sfx.Swap:
                    // a human hand-clap in a rink: dry slap up front, then a short room reverb
                    s = New(0.7f);
                    Tone(s, 0f, 0.028f, 0.45f, Wave.Sine, t => Glide(220f, 85f, t, 0.014f), t => Mathf.Exp(-t * 95f));
                    Noise(s, 0f, 0.012f, 0.85f, t => 9000f, 1200f, t => Mathf.Exp(-t * 220f), 91u); // attack transient
                    Noise(s, 0f, 0.07f, 1f, t => Glide(5200f, 1600f, t, 0.045f), 700f, t => Mathf.Exp(-t * 42f), 92u);
                    Noise(s, 0.004f, 0.06f, 0.55f, t => Glide(2800f, 900f, t, 0.04f), 350f, t => Mathf.Exp(-t * 38f), 93u);
                    Tone(s, 0.002f, 0.045f, 0.12f, Wave.Sine, t => 1250f, t => Mathf.Exp(-t * 48f)); // hollow palm cavity
                    Noise(s, 0.025f, 0.12f, 0.18f, t => Glide(3800f, 700f, t, 0.07f), 450f, t => Mathf.Exp(-t * 16f), 94u);
                    Reverb(s, 0.48f);
                    break;

                default:
                    throw new ArgumentOutOfRangeException("id");
            }
            if (!Loops(id)) FadeOut(s, 0.008f);
            Normalize(s, Loops(id) ? 0.6f : 0.9f);
            return s;
        }

        // ------------------------------------------------------------------ building blocks

        private static float[] New(float seconds) { return new float[Mathf.RoundToInt(seconds * Rate)]; }
        private static float Sq(float x) { x = Mathf.Clamp01(x); return x * x; }

        // the dash whoosh passes by at this moment (seconds in)
        private const float RushAt = 0.15f;

        /// <summary>Loudness of something rushing past: builds faster and faster up to <see cref="RushAt"/>, then drops off quickly.</summary>
        private static float Gust(float t) { return t < RushAt ? Mathf.Pow(t / RushAt, 2.2f) : Mathf.Exp(-(t - RushAt) / 0.055f); }

        /// <summary>Pitch (Hz) of something rushing past: climbs from <paramref name="from"/> to <paramref name="top"/> as it arrives, then falls to <paramref name="to"/>.</summary>
        private static float Rush(float t, float from, float top, float to)
        {
            return t < RushAt ? from * Mathf.Pow(top / from, Mathf.Pow(t / RushAt, 1.6f)) : Glide(top, to, t - RushAt, 0.07f);
        }

        /// <summary>
        /// Adds wind: noise squeezed into a band around <paramref name="centre"/> (Hz, may change over time), so it has a
        /// pitch you can hear moving. A bigger <paramref name="sharpness"/> makes the band narrower (more of a whistle).
        /// </summary>
        private static void Wind(float[] s, float at, float length, float gain, Curve centre, float sharpness, Curve loud, uint seed)
        {
            var first = Mathf.RoundToInt(at * Rate);
            var count = Mathf.RoundToInt(length * Rate);
            float low = 0f, band = 0f;
            var damp = 1f / Mathf.Max(0.5f, sharpness);
            for (var k = 0; k < count && first + k < s.Length; k++)
            {
                var t = k / (float)Rate;
                var f = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(centre(t), 30f, 6000f) / Rate);
                low += f * band;
                var high = Next(ref seed) - low - damp * band;
                band += f * high;
                var edge = Mathf.Min(1f, (length - t) / 0.01f);
                s[first + k] += band * damp * gain * loud(t) * edge;
            }
        }

        /// <summary>Loudness of a plucked note: up in <paramref name="attack"/> seconds, then dying away (faster for a bigger <paramref name="decay"/>).</summary>
        private static float Pluck(float t, float attack, float decay) { return Mathf.Min(1f, t / attack) * Mathf.Exp(-decay * t); }

        /// <summary>Slides from one value to another, most of the way within <paramref name="time"/> seconds.</summary>
        private static float Glide(float from, float to, float t, float time) { return to + (from - to) * Mathf.Exp(-t / time); }

        /// <summary>Adds a note: its pitch (Hz) and loudness may change over its length (t = seconds since it began).</summary>
        private static void Tone(float[] s, float at, float length, float gain, Wave wave, Curve hz, Curve loud, bool softEnd = true)
        {
            var first = Mathf.RoundToInt(at * Rate);
            var count = Mathf.RoundToInt(length * Rate);
            var phase = 0.0;
            for (var k = 0; k < count && first + k < s.Length; k++)
            {
                var t = k / (float)Rate;
                var x = (float)(phase - Math.Floor(phase));
                phase += hz(t) / Rate;
                float v;
                switch (wave)
                {
                    case Wave.Triangle: v = x < 0.25f ? 4f * x : x < 0.75f ? 2f - 4f * x : 4f * x - 4f; break;
                    case Wave.Square: v = x < 0.5f ? 1f : -1f; break;
                    default: v = Mathf.Sin(x * Tau); break;
                }
                var edge = softEnd ? Mathf.Min(1f, (length - t) / 0.003f) : 1f; // no click where the note stops
                s[first + k] += v * gain * loud(t) * edge;
            }
        }

        /// <summary>
        /// Adds hiss: white noise with the highs above <paramref name="lowpass"/> (Hz, may change over time) and the
        /// lows below <paramref name="highpass"/> taken off. The same <paramref name="seed"/> always gives the same hiss.
        /// </summary>
        private static void Noise(float[] s, float at, float length, float gain, Curve lowpass, float highpass, Curve loud, uint seed)
        {
            var first = Mathf.RoundToInt(at * Rate);
            var count = Mathf.RoundToInt(length * Rate);
            float l1 = 0f, l2 = 0f, low = 0f;
            var ah = highpass > 0f ? 1f - Mathf.Exp(-Tau * highpass / Rate) : 0f;
            for (var k = 0; k < count && first + k < s.Length; k++)
            {
                var t = k / (float)Rate;
                var fc = Mathf.Clamp(lowpass(t), 40f, 18000f);
                var a = 1f - Mathf.Exp(-Tau * fc / Rate);
                l1 += a * (Next(ref seed) - l1);
                l2 += a * (l1 - l2);
                var y = l2 * Mathf.Min(6f, Mathf.Sqrt(11000f / fc)); // a narrower band is quieter: make up for it
                if (highpass > 0f) { low += ah * (y - low); y -= low; }
                var edge = Mathf.Min(1f, (length - t) / 0.003f);
                s[first + k] += y * gain * loud(t) * edge;
            }
        }

        /// <summary>Adds sparks: sharp little clicks, many at first and fewer as they die away.</summary>
        private static void Crackle(float[] s, float at, float length, float gain, float decay, uint seed)
        {
            var first = Mathf.RoundToInt(at * Rate);
            var count = Mathf.RoundToInt(length * Rate);
            var level = 0f;
            for (var k = 0; k < count && first + k < s.Length; k++)
            {
                var t = k / (float)Rate;
                var fade = Mathf.Exp(-decay * t);
                if (Next(ref seed) * 0.5f + 0.5f < 0.012f * fade) level = Next(ref seed) > 0f ? 1f : -1f;
                s[first + k] += level * gain * fade;
                level *= 0.6f;
            }
        }

        /// <summary>A number from -1 to 1, the same run every time for the same seed (not Unity's Random: sounds must not differ between runs).</summary>
        private static float Next(ref uint seed)
        {
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            return (seed & 0xFFFFFF) / (float)0x800000 - 1f;
        }

        /// <summary>Takes the highs off the whole sound (softer, rounder).</summary>
        private static void Lowpass(float[] s, float hz)
        {
            var a = 1f - Mathf.Exp(-Tau * hz / Rate);
            var y = 0f;
            for (var i = 0; i < s.Length; i++) { y += a * (s[i] - y); s[i] = y; }
        }

        /// <summary>
        /// A short rink-style reverb: a few comb echoes mixed over the dry sound. <paramref name="mix"/> is how much
        /// wet (0 = dry only, 1 = all echo). Same input always gives the same output.
        /// </summary>
        private static void Reverb(float[] s, float mix)
        {
            mix = Mathf.Clamp01(mix);
            if (mix <= 0f) return;
            var wet = new float[s.Length];
            // prime-ish delays (ms) so they don't pile up on one pitch
            CombInto(s, wet, 0.029f, 0.78f);
            CombInto(s, wet, 0.037f, 0.72f);
            CombInto(s, wet, 0.053f, 0.65f);
            CombInto(s, wet, 0.079f, 0.55f);
            // early slap-backs (one-shot, no feedback)
            EchoInto(s, wet, 0.018f, 0.35f);
            EchoInto(s, wet, 0.041f, 0.22f);
            Lowpass(wet, 4200f); // room eats the highs
            var dry = 1f - mix;
            for (var i = 0; i < s.Length; i++) s[i] = s[i] * dry + wet[i] * mix;
        }

        private static void CombInto(float[] dry, float[] wet, float delaySec, float feedback)
        {
            var d = Mathf.Max(1, Mathf.RoundToInt(delaySec * Rate));
            var buf = new float[wet.Length];
            for (var i = 0; i < dry.Length; i++)
            {
                var x = dry[i] + (i >= d ? buf[i - d] * feedback : 0f);
                buf[i] = x;
                wet[i] += x;
            }
        }

        private static void EchoInto(float[] dry, float[] wet, float delaySec, float gain)
        {
            var d = Mathf.Max(1, Mathf.RoundToInt(delaySec * Rate));
            for (var i = d; i < wet.Length; i++) wet[i] += dry[i - d] * gain;
        }

        /// <summary>Pushes the sound into soft clipping: louder, thicker, a little gritty.</summary>
        private static void Drive(float[] s, float amount)
        {
            for (var i = 0; i < s.Length; i++) s[i] = (float)Math.Tanh(s[i] * amount);
        }

        private static void FadeOut(float[] s, float seconds)
        {
            var n = Mathf.Min(s.Length, Mathf.RoundToInt(seconds * Rate));
            for (var k = 0; k < n; k++) s[s.Length - 1 - k] *= k / (float)n;
        }

        /// <summary>Scales the sound so its loudest point is <paramref name="peak"/>.</summary>
        private static void Normalize(float[] s, float peak)
        {
            var max = 0f;
            foreach (var v in s) max = Mathf.Max(max, Mathf.Abs(v));
            if (max < 1e-6f) return;
            var k = peak / max;
            for (var i = 0; i < s.Length; i++) s[i] *= k;
        }
    }

    /// <summary>Which match song comes next.</summary>
    public static class Playlist
    {
        /// <summary>
        /// The next song out of <paramref name="count"/> (0-based), or -1 when there are none: any of them except
        /// <paramref name="last"/> (the one that just played; -1 = nothing has played yet), each as likely as the
        /// others, so the order is random but a song never plays twice in a row. With only one song there's no
        /// choice. <paramref name="roll"/> is a random number, 0 or more.
        /// </summary>
        public static int Next(int count, int last, int roll)
        {
            if (count <= 0) return -1;
            if (count == 1) return 0;
            if (roll < 0) roll = -(roll + 1);
            if (last < 0 || last >= count) return roll % count;
            var k = roll % (count - 1);
            return k >= last ? k + 1 : k;
        }
    }

    /// <summary>
    /// Plays the music and the sound effects. The music is files in Resources/UltraPuck/Music: MenuTheme plays in
    /// the menus and lobbies (stopping where it is when a match starts and carrying on from there afterwards), and
    /// every song in the Match folder is the match playlist: each match starts on a random one, and when a song ends
    /// another starts, never the same one twice in a row (see <see cref="Playlist"/>). Drop a song into either place
    /// and it's used; no code to change. Volumes come from <see cref="Settings"/>.
    /// Created by <see cref="GameRunner"/>; until then (and in tests) every call here does nothing.
    /// </summary>
    internal sealed class Sound : MonoBehaviour
    {
        private const string ThemePath = "UltraPuck/Music/MenuTheme", SongsPath = "UltraPuck/Music/Match";

        /// <summary>How loud the music is next to the effects at full volume (the effects need to cut through it).</summary>
        private const float MusicLevel = 0.6f;
        /// <summary>Seconds to fade the theme back in, a new match song in (short: it starts from its first note), and anything out.</summary>
        private const float FadeIn = 1.2f, SongIn = 0.4f, FadeOut = 0.7f;
        private const int Voices = 14;

        private static Sound _it;

        /// <summary>
        /// The pause menu (or any menu over a match) is open: play the music at half volume, unless
        /// <see cref="HearingMusic"/> is set (the Music slider is focused, so you can hear the level you're picking).
        /// </summary>
        public static bool QuietMusic;

        /// <summary>The Music slider has focus: don't quiet the music for the pause menu.</summary>
        public static bool HearingMusic;

        /// <summary>Screenshot mode: the volume to use instead of the player's own setting (0 = silent), or -1 normally.</summary>
        private float _fixedVolume = -1f;
        private float Master { get { return _fixedVolume >= 0f ? _fixedVolume : Settings.Volume; } }
        private AudioClip[] _clips;
        private AudioSource[] _voices;
        private float[] _voiceStarted;
        private float[] _lastPlayed;
        private int[] _played;

        private AudioSource _charge;
        private float _chargeLevel, _chargeWant, _chargePitch = 1f, _chargePan;
        private bool _chargeFull;
        private int _chargeFrame = -10;

        /// <summary>A piece of music. <see cref="Song"/>: one of the match playlist (played from its start each time) rather than the theme.</summary>
        private sealed class Track { public string Name; public AudioSource Source; public float Level; public bool Started, Paused, Song; public int Plays; }
        private readonly List<Track> _tracks = new List<Track>();
        private readonly List<Track> _songs = new List<Track>();
        private Track _theme, _wanted;
        private bool _matchMusic, _musicChosen;
        private int _lastSong = -1;
        /// <summary>Seconds the match song that should be playing has been silent (it has ended: time for the next).</summary>
        private float _songOver;

        /// <summary>
        /// Set up sound on <paramref name="host"/>. <paramref name="fixedVolume"/>: 0..1 to play at that volume
        /// whatever the player's setting is (the screenshot mode uses 0, so checking screens doesn't play music at
        /// whoever is at the PC: everything still runs, nothing is heard), or -1 for the player's own volume.
        /// </summary>
        public static void Create(GameObject host, float fixedVolume = -1f)
        {
            if (_it != null) return;
            _it = host.AddComponent<Sound>();
            _it._fixedVolume = fixedVolume < 0f ? -1f : Mathf.Clamp01(fixedVolume);
            _it.SetUp();
        }

        private void SetUp()
        {
            if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            AudioListener.volume = Master;

            var ids = (Sfx[])Enum.GetValues(typeof(Sfx));
            _clips = new AudioClip[ids.Length];
            _lastPlayed = new float[ids.Length];
            _played = new int[ids.Length];
            var replaced = new List<string>();
            foreach (var id in ids)
            {
                // a sound file with the effect's name wins over the one made in code
                var clip = Resources.Load<AudioClip>("UltraPuck/Sounds/" + id);
                if (clip != null) replaced.Add(id.ToString());
                else
                {
                    var samples = SoundBank.Render(id);
                    clip = AudioClip.Create(id.ToString(), samples.Length, 1, SoundBank.Rate, false);
                    clip.SetData(samples, 0);
                }
                _clips[(int)id] = clip;
                _lastPlayed[(int)id] = -10f;
            }

            _voices = new AudioSource[Voices];
            _voiceStarted = new float[Voices];
            for (var k = 0; k < Voices; k++) _voices[k] = NewSource(128);
            _charge = NewSource(100);
            _charge.clip = _clips[(int)Sfx.Charge];
            _charge.loop = true;
            _charge.volume = 0f;

            // the music: the theme, and every song in the Match folder (by name, so the list is the same on every PC)
            var theme = Resources.Load<AudioClip>(ThemePath);
            if (theme != null) _theme = NewTrack(theme, false);
            var songs = new List<AudioClip>(Resources.LoadAll<AudioClip>(SongsPath));
            songs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var clip in songs) _songs.Add(NewTrack(clip, true));
            var names = new List<string>();
            foreach (var t in _songs) names.Add(t.Name + " (" + Mathf.RoundToInt(t.Source.clip.length) + " s)");
            Debug.Log("Ultra Puck: music: " + (_theme != null ? "theme " + _theme.Name + " (" + Mathf.RoundToInt(theme.length) + " s)" : "no theme (Resources/" + ThemePath + ")") +
                "; " + (_songs.Count > 0 ? _songs.Count + " match songs: " + string.Join(", ", names.ToArray()) : "no match songs (Resources/" + SongsPath + ")"));

            Debug.Log("Ultra Puck: sound ready, " + ids.Length + " effects" + (replaced.Count > 0 ? " (from files: " + string.Join(", ", replaced.ToArray()) + ")" : "") +
                ", volume " + (Master * 100f).ToString("0.##") + "%" + (_fixedVolume >= 0f ? " (screenshot mode; the saved setting is " + Mathf.RoundToInt(Settings.Volume * 100f) + "%)" : ""));
        }

        private AudioSource NewSource(int priority)
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.spatialBlend = 0f; // plain stereo: the game is flat, nothing is "far away"
            a.dopplerLevel = 0f;
            a.priority = priority;
            return a;
        }

        // ------------------------------------------------------------------ effects

        /// <summary>
        /// Play an effect. <paramref name="pitch"/>: 1 = as made, 2 = an octave up. <paramref name="pan"/>: -1 left ..
        /// 1 right. The same effect asked for again within <paramref name="gap"/> seconds is skipped (several things
        /// happening at the same instant shouldn't stack into one loud bang).
        /// </summary>
        public static void Play(Sfx id, float volume = 1f, float pitch = 1f, float pan = 0f, float gap = 0.04f)
        {
            var s = _it;
            if (s == null || s._clips == null) return;
            var now = Time.unscaledTime;
            if (now - s._lastPlayed[(int)id] < gap) return;
            s._lastPlayed[(int)id] = now;
            s._played[(int)id]++;
            volume *= Settings.SoundVolume;
            if (volume <= 0.001f) return;

            // a free voice, or else the one that has been playing longest
            var pick = 0;
            for (var k = 0; k < s._voices.Length; k++)
            {
                if (!s._voices[k].isPlaying) { pick = k; break; }
                if (s._voiceStarted[k] < s._voiceStarted[pick]) pick = k;
            }
            var a = s._voices[pick];
            s._voiceStarted[pick] = now;
            a.Stop();
            a.clip = s._clips[(int)id];
            a.volume = Mathf.Clamp01(volume);
            a.pitch = Mathf.Clamp(pitch, 0.3f, 3f);
            a.panStereo = Mathf.Clamp(pan, -1f, 1f);
            a.Play();
        }

        /// <summary>Moving the highlight in a menu.</summary>
        public static void Move() { Play(Sfx.UiMove, 0.4f, 1f, 0f, 0.03f); }
        /// <summary>Changing a value left or right (a touch higher for more, lower for less).</summary>
        public static void Step(int direction) { Play(Sfx.UiMove, 0.4f, direction > 0 ? 1.19f : direction < 0 ? 0.84f : 1f, 0f, 0.03f); }
        /// <summary>Dragging a slider with the mouse: a tick now and then as it moves.</summary>
        public static void Drag() { Play(Sfx.UiMove, 0.4f, 1f, 0f, 0.1f); }
        /// <summary>Choosing something.</summary>
        public static void Select() { Play(Sfx.UiSelect, 0.5f); }
        /// <summary>Going back, closing, leaving.</summary>
        public static void Back() { Play(Sfx.UiBack, 0.45f); }
        /// <summary>Something that can't be done.</summary>
        public static void Deny() { Play(Sfx.UiDeny, 0.45f); }

        /// <summary>
        /// Someone is charging a shot: call every frame while they are. <paramref name="charge"/> is 0..1 (more with
        /// Over Charge): the hum climbs with it, and wavers once it's full. It stops by itself when the calls stop.
        /// </summary>
        public static void Charge(float charge, float volume, float pan)
        {
            var s = _it;
            if (s == null || s._charge == null) return;
            s._chargeFrame = Time.frameCount;
            s._chargeWant = Mathf.Lerp(0.22f, 0.5f, Mathf.Clamp01(charge)) * volume;
            s._chargePitch = Mathf.Lerp(0.8f, 2f, Mathf.Clamp01(charge)) + 0.2f * Mathf.Clamp(charge - 1f, 0f, 2f);
            s._chargeFull = charge >= 1f;
            s._chargePan = pan;
        }

        // ------------------------------------------------------------------ music

        private Track NewTrack(AudioClip clip, bool song)
        {
            var t = new Track { Name = clip.name, Source = NewSource(0), Song = song };
            t.Source.clip = clip;
            t.Source.loop = !song; // the theme goes round and round; a match song ends and another takes over
            t.Source.volume = 0f;
            _tracks.Add(t);
            return t;
        }

        /// <summary>
        /// Which music should be playing: the match playlist (<paramref name="match"/>) or the menu theme. Call it as
        /// often as you like; only a change does anything. Going into a match always starts a new song.
        /// </summary>
        public static void Music(bool match)
        {
            var s = _it;
            if (s == null || (s._musicChosen && s._matchMusic == match)) return;
            s._musicChosen = true;
            s._matchMusic = match;
            if (match) s.NextSong(false);
            else s._wanted = s._theme;
        }

        /// <summary>
        /// Put on another match song: a random one, but not the one that played last. <paramref name="straightIn"/>:
        /// at full volume from its first note (the last song has just ended), rather than faded in.
        /// </summary>
        private void NextSong(bool straightIn)
        {
            _songOver = 0f;
            var k = Playlist.Next(_songs.Count, _lastSong, UnityEngine.Random.Range(0, int.MaxValue));
            if (k < 0) { _wanted = null; return; } // no songs: a match is played without music
            var t = _songs[k];
            t.Source.Stop(); // from the top, even if it was still fading out from last time
            t.Started = false;
            t.Paused = false;
            t.Level = straightIn ? 1f : 0f;
            t.Plays++;
            _lastSong = k;
            _wanted = t;
            Debug.Log("Ultra Puck: match song: " + t.Name);
        }

        /// <summary>
        /// For checking the playlist without waiting three minutes (the screenshot mode uses it): jump the match song
        /// that's playing to <paramref name="secondsLeft"/> before its end.
        /// </summary>
        public static void SkipToSongEnd(float secondsLeft)
        {
            var s = _it;
            if (s == null || s._wanted == null || !s._wanted.Song || !s._wanted.Started) return;
            s._wanted.Source.time = Mathf.Max(0f, s._wanted.Source.clip.length - secondsLeft);
        }

        private void Update()
        {
            var dt = Time.unscaledDeltaTime;
            AudioListener.volume = Master;
            // pause music when the window isn't focused, if the player asked for that
            var hold = Settings.PauseMusicWhenUnfocused && !Application.isFocused && _fixedVolume < 0f;
            var quiet = QuietMusic && !HearingMusic ? 0.5f : 1f;

            foreach (var t in _tracks)
            {
                var want = t == _wanted ? 1f : 0f;
                t.Level = Mathf.MoveTowards(t.Level, want, dt / (want <= t.Level ? FadeOut : t.Song ? SongIn : FadeIn));
                t.Source.volume = t.Level * MusicLevel * Settings.MusicVolume * quiet;
                if (t.Level > 0f && !hold)
                {
                    if (!t.Started) { t.Source.Play(); t.Started = true; }
                    else if (t.Paused) { t.Source.UnPause(); t.Paused = false; }
                }
                else if (t.Started)
                {
                    // faded right out, or holding while unfocused. The theme waits where it is; a match song that's
                    // finished with is stopped (a hold just pauses it).
                    if (hold) { if (!t.Paused) { t.Source.Pause(); t.Paused = true; } }
                    else if (t.Song) { t.Source.Stop(); t.Started = false; }
                    else if (!t.Paused) { t.Source.Pause(); t.Paused = true; }
                }
            }

            // the match song has run out: on to the next. A moment's grace so a brief hiccup isn't a new song.
            // With "pause music when unfocused" on, wait until the window is focused again (it isn't the song ending).
            if (_wanted != null && _wanted.Song && _wanted.Started && !hold)
            {
                if (_wanted.Source.isPlaying) _songOver = 0f;
                else
                {
                    _songOver += dt;
                    if (_songOver > 0.3f) NextSong(true);
                }
            }

            // the charge hum: follows whoever asked this frame (or the last), and dies away quickly when nobody does
            var charging = Time.frameCount - _chargeFrame <= 1;
            _chargeLevel = Mathf.MoveTowards(_chargeLevel, charging ? _chargeWant : 0f, dt / 0.06f);
            if (_chargeLevel > 0.001f)
            {
                var waver = _chargeFull ? 0.78f + 0.22f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * 11f) : 1f;
                _charge.volume = _chargeLevel * waver * Settings.SoundVolume;
                _charge.pitch = _chargePitch;
                _charge.panStereo = _chargePan;
                if (!_charge.isPlaying) { _charge.Play(); _played[(int)Sfx.Charge]++; }
            }
            else if (_charge.isPlaying) _charge.Stop();
        }

        private void OnDestroy() { if (_it == this) _it = null; }

        // ------------------------------------------------------------------ checking it works

        /// <summary>What has been played so far and what the music is doing, for the log (sound can't be seen in a screenshot).</summary>
        public static string Report()
        {
            var s = _it;
            if (s == null) return "sound is off";
            var parts = new List<string>();
            foreach (Sfx id in Enum.GetValues(typeof(Sfx))) if (s._played[(int)id] > 0) parts.Add(id + " x" + s._played[(int)id]);
            var music = new List<string>();
            foreach (var t in s._tracks)
            {
                if (t.Song && t.Plays == 0) continue; // a match song that hasn't had a turn yet
                music.Add(t.Name + ": " + (t.Paused ? "paused" : t.Started ? (t.Source.isPlaying ? "playing" : "ended") : t.Song ? "stopped" : "not started") +
                    " at " + t.Source.time.ToString("0.0") + " s, level " + t.Level.ToString("0.00") + (t.Song ? ", played " + t.Plays + "x" : ""));
            }
            // how loud everything together is coming out right now (0 = silence; always 0 when the volume is 0)
            var buf = new float[2048];
            AudioListener.GetOutputData(buf, 0);
            var sum = 0f;
            foreach (var v in buf) sum += v * v;
            return "effects played: " + (parts.Count == 0 ? "none" : string.Join(", ", parts.ToArray())) + "; music wanted: " + (s._wanted != null ? s._wanted.Name : s._matchMusic ? "a match song (there are none)" : "none") +
                "; " + (music.Count == 0 ? "no tracks" : string.Join("; ", music.ToArray())) + "; coming out of the speakers: " + Mathf.Sqrt(sum / buf.Length).ToString("0.000000");
        }
    }
}
