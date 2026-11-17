using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace UltraPuck.Game
{
    /// <summary>
    /// Checks GitHub for a newer version when the game starts. If there is one, it says so, downloads it, and hands
    /// over to a small script that waits for the game to close, copies the new files over the old ones, and starts
    /// the game again. Settings are kept (they live outside the game folder). No internet, or a problem? The game
    /// just starts as normal.
    ///
    /// Releases: https://github.com/FancyDucky/UltraPuck-Standalone/releases. Each has version.txt (just the
    /// version, like 0.1.1) and UltraPuck-Windows.zip (the game in an "UltraPuck" folder). Tools/release.ps1 makes them.
    /// </summary>
    internal sealed class Updater : MonoBehaviour
    {
        private const string Repo = "https://github.com/FancyDucky/UltraPuck-Standalone";
        private const string ZipName = "UltraPuck-Windows.zip";

        /// <summary>True while an update is under way: the rest of the game ignores input.</summary>
        public static bool Busy { get; private set; }

        private string _status, _detail;
        private float _progress = -1f;
        private bool _failed;

        /// <summary>The version this copy of the game is (ULTRAPUCK_FAKE_VERSION pretends otherwise, for testing).</summary>
        public static string Current
        {
            get { return Environment.GetEnvironmentVariable("ULTRAPUCK_FAKE_VERSION") ?? Application.version; }
        }

        private void Start()
        {
            Debug.Log("Ultra Puck version " + Current);
            if (Application.isEditor || Environment.GetEnvironmentVariable("ULTRAPUCK_NOUPDATE") == "1") return;
            if (!Settings.AutoUpdate) { Debug.Log("Ultra Puck: auto-update is off (Settings > Gameplay)"); return; }
            foreach (var a in Environment.GetCommandLineArgs()) if (a == "-justupdated") return; // don't loop
            StartCoroutine(Check());
        }

        private IEnumerator Check()
        {
            string latest;
            using (var req = UnityWebRequest.Get(Repo + "/releases/latest/download/version.txt"))
            {
                req.timeout = 6;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { Debug.Log("Ultra Puck: update check skipped (" + req.error + ")"); yield break; }
                latest = req.downloadHandler.text.Trim();
            }
            if (!IsNewer(latest, Current)) { Debug.Log("Ultra Puck: up to date (" + Current + ")"); yield break; }

            Busy = true;
            _status = "Updating to version " + latest;
            _detail = "You have " + Current + ". The game will restart by itself when it's done.";
            yield return new WaitForSecondsRealtime(1.5f);

            var install = Path.GetDirectoryName(Application.dataPath);
            if (!Writable(install))
            {
                Fail("This folder can't be changed (" + install + "), so the game can't update itself. Download version " + latest + " from GitHub, or move the game to a folder like Documents.");
                yield break;
            }

            var work = Path.Combine(Path.GetTempPath(), "UltraPuckUpdate");
            try { if (Directory.Exists(work)) Directory.Delete(work, true); Directory.CreateDirectory(work); }
            catch (Exception ex) { Fail("Couldn't make a folder for the update: " + ex.Message); yield break; }

            var zip = Path.Combine(work, ZipName);
            using (var req = new UnityWebRequest(Repo + "/releases/download/v" + latest + "/" + ZipName, UnityWebRequest.kHttpVerbGET))
            {
                req.downloadHandler = new DownloadHandlerFile(zip) { removeFileOnAbort = true };
                req.timeout = 600;
                var op = req.SendWebRequest();
                while (!op.isDone) { _progress = req.downloadProgress; _detail = "Downloading... " + Mathf.RoundToInt(req.downloadProgress * 100f) + "%"; yield return null; }
                if (req.result != UnityWebRequest.Result.Success) { Fail("The download didn't work (" + req.error + "). The game will start without updating."); yield break; }
            }
            _progress = 1f;
            _detail = "Unpacking...";
            yield return null;

            string source;
            try
            {
                var unpacked = Path.Combine(work, "files");
                ZipFile.ExtractToDirectory(zip, unpacked);
                source = FindGame(unpacked);
                if (source == null) throw new IOException("the download doesn't contain the game");
            }
            catch (Exception ex) { Fail("Couldn't unpack the update: " + ex.Message); yield break; }

            _detail = "Restarting...";
            yield return null;
            try { Handover(work, source, install); }
            catch (Exception ex) { Fail("Couldn't start the update: " + ex.Message); yield break; }
            Application.Quit();
        }

        /// <summary>Writes the little script that swaps the files once the game has closed, and starts it.</summary>
        private static void Handover(string work, string source, string install)
        {
            var exe = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);
            // the same command line again (minus the first entry, the program itself), plus a note that we just updated
            var args = Environment.GetCommandLineArgs();
            var again = new System.Text.StringBuilder();
            for (var k = 1; k < args.Length; k++) again.Append('"').Append(args[k].Replace("\"", "")).Append("\" ");
            again.Append("-justupdated");
            File.WriteAllText(Path.Combine(work, "args.txt"), again.ToString());

            var script = Path.Combine(work, "apply.ps1");
            File.WriteAllText(script, string.Join("\r\n", new[]
            {
                "# Ultra Puck updater: waits for the game to close, copies the new version over it, starts it again.",
                "param([int]$GamePid, [string]$Source, [string]$Install, [string]$Exe, [string]$ArgsFile)",
                "try { Wait-Process -Id $GamePid -Timeout 60 -ErrorAction SilentlyContinue } catch { }",
                "Start-Sleep -Milliseconds 700",
                "robocopy $Source $Install /E /R:10 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null",
                "$a = Get-Content -Raw $ArgsFile",
                "Start-Process -FilePath (Join-Path $Install $Exe) -WorkingDirectory $Install -ArgumentList $a",
            }));

            var info = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"" +
                " -GamePid " + Process.GetCurrentProcess().Id +
                " -Source \"" + source + "\" -Install \"" + install + "\" -Exe \"" + exe + "\" -ArgsFile \"" + Path.Combine(work, "args.txt") + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
            Process.Start(info);
        }

        /// <summary>The folder inside the download that holds the game (the zip has an "UltraPuck" folder).</summary>
        private static string FindGame(string root)
        {
            if (File.Exists(Path.Combine(root, "UltraPuck.exe"))) return root;
            foreach (var d in Directory.GetDirectories(root))
            {
                var found = FindGame(d);
                if (found != null) return found;
            }
            return null;
        }

        private static bool Writable(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, ".update-check");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        /// <summary>"0.1.10" is newer than "0.1.9" (compared as numbers, not text).</summary>
        public static bool IsNewer(string latest, string current)
        {
            Version a, b;
            if (!Version.TryParse(latest, out a) || !Version.TryParse(current, out b)) return false;
            return a > b;
        }

        private void Fail(string why)
        {
            Debug.LogWarning("Ultra Puck: update failed: " + why);
            _failed = true;
            _status = "Couldn't update";
            _detail = why;
            StartCoroutine(Dismiss());
        }

        private IEnumerator Dismiss()
        {
            yield return new WaitForSecondsRealtime(6f);
            Busy = false;
            _status = null;
        }

        private void OnGUI()
        {
            if (_status == null) return;
            GUI.depth = -100;
            Ui.Begin();
            Ui.Box(new Rect(0f, 0f, Ui.W, Ui.H), new Color(0.04f, 0.04f, 0.045f, 0.96f), 0f);
            var w = 640f;
            var x = Ui.W / 2f - w / 2f;
            var y = Ui.H / 2f - 90f;
            Ui.Label(new Rect(x, y, w, 60f), _status, Ui.Font(38, true, TextAnchor.MiddleCenter), _failed ? new Color(1f, 0.7f, 0.6f) : Color.white);
            var style = new GUIStyle(Ui.Font(18, false, TextAnchor.UpperCenter)) { wordWrap = true };
            Ui.Label(new Rect(x, y + 70f, w, 80f), _detail ?? "", style, Ui.TextDim);
            if (_progress >= 0f && !_failed)
            {
                var bar = new Rect(x + 70f, y + 160f, w - 140f, 8f);
                Ui.Box(bar, new Color(1f, 1f, 1f, 0.12f), 4f);
                Ui.Box(new Rect(bar.x, bar.y, Mathf.Max(8f, bar.width * _progress), bar.height), Color.white, 4f);
            }
        }
    }
}
