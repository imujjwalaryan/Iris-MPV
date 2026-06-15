using System;
using System.Runtime.InteropServices;

namespace Iris;

public class MpvPlayer: IDisposable
{
    // DllImport tells C# "this function lives in a native dll, not in .NET"
    // CallingConvention.Cdecl is the calling convention used by C libraries —
    // it defines how arguments are passed and stack is cleaned up between C# and C.
    // Getting this wrong causes crashes, so always use Cdecl for libmpv.
    
    // Creates a new mpv instance, returns a pointer to it.
    // We store this pointer and pass it to every other mpv function.
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mpv_create();

    // Initializes mpv after all options are set.
    // ctx = context, the mpv instance pointer from mpv_create().
    // Returns 0 on success, negative number on failure.
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_initialize(IntPtr ctx);
    
    // Destroys the mpv instance and frees all native memory.
    // Must be called manually — GC won't do this for us.
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_destroy(IntPtr ctx);
    
    // Sets an option before mpv_initialize — used for things like
    // passing the window handle (HWND) so mpv knows where to render.
    // format = what data type we're passing (int64, double etc.)
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_set_option(IntPtr ctx, string name, int format, ref long data);

    // String version of mpv_set_option — simpler when value is text.
    // e.g. mpv_set_option_string(ctx, "vo", "gpu")
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_set_option_string(IntPtr ctx, string name, string value);

    // Sends a command to mpv as a null-terminated string array.
    // e.g. ["loadfile", "video.mp4", null]
    // null at the end tells the C side where the array stops.
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_command(IntPtr ctx, string?[] args);

    // Reads a runtime property from mpv.
    // Properties are live values like current time, duration, volume.
    // Different from options — options are set before init, properties change during playback.
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_get_property(IntPtr ctx, string name, int format, ref double data);

    // Sets a runtime property — e.g. changing volume while video is playing.
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_set_property(IntPtr ctx, string name, int format, ref double data);

    // String version of mpv_set_property — for properties that take text values
    // e.g. mpv_set_property_string(ctx, "pause", "yes")
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_set_property_string(IntPtr ctx, string name, string value);

    
    // ── Dispose ───────────────────────────────────────────────────────────
    // IDisposable pattern for cleaning up native resources.
    // The GC handles managed .NET objects automatically but has no idea
    // about our native mpv handle — we must free it ourselves.
    // Callers should wrap MpvPlayer in a 'using' block or call Dispose()
    // when the window closes.
    public void Dispose()
    {
        // _disposed check prevents double-free —
        // calling mpv_destroy twice on the same handle would crash.
        if (!_disposed && _handle != IntPtr.Zero)
        {
            mpv_destroy(_handle);
            _handle = IntPtr.Zero; // null the pointer after freeing
            _disposed = true;
        }
    }
    
    // ── MPV format constants ──────────────────────────────────────────────
    // These are defined in mpv's client.h header file.
    // When we call mpv_get_property or mpv_set_property we must tell mpv
    // what data type we're passing — it uses these int codes to know.

    // 64-bit integer — used for things like the window handle (HWND)
    private const int MPV_FORMAT_INT64  = 4;

    // 64-bit floating point — used for time, duration, volume, etc.
    private const int MPV_FORMAT_DOUBLE = 5;
    
    // ── State ─────────────────────────────────────────────────────────────

    // Holds the pointer to our mpv instance returned by mpv_create().
    // Every mpv function needs this — it's how mpv knows which instance
    // you're talking to. Like "this" pointer in C++.
    private IntPtr _handle;

    // Tracks whether Dispose() has already been called.
    // Prevents us from calling mpv_destroy() twice on the same handle
    // which would crash the app — double free is a classic native code bug.
    private bool _disposed;

    // ── Constructor ───────────────────────────────────────────────────────
    // windowHandle is the HWND of our MpvHost child window.
    // mpv will render video directly into that window.
    public MpvPlayer(IntPtr windowHandle)
    {
        // Create the mpv instance — like calling new in C++
        _handle = mpv_create();
        if(_handle == IntPtr.Zero) 
            throw new Exception("Failed to create mpv instance.");
        
        
        // Convert the window handle to a long and pass it to mpv.
        // "wid" = window id — this tells mpv where to draw video.
        // Must be set BEFORE mpv_initialize, not after.
        long wid = windowHandle.ToInt64();
        mpv_set_option(_handle, "wid", MPV_FORMAT_INT64, ref wid);
        
        // vo = video output driver.
        // "gpu" uses DirectX on Windows — hardware accelerated, best quality.
        mpv_set_option_string(_handle,"vo", "gpu");
        
        // hwdec = hardware decoding.
        // "auto" lets mpv pick the best decoder your GPU supports
        // (DXVA2, D3D11VA, NVDEC for your RTX 4070 Ti Super, etc.)
        mpv_set_option_string(_handle,"hwdec", "auto");
        
        // keep-open = don't unload the file when playback ends.
        // Without this mpv closes the video as soon as it finishes.
        mpv_set_option_string(_handle,"keep-open", "yes");
        
        // Initialize mpv — options are now locked in.
        // After this we can start sending playback commands.
        int result = mpv_initialize(_handle);
        if(result<0)
            throw new Exception($"Failed to initialize mpv. Error code: {result}");
    }
    
    // ── Playback controls ─────────────────────────────────────────────────

    // Loads a file path into mpv and starts playing it.
    // mpv_command takes a null-terminated string array —
    // "loadfile" is the command, path is the argument, null ends the array.
    public void Load(string path)
    {
        mpv_command(_handle, new[] { "loadfile", path, null });
    }

    // Resumes playback by setting the pause property to "no"
    public void Play()
    {
        mpv_set_property_string(_handle, "pause", "no");
    }

    // Pauses playback by setting the pause property to "yes"
    public void Pause()
    {
        mpv_set_property_string(_handle, "pause", "yes");
    }

    // Toggles between play and pause.
    // "cycle" is an mpv command that flips a boolean property.
    // Cleaner than checking current state and calling Play/Pause manually.
    public void TogglePlayPause()
    {
        mpv_command(_handle, new[] { "cycle", "pause", null });
    }
    public void ToggleMute()
    {
        mpv_command(_handle, new[] { "cycle", "mute", null });
    }

    // Seeks forward or backward relative to current position.
    // Positive = forward, negative = backward.
    // e.g. Seek(-5) goes back 5 seconds
    public void Seek(double seconds)
    {
        mpv_command(_handle, new[] { "seek", seconds.ToString(), "relative", null });
    }

    // Seeks to an exact position in the file.
    // e.g. SeekAbsolute(120) jumps to the 2 minute mark
    public void SeekAbsolute(double seconds)
    {
        mpv_command(_handle, new[] { "seek", seconds.ToString(), "absolute", null });
    }
    
    // ── Properties ────────────────────────────────────────────────────────
    // These read/write live values from mpv during playback.
    // Unlike options (set before init), properties change in real time.
    
    // Total length of the loaded file in seconds.
    // Returns 0 if no file is loaded yet.
    public double Duration
    {
        get
        {
            double val = 0;
            mpv_get_property(_handle, "duration", MPV_FORMAT_DOUBLE, ref val);
            return val;
        }
    }

    // Current playback position in seconds.
    // This is what we'll use to update the progress bar.
    public double CurrentTime
    {
        get
        {
            double val = 0;
            mpv_get_property(_handle, "time-pos", MPV_FORMAT_DOUBLE, ref val);
            return val;
        }
    }

    // Volume level — 0 to 100.
    // getter reads current volume, setter changes it in real time.
    public double Volume
    {
        get
        {
            double val = 0;
            mpv_get_property(_handle, "volume", MPV_FORMAT_DOUBLE, ref val);
            return val;
        }
        set
        {
            double val = value;
            mpv_set_property(_handle, "volume", MPV_FORMAT_DOUBLE, ref val);
        }
    }
    
    public bool IsPaused
    {
        get
        {
            double val = 0;
            mpv_get_property(_handle, "pause", MPV_FORMAT_DOUBLE, ref val);
            return val == 1;
        }
    }

    public bool IsMuted
    {
        get
        {
            double val = 0;
            mpv_get_property(_handle, "mute", MPV_FORMAT_DOUBLE, ref val);
            return val == 1;
        }
    }
}
























