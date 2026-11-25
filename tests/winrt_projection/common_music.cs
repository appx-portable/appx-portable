// Exercise the shipped CommonDX music path without launching its game.
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Xna.Framework.Media;
using SharpDX.MediaFoundation;

internal static class CommonMusicTest
{
    [DllImport("combase.dll")] private static extern int RoInitialize(uint flags);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static string CurrentSource(MediaEngine engine)
    {
        IntPtr output = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(output, IntPtr.Zero);
            engine.GetCurrentSource(output);
            IntPtr text = Marshal.ReadIntPtr(output);
            try { return text == IntPtr.Zero ? "" : Marshal.PtrToStringBSTR(text); }
            finally { if (text != IntPtr.Zero) Marshal.FreeBSTR(text); }
        }
        finally { Marshal.FreeHGlobal(output); }
    }
    private static void WaitForPlayback(MediaEngine engine, string label)
    {
        double baseline = double.NaN;
        for (int count = 0; count < 150; ++count)
        {
            Thread.Sleep(200);
            Check(engine.Error == null, label + " decode/playback error");
            if (count % 25 == 0) Console.WriteLine("music: " + label + " ready=" + engine.ReadyState + " time=" + engine.CurrentTime + " duration=" + engine.Duration);
            if ((int)engine.ReadyState >= 2 && engine.HasAudio())
            {
                double current = engine.CurrentTime;
                if (double.IsNaN(baseline) || current < baseline) baseline = current;
                if (current >= baseline + 1.0) return;
            }
        }
        throw new Exception(label + " has no advancing audio: time=" + engine.CurrentTime + " duration=" + engine.Duration + " source=" + CurrentSource(engine));
    }
    private static int Main(string[] args)
    {
        MediaEngine engine = null;
        bool shutdown = false;
        try
        {
            Console.WriteLine("music: initialize COM");
            Marshal.ThrowExceptionForHR(RoInitialize(1));
            Console.WriteLine("music: initialize original MediaPlayer");
            Check(MediaPlayer.GameHasControl, "music control");
            engine = (MediaEngine)typeof(MediaPlayer).GetField("_mediaEngineEx", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            MediaPlayer.Volume = 0.25f;
            // Original Play calls SetSource, Load, and Play. Load used to throw
            // E_NOTIMPL under Wine before playback could be reached.
            Console.WriteLine("music: play first track");
            if (args.Length > 2 && args[2] == "delayed")
            {
                // Resolve before Play to cover preload timing separately from
                // the following uninterrupted source-change regression.
                engine.Source = new Uri(System.IO.Path.GetFullPath(args[0])).AbsoluteUri;
                engine.Load();
                for (int count = 0; (int)engine.ReadyState < 2 && count < 150; ++count) Thread.Sleep(200);
                Check((int)engine.ReadyState >= 2 && engine.HasAudio(), "audio-only preload resolves");
                engine.Play();
            }
            else MediaPlayer.Play(new Song(args[0]));
            WaitForPlayback(engine, "first track");
            Console.WriteLine("music: first track advances");
            if (args.Length > 1 && args[1].Length != 0)
            {
                string previous = CurrentSource(engine);
                // The game's menu-to-level path replaces the song without an
                // intervening Pause/Stop, then enables looping.
                Console.WriteLine("music: replace source with level track");
                MediaPlayer.Play(new Song(args[1]));
                MediaPlayer.IsRepeating = true;
                Check(CurrentSource(engine) != previous, "track source changes");
                WaitForPlayback(engine, "level track after source change");
            }
            MediaPlayer.Pause();
            Thread.Sleep(250);
            double paused = engine.CurrentTime;
            Thread.Sleep(300);
            Check(Math.Abs(engine.CurrentTime - paused) < 0.15, "pause stops music clock");
            MediaPlayer.Resume();
            for (int count = 0; engine.CurrentTime <= paused + 0.2 && count < 50; ++count) Thread.Sleep(100);
            Check(engine.CurrentTime > paused + 0.2, "resume advances music clock: before=" + paused + " after=" + engine.CurrentTime);
            MediaPlayer.Stop();
            Check(MediaPlayer.State == MediaState.Stopped, "stop state");
            engine.Shutdown();
            shutdown = true;
            try { engine.Load(); throw new Exception("Load after shutdown succeeded"); }
            catch (SharpDX.SharpDXException error) { Check(error.HResult == unchecked((int)0xc00d3e85), "Load shutdown HRESULT"); }
            Console.WriteLine("PASS: original CommonDX music load, audio stream, advancing clock, pause, resume and shutdown");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (engine != null)
            {
                if (!shutdown) { try { engine.Shutdown(); } catch (SharpDX.SharpDXException) { } }
                engine.Dispose();
            }
        }
    }
}
