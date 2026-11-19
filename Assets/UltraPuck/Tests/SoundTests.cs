using System;
using System.IO;
using NUnit.Framework;
using UltraPuck.Game;

namespace UltraPuck.Tests
{
    /// <summary>
    /// The sound effects are made in code (<see cref="SoundBank"/>). Nobody listens during the tests, so these check
    /// what can be checked from the numbers: every effect is there, has sound in it, doesn't distort, starts and ends
    /// without a click, and comes out the same every time.
    /// </summary>
    public class SoundTests
    {
        private static Sfx[] All { get { return (Sfx[])Enum.GetValues(typeof(Sfx)); } }

        [Test]
        public void EveryEffectHasSoundInItAndDoesNotDistort()
        {
            foreach (var id in All)
            {
                var s = SoundBank.Render(id);
                Assert.Greater(s.Length, SoundBank.Rate / 25, id + " is too short to hear");
                Assert.Less(s.Length, SoundBank.Rate * 3, id + " is longer than any effect should be");
                var peak = 0f;
                var sum = 0.0;
                foreach (var v in s)
                {
                    Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), id + " has a broken sample");
                    peak = Math.Max(peak, Math.Abs(v));
                    sum += v * v;
                }
                var rms = Math.Sqrt(sum / s.Length);
                Assert.LessOrEqual(peak, 0.901f, id + " would distort");
                Assert.Greater(peak, 0.5f, id + " is nearly silent");
                Assert.Greater(rms, 0.02, id + " is nearly silent");
            }
        }

        [Test]
        public void EffectsStartAndEndWithoutAClick()
        {
            foreach (var id in All)
            {
                if (SoundBank.Loops(id)) continue;
                var s = SoundBank.Render(id);
                Assert.Less(Math.Abs(s[s.Length - 1]), 0.01f, id + " stops dead (a click)");
                // the last 2 ms are much quieter than the loudest part
                var tail = 0f;
                for (var k = s.Length - SoundBank.Rate / 500; k < s.Length; k++) tail = Math.Max(tail, Math.Abs(s[k]));
                Assert.Less(tail, 0.2f, id + " is still loud when it ends");
            }
        }

        [Test]
        public void TheChargeHumJoinsUpWithItself()
        {
            // played round and round: the step from its last sample back to its first must be no bigger than any
            // step inside it, or there's a tick once a second
            var s = SoundBank.Render(Sfx.Charge);
            Assert.IsTrue(SoundBank.Loops(Sfx.Charge));
            var biggest = 0f;
            for (var k = 1; k < s.Length; k++) biggest = Math.Max(biggest, Math.Abs(s[k] - s[k - 1]));
            Assert.LessOrEqual(Math.Abs(s[0] - s[s.Length - 1]), biggest * 1.05f);
            // and it keeps humming all the way through (no fade at either end)
            for (var part = 0; part < 10; part++)
            {
                var sum = 0.0;
                for (var k = part * s.Length / 10; k < (part + 1) * s.Length / 10; k++) sum += s[k] * s[k];
                Assert.Greater(Math.Sqrt(sum / (s.Length / 10)), 0.03, "the hum drops out in part " + part);
            }
        }

        [Test]
        public void EffectsAreTheSameEveryTime()
        {
            foreach (var id in All)
            {
                var a = SoundBank.Render(id);
                var b = SoundBank.Render(id);
                Assert.AreEqual(a.Length, b.Length, id.ToString());
                for (var k = 0; k < a.Length; k++)
                    if (a[k] != b[k]) Assert.Fail(id + " differs between two renders at sample " + k);
            }
        }

        [Test]
        public void TheNextMatchSongIsRandomButNeverTheSameAgain()
        {
            Assert.AreEqual(-1, Playlist.Next(0, -1, 5), "no songs: nothing to play");
            Assert.AreEqual(0, Playlist.Next(1, -1, 5), "one song: that one");
            Assert.AreEqual(0, Playlist.Next(1, 0, 5), "one song: that one, again (there's no other)");
            var random = new Random(12345);
            for (var count = 2; count <= 6; count++)
            {
                // a long run of songs: never the same twice in a row, and every song gets its turns
                var turns = new int[count];
                var last = -1;
                for (var k = 0; k < 6000; k++)
                {
                    var next = Playlist.Next(count, last, random.Next());
                    Assert.IsTrue(next >= 0 && next < count, "song " + next + " of " + count);
                    Assert.AreNotEqual(last, next, "the same song twice in a row (of " + count + ")");
                    turns[next]++;
                    last = next;
                }
                foreach (var n in turns) Assert.Greater(n, 6000 / count / 2, "a song of " + count + " hardly ever plays");
                // after any song, each of the others can come next
                for (var after = -1; after < count; after++)
                {
                    var seen = new bool[count];
                    for (var roll = 0; roll < count * 3; roll++) seen[Playlist.Next(count, after, roll)] = true;
                    for (var song = 0; song < count; song++)
                        Assert.AreEqual(song != after, seen[song], "song " + song + " after song " + after + " (of " + count + ")");
                }
            }
        }

        /// <summary>
        /// Writes every effect as a .wav into the folder named by ULTRAPUCK_SOUNDS, to listen to or look at outside
        /// the game. Does nothing without it.
        /// </summary>
        [Test]
        public void EffectsCanBeSavedToListenTo()
        {
            var dir = Environment.GetEnvironmentVariable("ULTRAPUCK_SOUNDS");
            if (string.IsNullOrEmpty(dir)) Assert.Pass("set ULTRAPUCK_SOUNDS to a folder to save the effects there");
            Directory.CreateDirectory(dir);
            foreach (var id in All)
            {
                var s = SoundBank.Render(id);
                using (var w = new BinaryWriter(File.Create(Path.Combine(dir, id + ".wav"))))
                {
                    w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + s.Length * 2); w.Write(new[] { 'W', 'A', 'V', 'E' });
                    w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1);
                    w.Write(SoundBank.Rate); w.Write(SoundBank.Rate * 2); w.Write((short)2); w.Write((short)16);
                    w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(s.Length * 2);
                    foreach (var v in s) w.Write((short)Math.Round(Math.Max(-1f, Math.Min(1f, v)) * 32767f));
                }
            }
        }
    }
}
