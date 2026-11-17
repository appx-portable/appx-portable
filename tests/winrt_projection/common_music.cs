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
    private static int Main(string[] args)
    {
        MediaEngine engine = null;
        try
        {
            Marshal.ThrowExceptionForHR(RoInitialize(1));
            Check(MediaPlayer.GameHasControl, "music control");
            engine = (MediaEngine)typeof(MediaPlayer).GetField("_mediaEngineEx", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            MediaPlayer.Volume = 0.25f;
            // Original Play calls SetSource, Load, and Play. Load used to throw
            // E_NOTIMPL under Wine before playback could be reached.
            MediaPlayer.Play(new Song(args[0]));
            double time = 0;
            for (int count = 0; count < 50; ++count)
            {
                Thread.Sleep(200);
                Check(engine.Error == null, "media engine decode/playback error");
                time = engine.CurrentTime;
                if (time >= 1.0 && engine.HasAudio()) break;
            }
            Check(engine.HasAudio() && time >= 1.0 && engine.Duration > 1.0, "audio stream and advancing playback clock");
            MediaPlayer.Pause();
            Thread.Sleep(250);
            double paused = engine.CurrentTime;
            Thread.Sleep(300);
            Check(Math.Abs(engine.CurrentTime - paused) < 0.15, "pause stops music clock");
            MediaPlayer.Resume();
            Thread.Sleep(600);
            Check(engine.CurrentTime > paused + 0.2, "resume advances music clock");
            MediaPlayer.Stop();
            Check(MediaPlayer.State == MediaState.Stopped, "stop state");
            engine.Shutdown();
            try { engine.Load(); throw new Exception("Load after shutdown succeeded"); }
            catch (SharpDX.SharpDXException error) { Check(error.HResult == unchecked((int)0xc00d3e85), "Load shutdown HRESULT"); }
            Console.WriteLine("PASS: original CommonDX music load, audio stream, advancing clock, pause, resume and shutdown");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (engine != null) engine.Dispose(); }
    }
}
