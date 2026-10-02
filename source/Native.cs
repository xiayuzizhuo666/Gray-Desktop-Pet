using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GrayPet {
    internal static class Native {
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X,Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left,Top,Right,Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO {public int Size; public RECT Monitor,Work; public uint Flags;}
        delegate bool MonitorEnum(IntPtr monitor,IntPtr dc,ref RECT rect,IntPtr data);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll",SetLastError=true)] static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
        [DllImport("user32.dll",SetLastError=true)] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorEnum callback,IntPtr data);
        [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor,ref MONITORINFO info);
        [DllImport("user32.dll",EntryPoint="GetWindowLong")] internal static extern int GetWindowLong(IntPtr hwnd,int index);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool AreDpiAwarenessContextsEqual(IntPtr first,IntPtr second);
        internal static Box Rect(IntPtr hwnd) {
            RECT r;if(!GetWindowRect(hwnd,out r)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return new Box(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top);
        }
        internal static void Move(IntPtr hwnd,Box b) {
            if(!SetWindowPos(hwnd,IntPtr.Zero,b.X,b.Y,0,0,0x0001|0x0004|0x0010))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        internal static Box[] WorkAreas() {
            var areas=new List<Box>();
            EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,delegate(IntPtr m,IntPtr dc,ref RECT r,IntPtr d) {
                var info=new MONITORINFO();info.Size=Marshal.SizeOf(typeof(MONITORINFO));
                if(GetMonitorInfo(m,ref info)) areas.Add(new Box(info.Work.Left,info.Work.Top,info.Work.Right-info.Work.Left,info.Work.Bottom-info.Work.Top));
                return true;
            },IntPtr.Zero);
            if(areas.Count==0) throw new InvalidOperationException("Windows did not return a display work area.");
            return areas.ToArray();
        }
    }
}
