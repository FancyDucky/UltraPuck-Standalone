using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using UltraPuck.Game;
using UnityEngine;

namespace UltraPuck.Tests
{
    /// <summary>Custom arena zips: which pictures count, which rink they're for, and that the pictures load.</summary>
    public class ArenaTests
    {
        private static readonly List<string> Maps = new List<string> { "test-rink.json", "big-rink.json", "hockey-rink.json", "s-rink.json", "u-rink.json" };
        private static string Name(string file)
        {
            switch (file) { case "big-rink.json": return "Big Rink"; case "hockey-rink.json": return "Hockey Rink"; case "s-rink.json": return "S Rink"; case "u-rink.json": return "U Rink"; default: return "Test Rink"; }
        }

        private static byte[] Png(Color c)
        {
            var t = new Texture2D(8, 4, TextureFormat.RGBA32, false);
            var px = new Color[32];
            for (var k = 0; k < px.Length; k++) px[k] = c;
            t.SetPixels(px);
            var b = t.EncodeToPNG();
            Object.DestroyImmediate(t);
            return b;
        }

        private static byte[] Zip(params KeyValuePair<string, byte[]>[] files)
        {
            using (var m = new MemoryStream())
            {
                using (var z = new ZipArchive(m, ZipArchiveMode.Create, true))
                    foreach (var f in files)
                        using (var s = z.CreateEntry(f.Key).Open()) s.Write(f.Value, 0, f.Value.Length);
                return m.ToArray();
            }
        }

        private static KeyValuePair<string, byte[]> F(string name, byte[] b) { return new KeyValuePair<string, byte[]>(name, b); }
        private static byte[] Text(string s) { return System.Text.Encoding.UTF8.GetBytes(s); }

        [Test]
        public void ArenaZipsAreReadAndMatchedToTheirRink()
        {
            // arena.json says the name and the rink (by its name here)
            var a = ArenaSkin.FromZip(Zip(F("Background.png", Png(Color.green)), F("Shadows.png", Png(Color.black)), F("arena.json", Text("{ \"name\": \"Pond\", \"rink\": \"Big Rink\" }"))), "x.zip", Maps, Name);
            Assert.AreEqual("Pond", a.Name);
            Assert.AreEqual("big-rink.json", a.Rink);
            Assert.IsNotNull(a.Back, "the background loads");
            Assert.AreEqual(8, a.Back.width);
            Assert.IsNotNull(a.Shade);
            Assert.IsNull(a.Front, "no foreground in this one");
            Assert.IsNull(a.Floor, "no floor in this one");
            a.Unload();

            // no arena.json: the rink named in the zip's file name, the name from the file name
            var b = ArenaSkin.FromZip(Zip(F("art/Foreground.PNG", Png(Color.red))), "s-rink night.zip", Maps, Name);
            Assert.AreEqual("s-rink night", b.Name);
            Assert.AreEqual("s-rink.json", b.Rink);
            Assert.IsNotNull(b.Front, "in a folder inside the zip, any case");
            var f = ArenaSkin.FromZip(Zip(F("Arena Floor.png", Png(Color.green))), "grass.zip", Maps, Name);
            Assert.IsNotNull(f, "a floor on its own is an arena");
            Assert.IsNotNull(f.Floor);
            Assert.IsNull(f.Back);
            f.Unload();

            // nothing to go on: the Hockey Rink; the template's guide picture doesn't count
            var c = ArenaSkin.FromZip(Zip(F("Background.png", Png(Color.blue)), F("guide (do not use in game).png", Png(Color.white))), "mine.zip", Maps, Name);
            Assert.AreEqual("hockey-rink.json", c.Rink);
            Assert.IsNull(ArenaSkin.FromZip(Zip(F("guide.png", Png(Color.white)), F("notes.txt", Text("hi"))), "empty.zip", Maps, Name), "no pictures: not an arena");

            // the same bytes, the same hash (it's how a downloaded one is recognised)
            Assert.AreEqual(c.Hash, ArenaSkin.FromZip(c.Bytes, "copy.zip", Maps, Name).Hash);
            Assert.AreNotEqual(c.Hash, b.Hash);

            // the sample arena.json the folder comes with works as it is
            var d = ArenaSkin.FromZip(Zip(F("Background.png", Png(Color.blue)), F("arena.json", Text(ArenaSkin.SampleJson))), "u-rink.zip", Maps, Name);
            Assert.AreEqual("My Arena", d.Name);
            Assert.AreEqual("hockey-rink.json", d.Rink, "its rink, not the one in the file name");
        }
    }
}
