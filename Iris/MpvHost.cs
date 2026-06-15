using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Iris;

// HwndHost is a special WPF base class that lets you embed a real native
// Win32 window (HWND) inside your WPF layout. We inherit from it to create
// a child window that mpv can render video into.
public class MpvHost: HwndHost
{

    // P/Invoke declaration for Win32's CreateWindowEx function.
    // This lives in user32.dll — the core Windows UI dll.
    // It creates a native Win32 window and returns its handle (HWND).
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);
    
    // P/Invoke for DestroyWindow — cleans up the native window when we're done.
    // Always important to destroy what you create in native code,
    // since the garbage collector doesn't know about native resources.
    [DllImport("user32.dll",SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);
    
    // Win32 window style constants.
    // These are bit flags — you combine them with | (bitwise OR).
    // WS_CHILD  = this window is a child of another window (our WPF window)
    // WS_VISIBLE = the window is visible immediately after creation
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;

    // Stores the native window handle (HWND) after we create it.
    // IntPtr is C#'s way of holding a raw pointer/handle from native code.
    private IntPtr _hwnd;
    
    // Public property so MpvPlayer can read the HWND and tell mpv
    // "render your video into this window."
    public IntPtr Hwnd => _hwnd;
    
    // WPF calls this method when it's ready to create the native child window.
    // hwndParent is the handle of the WPF window that will own our child window.
    // We must return a HandleRef wrapping our created HWND.
    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
       _hwnd = CreateWindowEx(
           0,
           "STATIC", 
           "", 
           WS_CHILD | WS_VISIBLE, 
           0, 0, 0, 0, 
           hwndParent.Handle, 
           IntPtr.Zero, 
           IntPtr.Zero, 
           IntPtr.Zero);
       
       
       // HandleRef wraps an HWND with its owner object.
       // WPF uses this to track the native window's lifetime.
       return new HandleRef(this, _hwnd);
       
    }

    // WPF calls this when the host is being removed/destroyed.
    // We must manually destroy the native window here —
    // this is the "unmanaged resource cleanup" pattern in C#.
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DestroyWindow(hwnd.Handle);
    }
}