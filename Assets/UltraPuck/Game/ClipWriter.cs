using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Debug = UnityEngine.Debug;

namespace UltraPuck.Game
{
    /// <summary>
    /// Writes frames (RGBA, bottom row first, as Unity reads them back) into an MP4 (H.264). On Windows that's the
    /// encoder built into Windows (Media Foundation), so nothing has to be installed; on Linux it's ffmpeg, if it's
    /// there. Frames are encoded on a thread of their own; <see cref="Add"/> waits if it falls behind.
    /// </summary>
    internal abstract class ClipWriter : IDisposable
    {
        protected readonly int W, H, Fps;
        /// <summary>The sound: samples a second and channels (0 channels: no sound).</summary>
        protected readonly int Rate, Channels;
        private struct Item { public byte[] Data; public bool Audio; }
        private readonly BlockingCollection<Item> _frames = new BlockingCollection<Item>(12);
        private Thread _thread;
        protected volatile string Failed;

        protected ClipWriter(int w, int h, int fps, int rate, int channels) { W = w; H = h; Fps = fps; Rate = rate; Channels = channels; }

        /// <summary>Whether the file gets a sound track (set once it has started).</summary>
        public bool HasSound { get; protected set; }

        /// <summary>A writer for <paramref name="path"/>, or null (and why in <paramref name="problem"/>).</summary>
        /// <param name="rate">Sound: samples a second (0: no sound).</param>
        public static ClipWriter Open(string path, int w, int h, int fps, int rate, int channels, out string problem)
        {
            problem = null;
            ClipWriter cw = null;
            try
            {
                if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsPlayer || UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor)
                    cw = new MediaFoundationWriter(path, w, h, fps, rate, channels);
                else cw = new FfmpegWriter(path, w, h, fps);
                cw.Start();
                return cw;
            }
            catch (Exception e)
            {
                problem = e.Message;
                if (cw != null) cw.Dispose();
                return null;
            }
        }

        private void Start()
        {
            var ready = new ManualResetEvent(false);
            _thread = new Thread(() =>
            {
                try { Begin(); }
                catch (Exception e) { Failed = e.Message; }
                ready.Set();
                if (Failed != null) return;
                try
                {
                    foreach (var f in _frames.GetConsumingEnumerable())
                    {
                        if (Failed != null) continue;
                        if (f.Audio) WriteSound(f.Data); else Write(f.Data);
                    }
                }
                catch (Exception e) { Failed = e.Message; }
                try { End(); }
                catch (Exception e) { if (Failed == null) Failed = e.Message; }
            }) { IsBackground = true, Name = "Ultra Puck clip" };
            _thread.Start();
            ready.WaitOne(15000);
            if (Failed != null) throw new IOException(Failed);
        }

        /// <summary>The next frame: W x H x 4 bytes, RGBA, bottom row first.</summary>
        public bool Add(byte[] rgba)
        {
            if (Failed != null) return false;
            _frames.Add(new Item { Data = rgba });
            return true;
        }

        /// <summary>The sound to go with the frames so far: 16-bit samples, channels interleaved.</summary>
        public void AddSound(byte[] pcm)
        {
            if (Failed != null || !HasSound || pcm == null || pcm.Length == 0) return;
            _frames.Add(new Item { Data = pcm, Audio = true });
        }

        /// <summary>Finish the file. Returns null, or what went wrong.</summary>
        public string Finish()
        {
            _frames.CompleteAdding();
            if (_thread != null) _thread.Join(60000);
            return Failed;
        }

        public void Dispose()
        {
            if (!_frames.IsAddingCompleted) _frames.CompleteAdding();
        }

        protected abstract void Begin();
        protected abstract void Write(byte[] rgba);
        protected virtual void WriteSound(byte[] pcm) { }
        protected abstract void End();

        // ------------------------------------------------------------------ Windows: Media Foundation

        private sealed class MediaFoundationWriter : ClipWriter
        {
            private readonly string _path;
            private IntPtr _writer;
            private uint _stream;
            private long _frame;

            private uint _audio;
            private long _samples;

            public MediaFoundationWriter(string path, int w, int h, int fps, int rate, int channels) : base(w, h, fps, rate, channels) { _path = path; }

            [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint coInit);
            [DllImport("mfplat.dll")] private static extern int MFStartup(uint version, uint flags);
            [DllImport("mfplat.dll")] private static extern int MFShutdown();
            [DllImport("mfplat.dll")] private static extern int MFCreateMediaType(out IntPtr type);
            [DllImport("mfplat.dll")] private static extern int MFCreateSample(out IntPtr sample);
            [DllImport("mfplat.dll")] private static extern int MFCreateMemoryBuffer(uint max, out IntPtr buffer);
            [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] private static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attributes, out IntPtr writer);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint FnRelease(IntPtr self);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnSetGuid(IntPtr self, ref Guid key, ref Guid value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnSetU32(IntPtr self, ref Guid key, uint value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnSetU64(IntPtr self, ref Guid key, ulong value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnAddStream(IntPtr self, IntPtr type, out uint index);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnSetInput(IntPtr self, uint index, IntPtr type, IntPtr attributes);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnPlain(IntPtr self);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnWriteSample(IntPtr self, uint index, IntPtr sample);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnLock(IntPtr self, out IntPtr data, out uint max, out uint current);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnSetLength(IntPtr self, uint length);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnAddBuffer(IntPtr self, IntPtr buffer);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnSetTime(IntPtr self, long time);

            // slots in the COM tables (IUnknown 0-2; IMFAttributes 3-32; IMFSample after that)
            private const int Release = 2, SetUINT32 = 21, SetUINT64 = 22, SetGUID = 24;
            private const int AddStream = 3, SetInputMediaType = 4, BeginWriting = 5, WriteSample = 6, FinalizeWriting = 11;
            private const int SetSampleTime = 36, SetSampleDuration = 38, AddBuffer = 42;
            private const int Lock = 3, Unlock = 4, SetCurrentLength = 6;

            private static T Fn<T>(IntPtr obj, int slot) where T : class
            {
                var table = Marshal.ReadIntPtr(obj);
                return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table, slot * IntPtr.Size), typeof(T)) as T;
            }

            private static void Check(int hr, string what) { if (hr < 0) throw new IOException("Windows couldn't make the video (" + what + ", 0x" + hr.ToString("X8") + ")."); }
            private static void Free(IntPtr p) { if (p != IntPtr.Zero) Fn<FnRelease>(p, Release)(p); }

            private static Guid G(string s) { return new Guid(s); }
            private static readonly Guid MajorType = G("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), Video = G("73646976-0000-0010-8000-00AA00389B71");
            private static readonly Guid SubType = G("f7e34c9a-42e8-4714-b74b-cb29d72c35e5"), H264 = G("34363248-0000-0010-8000-00AA00389B71"), Rgb32 = G("00000016-0000-0010-8000-00AA00389B71");
            private static readonly Guid Bitrate = G("20332624-fb0d-4d9e-bd0d-cbf6786c102e"), Interlace = G("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
            private static readonly Guid FrameSize = G("1652c33d-d6b2-4012-b834-72030849a37d"), FrameRate = G("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
            private static readonly Guid PixelAspect = G("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
            private static readonly Guid Profile = G("ad76a80b-2d5c-4e0b-b375-64e520137036");
            private static readonly Guid Audio = G("73647561-0000-0010-8000-00AA00389B71"), Aac = G("00001610-0000-0010-8000-00AA00389B71"), Pcm = G("00000001-0000-0010-8000-00AA00389B71");
            private static readonly Guid Bits = G("f2deb57f-40fa-4764-aa33-ed4f2d1ff669"), SampleRate = G("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
            private static readonly Guid NumChannels = G("37e48bf5-645e-4c5b-89de-ada9e29b696a"), BytesPerSecond = G("1aab75c8-cfef-451c-ab95-ac034b8e1731");
            private static readonly Guid BlockAlign = G("322de230-9eeb-43bd-ab7a-ff412251541d");

            private void Describe(IntPtr type, Guid subtype, bool output)
            {
                var k = MajorType; var v = Video;
                Check(Fn<FnSetGuid>(type, SetGUID)(type, ref k, ref v), "type");
                k = SubType; v = subtype;
                Check(Fn<FnSetGuid>(type, SetGUID)(type, ref k, ref v), "format");
                if (output)
                {
                    // plenty of bits for fast movement on flat colours (about 31 Mbit/s at 1080p60), and the High profile
                    k = Bitrate; Check(Fn<FnSetU32>(type, SetUINT32)(type, ref k, (uint)Math.Min(60000000L, (long)W * H * Fps / 4)), "bitrate");
                    k = Profile; Check(Fn<FnSetU32>(type, SetUINT32)(type, ref k, 100u), "profile");
                }
                k = Interlace; Check(Fn<FnSetU32>(type, SetUINT32)(type, ref k, 2u), "progressive");
                k = FrameSize; Check(Fn<FnSetU64>(type, SetUINT64)(type, ref k, ((ulong)(uint)W << 32) | (uint)H), "size");
                k = FrameRate; Check(Fn<FnSetU64>(type, SetUINT64)(type, ref k, ((ulong)(uint)Fps << 32) | 1u), "frame rate");
                k = PixelAspect; Check(Fn<FnSetU64>(type, SetUINT64)(type, ref k, (1ul << 32) | 1u), "pixel shape");
            }

            protected override void Begin()
            {
                CoInitializeEx(IntPtr.Zero, 0); // (already set up on this thread is fine too)
                Check(MFStartup(0x00020070, 0), "start");
                if (File.Exists(_path)) File.Delete(_path);
                Check(MFCreateSinkWriterFromURL(_path, IntPtr.Zero, IntPtr.Zero, out _writer), "file");
                IntPtr outType, inType;
                Check(MFCreateMediaType(out outType), "output");
                try
                {
                    Describe(outType, H264, true);
                    Check(Fn<FnAddStream>(_writer, AddStream)(_writer, outType, out _stream), "stream");
                }
                finally { Free(outType); }
                Check(MFCreateMediaType(out inType), "input");
                try
                {
                    Describe(inType, Rgb32, false);
                    Check(Fn<FnSetInput>(_writer, SetInputMediaType)(_writer, _stream, inType, IntPtr.Zero), "input format");
                }
                finally { Free(inType); }
                AddSoundStream();
                Check(Fn<FnPlain>(_writer, BeginWriting)(_writer), "begin");
            }

            /// <summary>An AAC sound track (Windows' own encoder takes 44.1 or 48 kHz, mono or stereo). Without it, a silent clip.</summary>
            private void AddSoundStream()
            {
                if (Channels < 1 || Channels > 2 || (Rate != 44100 && Rate != 48000)) { Debug.Log("Ultra Puck: clip without sound (" + Rate + " Hz, " + Channels + " channels)"); return; }
                IntPtr outType = IntPtr.Zero, inType = IntPtr.Zero;
                try
                {
                    Check(MFCreateMediaType(out outType), "sound output");
                    var k = MajorType; var v = Audio;
                    Check(Fn<FnSetGuid>(outType, SetGUID)(outType, ref k, ref v), "sound");
                    k = SubType; v = Aac;
                    Check(Fn<FnSetGuid>(outType, SetGUID)(outType, ref k, ref v), "AAC");
                    k = Bits; Check(Fn<FnSetU32>(outType, SetUINT32)(outType, ref k, 16u), "bits");
                    k = SampleRate; Check(Fn<FnSetU32>(outType, SetUINT32)(outType, ref k, (uint)Rate), "rate");
                    k = NumChannels; Check(Fn<FnSetU32>(outType, SetUINT32)(outType, ref k, (uint)Channels), "channels");
                    k = BytesPerSecond; Check(Fn<FnSetU32>(outType, SetUINT32)(outType, ref k, 24000u), "sound bitrate"); // 192 kbit/s
                    Check(Fn<FnAddStream>(_writer, AddStream)(_writer, outType, out _audio), "sound stream");

                    Check(MFCreateMediaType(out inType), "sound input");
                    k = MajorType; v = Audio;
                    Check(Fn<FnSetGuid>(inType, SetGUID)(inType, ref k, ref v), "sound");
                    k = SubType; v = Pcm;
                    Check(Fn<FnSetGuid>(inType, SetGUID)(inType, ref k, ref v), "PCM");
                    k = Bits; Check(Fn<FnSetU32>(inType, SetUINT32)(inType, ref k, 16u), "bits");
                    k = SampleRate; Check(Fn<FnSetU32>(inType, SetUINT32)(inType, ref k, (uint)Rate), "rate");
                    k = NumChannels; Check(Fn<FnSetU32>(inType, SetUINT32)(inType, ref k, (uint)Channels), "channels");
                    k = BlockAlign; Check(Fn<FnSetU32>(inType, SetUINT32)(inType, ref k, (uint)(Channels * 2)), "block");
                    k = BytesPerSecond; Check(Fn<FnSetU32>(inType, SetUINT32)(inType, ref k, (uint)(Rate * Channels * 2)), "bytes");
                    Check(Fn<FnSetInput>(_writer, SetInputMediaType)(_writer, _audio, inType, IntPtr.Zero), "sound input format");
                    HasSound = true;
                }
                catch (Exception e) { Debug.LogWarning("Ultra Puck: clip without sound: " + e.Message); HasSound = false; }
                finally { Free(outType); Free(inType); }
            }

            protected override void WriteSound(byte[] pcm)
            {
                IntPtr buffer, sample = IntPtr.Zero;
                Check(MFCreateMemoryBuffer((uint)pcm.Length, out buffer), "sound buffer");
                try
                {
                    IntPtr data; uint max, cur;
                    Check(Fn<FnLock>(buffer, Lock)(buffer, out data, out max, out cur), "sound lock");
                    Marshal.Copy(pcm, 0, data, pcm.Length);
                    Fn<FnPlain>(buffer, Unlock)(buffer);
                    Check(Fn<FnSetLength>(buffer, SetCurrentLength)(buffer, (uint)pcm.Length), "sound length");
                    Check(MFCreateSample(out sample), "sound sample");
                    Check(Fn<FnAddBuffer>(sample, AddBuffer)(sample, buffer), "sound add");
                    var n = pcm.Length / (2 * Channels);
                    Check(Fn<FnSetTime>(sample, SetSampleTime)(sample, _samples * 10000000L / Rate), "sound time");
                    Check(Fn<FnSetTime>(sample, SetSampleDuration)(sample, (long)n * 10000000L / Rate), "sound duration");
                    Check(Fn<FnWriteSample>(_writer, WriteSample)(_writer, _audio, sample), "sound");
                    _samples += n;
                }
                finally { Free(sample); Free(buffer); }
            }

            protected override void Write(byte[] rgba)
            {
                var size = (uint)(W * H * 4);
                IntPtr buffer, sample = IntPtr.Zero;
                Check(MFCreateMemoryBuffer(size, out buffer), "buffer");
                try
                {
                    IntPtr data; uint max, cur;
                    Check(Fn<FnLock>(buffer, Lock)(buffer, out data, out max, out cur), "lock");
                    // RGBA to the BGRA Windows wants (both bottom row first)
                    var bgra = new byte[size];
                    for (var i = 0; i < size; i += 4) { bgra[i] = rgba[i + 2]; bgra[i + 1] = rgba[i + 1]; bgra[i + 2] = rgba[i]; bgra[i + 3] = 255; }
                    Marshal.Copy(bgra, 0, data, (int)size);
                    Fn<FnPlain>(buffer, Unlock)(buffer);
                    Check(Fn<FnSetLength>(buffer, SetCurrentLength)(buffer, size), "length");
                    Check(MFCreateSample(out sample), "sample");
                    Check(Fn<FnAddBuffer>(sample, AddBuffer)(sample, buffer), "add");
                    var step = 10000000L / Fps;
                    Check(Fn<FnSetTime>(sample, SetSampleTime)(sample, _frame * 10000000L / Fps), "time");
                    Check(Fn<FnSetTime>(sample, SetSampleDuration)(sample, step), "duration");
                    Check(Fn<FnWriteSample>(_writer, WriteSample)(_writer, _stream, sample), "frame");
                    _frame++;
                }
                finally { Free(sample); Free(buffer); }
            }

            protected override void End()
            {
                try { if (_writer != IntPtr.Zero && Failed == null) Check(Fn<FnPlain>(_writer, FinalizeWriting)(_writer), "finish"); }
                finally { Free(_writer); _writer = IntPtr.Zero; MFShutdown(); }
            }
        }

        // ------------------------------------------------------------------ Linux (Steam Deck): ffmpeg

        private sealed class FfmpegWriter : ClipWriter
        {
            private readonly string _path;
            private Process _p;
            private Stream _in;

            public FfmpegWriter(string path, int w, int h, int fps) : base(w, h, fps, 0, 0) { _path = path; }

            protected override void Begin()
            {
                var args = "-y -loglevel error -f rawvideo -pix_fmt rgba -s " + W + "x" + H + " -r " + Fps + " -i - -vf vflip -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -movflags +faststart \"" + _path + "\"";
                try
                {
                    _p = Process.Start(new ProcessStartInfo("ffmpeg", args) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true });
                }
                catch (Exception) { throw new IOException("Saving clips here needs ffmpeg (install it from your software store or package manager)."); }
                if (_p == null) throw new IOException("Couldn't start ffmpeg.");
                _in = _p.StandardInput.BaseStream;
            }

            protected override void Write(byte[] rgba) { _in.Write(rgba, 0, rgba.Length); }

            protected override void End()
            {
                if (_in != null) _in.Close();
                if (_p == null) return;
                var err = _p.StandardError.ReadToEnd();
                _p.WaitForExit(60000);
                if (_p.ExitCode != 0 && Failed == null) Failed = "ffmpeg: " + err.Trim();
            }
        }

        // ------------------------------------------------------------------ the clipboard (see below), and the sound

        /// <summary>Put a file on the clipboard (to paste into a chat or a folder). Returns false if it couldn't.</summary>
        public static bool CopyFile(string path)
        {
            try
            {
                if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsPlayer || UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor)
                {
                    var p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Sta -Command \"Set-Clipboard -LiteralPath '" + path.Replace("'", "''") + "'\"")
                        { UseShellExecute = false, CreateNoWindow = true });
                    if (p == null) return false;
                    p.WaitForExit(10000);
                    return p.ExitCode == 0;
                }
                // Linux: as a file to paste (X or Wayland), whichever tool is there
                var uri = new Uri(path).AbsoluteUri;
                foreach (var tool in new[] { "wl-copy|--type text/uri-list", "xclip|-selection clipboard -t text/uri-list" })
                {
                    var bits = tool.Split('|');
                    try
                    {
                        var p = Process.Start(new ProcessStartInfo(bits[0], bits[1]) { UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true });
                        if (p == null) continue;
                        p.StandardInput.Write(uri + "\n");
                        p.StandardInput.Close();
                        p.WaitForExit(5000);
                        if (!p.HasExited || p.ExitCode == 0) return true;
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception e) { Debug.LogWarning("Ultra Puck: couldn't copy the clip: " + e.Message); }
            return false;
        }
    }

    /// <summary>
    /// A saved clip's sound track, mixed here a video frame at a time from the effects the game played meanwhile
    /// (<see cref="Sound.Heard"/>): each one from the start of the frame it was played in, at its volume, pitch and
    /// pan. Unity can't record its own output in step with frames that take longer than they last.
    /// </summary>
    internal sealed class ClipMixer : IDisposable
    {
        private MusicReader _music;
        private string _musicSong;
        private float _musicNext; // where the reader is in the song (seconds)

        public void Dispose() { if (_music != null) _music.Dispose(); _music = null; }

        /// <summary>The song's file for clips (StreamingAssets/ClipMusic, put there by the build), or null.</summary>
        private static string SongFile(string song)
        {
            if (string.IsNullOrEmpty(song)) return null;
            var dir = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, "ClipMusic");
            foreach (var ext in new[] { ".mp3", ".wav", ".m4a" })
            {
                var f = System.IO.Path.Combine(dir, song + ext);
                if (System.IO.File.Exists(f)) return f;
            }
            return null;
        }

        /// <summary>This frame's worth of the song (stereo, 48 kHz), added into <paramref name="l"/> / <paramref name="r"/>.</summary>
        private void MixMusic(string song, float at, float gain, float[] l, float[] r, int n)
        {
            if (string.IsNullOrEmpty(song) || gain <= 0.001f) { _musicSong = null; return; }
            // a different song, or a jump in it (the music paused, or a new song started): start reading again there
            if (song != _musicSong || Math.Abs(at - _musicNext) > 0.25f)
            {
                if (_music != null) _music.Dispose();
                _music = null;
                _musicSong = song;
                var file = SongFile(song);
                if (file != null)
                {
                    try { _music = new MusicReader(file, at); }
                    catch (Exception e) { Debug.LogWarning("Ultra Puck: clip music " + song + ": " + e.Message); }
                }
                else Debug.LogWarning("Ultra Puck: clip music: no file for " + song + " in StreamingAssets/ClipMusic");
            }
            _musicNext = at + n / (float)Rate;
            if (_music == null) return;
            _music.Read(l, r, n, gain);
        }
        public const int Rate = 48000, Channels = 2;
        private sealed class Voice { public float[] Data; public double Pos, Step; public float L, R; }
        private readonly List<Voice> _voices = new List<Voice>();
        private readonly Dictionary<UnityEngine.AudioClip, float[]> _data = new Dictionary<UnityEngine.AudioClip, float[]>();

        private double _owed;
        private readonly double _framesPerVideo;

        public ClipMixer(int fps) { _framesPerVideo = Rate / (double)fps; }

        private float[] DataOf(UnityEngine.AudioClip clip)
        {
            float[] d;
            if (_data.TryGetValue(clip, out d)) return d;
            d = null;
            try
            {
                // (mono: the game's own effects are; anything else is folded down to it)
                var all = new float[clip.samples * clip.channels];
                if (clip.GetData(all, 0))
                {
                    d = new float[clip.samples];
                    for (var i = 0; i < clip.samples; i++)
                    {
                        var v = 0f;
                        for (var c = 0; c < clip.channels; c++) v += all[i * clip.channels + c];
                        d[i] = v / clip.channels;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("Ultra Puck: clip sound " + clip.name + ": " + e.Message); }
            _data[clip] = d;
            return d;
        }

        /// <summary>
        /// The next video frame's worth of sound (16-bit, stereo): the effects heard since the last, and song
        /// <paramref name="song"/> ("" none) from <paramref name="songAt"/> seconds in, at <paramref name="musicGain"/>.
        /// </summary>
        public byte[] Frame(string song = "", float songAt = 0f, float musicGain = 0f)
        {
            foreach (var h in Sound.Heard)
            {
                if (h.Clip == null) continue;
                var d = DataOf(h.Clip);
                if (d == null) continue;
                // equal-power pan, as loud as Unity plays it in the middle
                var angle = (h.Pan + 1f) * UnityEngine.Mathf.PI / 4f;
                _voices.Add(new Voice
                {
                    Data = d, Pos = 0, Step = h.Clip.frequency * h.Pitch / (double)Rate,
                    L = h.Volume * UnityEngine.Mathf.Cos(angle) * 1.41421f, R = h.Volume * UnityEngine.Mathf.Sin(angle) * 1.41421f,
                });
            }
            Sound.Heard.Clear();
            _owed += _framesPerVideo;
            var n = (int)_owed;
            _owed -= n;
            var pcm = new byte[n * Channels * 2];
            var ml = new float[n]; var mr = new float[n];
            MixMusic(song, songAt, musicGain, ml, mr, n);
            for (var i = 0; i < n; i++)
            {
                float l = ml[i], r = mr[i];
                for (var k = _voices.Count - 1; k >= 0; k--)
                {
                    var v = _voices[k];
                    var at = (int)v.Pos;
                    if (at >= v.Data.Length - 1) { _voices.RemoveAt(k); continue; }
                    var f = (float)(v.Pos - at);
                    var s = v.Data[at] + (v.Data[at + 1] - v.Data[at]) * f;
                    l += s * v.L; r += s * v.R;
                    v.Pos += v.Step;
                }
                Put(pcm, i * 4, l);
                Put(pcm, i * 4 + 2, r);
            }
            return pcm;
        }

        /// <summary>A sample into 16 bits, gently rounded off near the top instead of clipping hard.</summary>
        private static void Put(byte[] pcm, int at, float v)
        {
            if (v > 0.8f) v = 0.8f + 0.2f * (float)Math.Tanh((v - 0.8f) / 0.2f);
            else if (v < -0.8f) v = -0.8f + 0.2f * (float)Math.Tanh((v + 0.8f) / 0.2f);
            var s = (short)UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(v * 32767f), -32768, 32767);
            pcm[at] = (byte)s; pcm[at + 1] = (byte)(s >> 8);
        }
    }

    /// <summary>
    /// A song file read from a point onwards as 48 kHz stereo, through Windows' own decoders (Media Foundation's
    /// source reader: MP3, AAC, WAV). Windows only; it throws anywhere else (clips are then made without music).
    /// </summary>
    internal sealed class MusicReader : IDisposable
    {
        [DllImport("mfplat.dll")] private static extern int MFStartup(uint version, uint flags);
        [DllImport("mfplat.dll")] private static extern int MFShutdown();
        [DllImport("mfplat.dll")] private static extern int MFCreateMediaType(out IntPtr type);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] private static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IntPtr reader);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint FnRelease(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnSetGuid(IntPtr self, ref Guid key, ref Guid value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnGetU32(IntPtr self, ref Guid key, out uint value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnSelect(IntPtr self, uint stream, bool on);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnGetType(IntPtr self, uint stream, out IntPtr type);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnSetType(IntPtr self, uint stream, IntPtr reserved, IntPtr type);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnSeek(IntPtr self, ref Guid format, ref PropVariant at);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnRead(IntPtr self, uint stream, uint control, out uint actual, out uint flags, out long time, out IntPtr sample);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnContiguous(IntPtr self, out IntPtr buffer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnLock(IntPtr self, out IntPtr data, out uint max, out uint current);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnPlain(IntPtr self);

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct PropVariant { [FieldOffset(0)] public ushort Vt; [FieldOffset(8)] public long Value; }

        private const uint FirstAudio = 0xFFFFFFFD, AllStreams = 0xFFFFFFFE, EndOfStream = 0x2;
        private static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), Audio = new Guid("73647561-0000-0010-8000-00AA00389B71");
        private static readonly Guid SubType = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5"), Float = new Guid("00000003-0000-0010-8000-00AA00389B71");
        private static readonly Guid SampleRate = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba"), NumChannels = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");

        private static T Fn<T>(IntPtr obj, int slot) where T : class
        {
            var table = Marshal.ReadIntPtr(obj);
            return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table, slot * IntPtr.Size), typeof(T)) as T;
        }
        private static void Check(int hr, string what) { if (hr < 0) throw new IOException("can't read the song (" + what + ", 0x" + hr.ToString("X8") + ")"); }
        private static void Free(IntPtr p) { if (p != IntPtr.Zero) Fn<FnRelease>(p, 2)(p); }

        private IntPtr _reader;
        private readonly int _rate, _channels;
        private bool _started, _ended;
        // decoded and not yet used (stereo pairs at the song's own rate), and where between them the next sample falls
        private readonly List<float> _l = new List<float>(), _r = new List<float>();
        private double _pos;
        private long _skip; // frames to drop at the start (the decoder starts a little before the point asked for)

        public MusicReader(string file, float seconds)
        {
            if (UnityEngine.Application.platform != UnityEngine.RuntimePlatform.WindowsPlayer && UnityEngine.Application.platform != UnityEngine.RuntimePlatform.WindowsEditor)
                throw new IOException("music in clips is Windows only");
            Check(MFStartup(0x00020070, 0), "start");
            _started = true;
            Check(MFCreateSourceReaderFromURL(file, IntPtr.Zero, out _reader), "open");
            Fn<FnSelect>(_reader, 4)(_reader, AllStreams, false);
            Check(Fn<FnSelect>(_reader, 4)(_reader, FirstAudio, true), "select");
            IntPtr want;
            Check(MFCreateMediaType(out want), "type");
            try
            {
                var k = MajorType; var v = Audio;
                Check(Fn<FnSetGuid>(want, 24)(want, ref k, ref v), "audio");
                k = SubType; v = Float;
                Check(Fn<FnSetGuid>(want, 24)(want, ref k, ref v), "float");
                Check(Fn<FnSetType>(_reader, 7)(_reader, FirstAudio, IntPtr.Zero, want), "set type");
            }
            finally { Free(want); }
            IntPtr got;
            Check(Fn<FnGetType>(_reader, 6)(_reader, FirstAudio, out got), "get type");
            try
            {
                uint rate, ch;
                var k = SampleRate; Check(Fn<FnGetU32>(got, 7)(got, ref k, out rate), "rate");
                k = NumChannels; Check(Fn<FnGetU32>(got, 7)(got, ref k, out ch), "channels");
                _rate = (int)rate; _channels = Math.Max(1, (int)ch);
            }
            finally { Free(got); }
            var at = new PropVariant { Vt = 20, Value = (long)(Math.Max(0f, seconds) * 10000000.0) }; // VT_I8, 100 ns units
            var none = Guid.Empty;
            Check(Fn<FnSeek>(_reader, 8)(_reader, ref none, ref at), "seek");
            _skip = -1;
            _wanted = (long)(Math.Max(0f, seconds) * _rate);
        }

        private readonly long _wanted;

        /// <summary>Decode the next piece into the queue. False at the end of the song.</summary>
        private bool Decode()
        {
            if (_ended) return false;
            uint actual, flags; long time; IntPtr sample;
            Check(Fn<FnRead>(_reader, 9)(_reader, FirstAudio, 0, out actual, out flags, out time, out sample), "read");
            if ((flags & EndOfStream) != 0) _ended = true;
            if (sample == IntPtr.Zero) return !_ended;
            IntPtr buffer = IntPtr.Zero;
            try
            {
                Check(Fn<FnContiguous>(sample, 41)(sample, out buffer), "buffer");
                IntPtr data; uint max, cur;
                Check(Fn<FnLock>(buffer, 3)(buffer, out data, out max, out cur), "lock");
                var floats = new float[cur / 4];
                Marshal.Copy(data, floats, 0, floats.Length);
                Fn<FnPlain>(buffer, 4)(buffer);
                // the first piece after the seek starts before the point asked for: drop what's before it
                if (_skip < 0) _skip = Math.Max(0L, _wanted - (long)(time / 10000000.0 * _rate));
                for (var i = 0; i + _channels - 1 < floats.Length; i += _channels)
                {
                    if (_skip > 0) { _skip--; continue; }
                    var left = floats[i];
                    var right = _channels > 1 ? floats[i + 1] : left;
                    _l.Add(left); _r.Add(right);
                }
            }
            finally { Free(buffer); Free(sample); }
            return true;
        }

        /// <summary>Add the next <paramref name="n"/> samples (48 kHz) times <paramref name="gain"/> into <paramref name="l"/> / <paramref name="r"/>.</summary>
        public void Read(float[] l, float[] r, int n, float gain)
        {
            var step = _rate / (double)ClipMixer.Rate;
            for (var i = 0; i < n; i++)
            {
                while ((int)_pos + 1 >= _l.Count) if (!Decode()) return;
                var at = (int)_pos;
                var f = (float)(_pos - at);
                l[i] += (_l[at] + (_l[at + 1] - _l[at]) * f) * gain;
                r[i] += (_r[at] + (_r[at + 1] - _r[at]) * f) * gain;
                _pos += step;
            }
            // forget what's been used
            var used = (int)_pos;
            if (used > 4096) { _l.RemoveRange(0, used); _r.RemoveRange(0, used); _pos -= used; }
        }

        public void Dispose()
        {
            Free(_reader); _reader = IntPtr.Zero;
            if (_started) { MFShutdown(); _started = false; }
        }
    }
}
