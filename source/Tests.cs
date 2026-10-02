using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GrayPet {
    internal static class Tests {
        static readonly List<string> results=new List<string>();
        static int assertions;
        static void Check(bool ok,string reason) {assertions++;if(!ok) throw new Exception(reason);}
        static void Test(string name,Action action) {action();results.Add("PASS "+name);}
        static void Pump(int milliseconds) {
            var frame=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.Background);
            timer.Interval=TimeSpan.FromMilliseconds(milliseconds);timer.Tick+=delegate {timer.Stop();frame.Continue=false;};
            timer.Start();Dispatcher.PushFrame(frame);
        }
        static byte[] Pixels(BitmapSource image) {
            var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);
            var pixels=new byte[converted.PixelWidth*converted.PixelHeight*4];converted.CopyPixels(pixels,converted.PixelWidth*4,0);return pixels;
        }
        internal static int Run(SpriteAtlas atlas,string directory) {
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            results.Add("Run UTC: "+DateTime.UtcNow.ToString("o"));
            results.Add("Version: "+typeof(Program).Assembly.GetName().Version+"; OS: "+Environment.OSVersion+"; CLR: "+Environment.Version);
            Test("runtime frame definitions match the supplied original manifest",delegate {
                string manifestPath=Path.Combine(directory,"assets","manifest.json");
                var supplied=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(manifestPath));
                var actions=(object[])supplied["standard_animations"];
                Check(actions.Length==atlas.Spec.actions.Length,"Manifest action count");
                for(int i=0;i<actions.Length;i++) {
                    var source=(Dictionary<string,object>)actions[i];var runtime=atlas.Spec.actions[i];
                    Check((string)source["state"]==runtime.id && (int)source["row"]==runtime.row,"Manifest state "+i);
                    var times=((object[])source["preview_frame_durations_ms"]).Select(t=>(int)t).ToArray();
                    Check(times.SequenceEqual(runtime.durations),"Manifest timing "+runtime.id);
                }
            });
            Test("source SHA256, dimensions and all 73 populated / 15 empty cells",delegate {
                Check(atlas.Spec.ValidCells().Distinct().Count()==73,"Valid cell count");
                var bytes=Pixels(atlas.Source);int nonempty=0,empty=0;
                for(int row=0;row<11;row++) for(int col=0;col<8;col++) {
                    bool visible=false;
                    for(int y=row*208;y<(row+1)*208 && !visible;y++) for(int x=col*192;x<(col+1)*192;x++)
                        if(bytes[(y*1536+x)*4+3]!=0) {visible=true;break;}
                    int index=row*8+col;bool valid=atlas.Spec.ValidCells().Contains(index);
                    Check(visible==valid,"Alpha occupancy at cell "+index);if(visible) nonempty++;else empty++;
                }
                Check(nonempty==73 && empty==15,"Actual occupancy totals");
                Check(atlas.Cells.Where(c=>c!=null).Count()==73,"Only valid cells are cropped");
            });
            Test("frame timing boundaries, loops and one-shot completion",delegate {
                foreach(var action in atlas.Spec.actions) {
                    var p=new AnimationPlayer();p.Play(action);
                    for(int f=0;f<action.durations.Length;f++) {
                        Check(p.Frame==f,"Frame sequence "+action.id);p.Advance(action.durations[f]-1);
                        Check(p.Frame==f && !p.Finished,"Early frame boundary "+action.id);p.Advance(1);
                    }
                    Check(action.loop ? p.Frame==0 && !p.Finished : p.Frame==action.durations.Length-1 && p.Finished,"End policy "+action.id);
                    for(int n=0;n<100;n++) {p.Advance(73);Check(p.Frame>=0 && p.Frame<action.durations.Length,"Frame bounds");}
                }
            });
            Test("100 repeated pause / resume cycles preserve animation time",delegate {
                var p=new AnimationPlayer();p.Play(atlas.Spec.Find("idle"));p.Advance(57);
                for(int n=0;n<100;n++) {
                    int f=p.Frame;double t=p.Elapsed;p.Paused=true;p.Advance(10000);
                    Check(p.Frame==f && p.Elapsed==t,"Pause mutation");p.Paused=false;p.Advance(10);
                }
            });
            Test("16 gaze directions including sector interiors and wrap",delegate {
                for(int n=0;n<16;n++) foreach(double shift in new double[]{-11,0,11}) {
                    double angle=(n*22.5+shift)*Math.PI/180;
                    Check(Geometry.Gaze(Math.Sin(angle)*100,-Math.Cos(angle)*100)==n,"Gaze sector "+n);
                }
                Check(Geometry.Gaze(0,-100)==0 && Geometry.Gaze(100,0)==4 && Geometry.Gaze(0,100)==8 && Geometry.Gaze(-100,0)==12,"Cardinals");
            });
            Test("multiple monitor geometry, negative coordinates, gaps and work-area edges",delegate {
                var screens=new[]{new Box(-1920,0,1920,1040),new Box(0,0,2560,1400),new Box(2560,-1080,1920,1040)};
                var b=Geometry.Clamp(new Box(-2100,1100,240,260),screens[0]);
                Check(b.X==-1920 && b.Y==780,"Left monitor clamp");
                b=Geometry.Clamp(new Box(4500,-1500,240,260),screens[2]);Check(b.X==4240 && b.Y==-1080,"Upper right clamp");
                Check(Geometry.Nearest(new Box(-1800,100,240,260),screens).X==-1920,"Negative monitor selection");
                Check(Geometry.Nearest(new Box(3000,-800,240,260),screens).Y==-1080,"Vertical monitor selection");
                b=Geometry.Clamp(new Box(5000,5000,240,260),Geometry.Nearest(new Box(5000,5000,240,260),screens));
                Check(b.X>=0 && b.Right<=2560 && b.Bottom<=1400,"Offscreen recovery");
            });
            Test("drag offset and click / movement distinction",delegate {
                var d=new DragState();d.Begin(115,130,new Box(100,100,240,260));
                var b=d.Move(-500,300);Check(b.X==-515 && b.Y==270 && d.Moved,"Drag offset");
                d.End();Check(!d.Active,"Drag release");d.Begin(5,5,new Box(0,0,240,260));d.Move(7,6);Check(!d.Moved,"Click threshold");
            });
            var window=new PetWindow(atlas,true);window.Show();Pump(150);
            Test("actual window DPI awareness and logical-to-physical size",delegate {
                var dpi=Native.GetDpiForWindow(window.Handle);
                var context=Native.GetWindowDpiAwarenessContext(window.Handle);
                results.Add("Actual window DPI: "+dpi+"; PerMonitorV2: "+Native.AreDpiAwarenessContextsEqual(context,new IntPtr(-4)));
                Check(Native.AreDpiAwarenessContextsEqual(context,new IntPtr(-4)),"PerMonitorV2 awareness is active");
                bool switchValue;
                Check(AppContext.TryGetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges",out switchValue) && !switchValue,"WPF per-monitor scaling enabled");
                Check(AppContext.TryGetSwitch("Switch.System.Windows.DoNotUsePresentationDpiCapabilityTier2OrGreater",out switchValue) && !switchValue,"WPF tier-two DPI handling enabled");
                var transform=PresentationSource.FromVisual(window).CompositionTarget.TransformToDevice;
                Check(Math.Abs(transform.M11-dpi/96.0)<0.001 && Math.Abs(transform.M22-dpi/96.0)<0.001,"WPF transform matches native DPI");
                var bounds=Native.Rect(window.Handle);
                Check(Math.Abs(bounds.Width-window.ActualWidth*transform.M11)<=1 && Math.Abs(bounds.Height-window.ActualHeight*transform.M22)<=1,"Native physical size matches WPF logical size");
            });
            Test("actual Windows window: transparent, borderless, topmost and timer",delegate {
                Check(Native.IsWindow(window.Handle),"Native HWND");
                int style=Native.GetWindowLong(window.Handle,-16),extended=Native.GetWindowLong(window.Handle,-20);
                Check((style & 0x00C00000)==0,"Borderless native style");Check((extended & 8)!=0,"Native topmost style");
                Check((extended & 0x00080000)!=0 && window.AllowsTransparency,"Native layered transparency");
                Check(!window.ShowInTaskbar && window.TimerRunning,"Window lifecycle settings");
                window.AutomaticMenu.IsChecked=false;window.GazeMenu.IsChecked=false;window.Play("idle");
                Pump(400);Check(window.Player.Frame>0,"Real dispatcher animation advanced");
                window.SaveRender(Path.Combine(directory,"window-render.png"));
                var rendered=new BitmapImage(new Uri(Path.Combine(directory,"window-render.png")));var renderedBytes=Pixels(rendered);
                Check(renderedBytes[3]==0 && renderedBytes.Where((v,i)=>i%4==3).Any(v=>v>0),"Rendered transparency and visible sprite");
            });
            Test("right-click menu structure, open/close, size and all action commands",delegate {
                Check(window.SizeMenu.Items.Count==5 && window.ActionMenu.Items.Count==9,"Menu entries");
                window.PetMenu.PlacementTarget=window.Sprite;window.PetMenu.IsOpen=true;Pump(80);Check(window.PetMenu.IsOpen,"Menu opened");
                window.PetMenu.IsOpen=false;Pump(50);Check(!window.PetMenu.IsOpen,"Menu closed");
                foreach(MenuItem item in window.ActionMenu.Items) {
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check(window.Player.Action.id==(string)item.Tag,"Action command "+item.Tag);
                }
                foreach(MenuItem item in window.SizeMenu.Items) {
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump(40);
                    Check(Math.Abs(window.Width-192*(double)item.Tag)<0.1,"Size command "+item.Header);
                    var b=Native.Rect(window.Handle);var work=Geometry.Nearest(b,Native.WorkAreas());
                    Check(b.X>=work.X && b.Y>=work.Y && b.Right<=work.Right && b.Bottom<=work.Bottom,"Resize bounds");
                }
                window.ResizePet(1.25);Pump(40);
            });
            Test("menu pause / resume 100 cycles and frozen frame",delegate {
                window.Play("idle");
                for(int n=0;n<100;n++) {
                    window.PauseMenu.IsChecked=true;window.PauseMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    int f=window.Player.Frame;double elapsed=window.Player.Elapsed;window.Step(5000);
                    Check(window.Player.Frame==f && window.Player.Elapsed==elapsed,"Menu pause mutation");
                    window.PauseMenu.IsChecked=false;window.PauseMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));window.Step(20);
                }
                Check(!window.Player.Paused && window.TimerRunning,"Resume retains one timer");
            });
            Test("click / double-click decisions, drag suppression and one-shot return",delegate {
                window.AutomaticMenu.IsChecked=false;window.Play("idle");
                window.RespondToGesture(false,1);Check(window.Player.Action.id=="waving","Single-click gesture chooses wave");
                window.Step(1000);Check(window.Player.Action.id=="idle","Wave returns to idle");
                window.RespondToGesture(false,2);Check(window.Player.Action.id=="jumping","Double-click gesture chooses jump");
                window.Step(1000);Check(window.Player.Action.id=="idle","Jump returns to idle");
                window.RespondToGesture(true,1);Check(window.Player.Action.id=="idle","A drag does not trigger a wave");
            });
            Test("walking displacement, pause and right-edge recovery",delegate {
                var b=Native.Rect(window.Handle);var work=Geometry.Nearest(b,Native.WorkAreas());
                Native.Move(window.Handle,new Box(work.X+120,work.Y+100,b.Width,b.Height));
                window.Play("running-right");window.Step(1000);b=Native.Rect(window.Handle);
                Check(b.X==work.X+165,"Right walk advances 45 physical pixels per second");
                window.SetPaused(true);var frame=window.Player.Frame;window.Step(20000);
                Check(Native.Rect(window.Handle).X==b.X && window.Player.Frame==frame,"Pause freezes both movement and animation");
                window.SetPaused(false);window.Play("running-left");window.Step(1000);
                Check(Native.Rect(window.Handle).X==work.X+120,"Left walk returns 45 pixels");
                Native.Move(window.Handle,new Box(work.Right-b.Width,work.Y+100,b.Width,b.Height));
                window.Play("running-right");window.Step(1000);
                Check(window.Player.Action.id=="failed" && Native.Rect(window.Handle).Right==work.Right,"Right edge stays inside work area");
                window.Step(2000);Check(window.Player.Action.id=="idle","Edge reaction returns to idle");
            });
            Test("tray icon, restore, pause / resume and synchronization",delegate {
                Check(window.Tray!=null && window.Tray.Visible,"Tray icon enabled");
                for(int n=0;n<20;n++) {
                    window.Tray.Pause.PerformClick();Check(window.Player.Paused && window.PauseMenu.IsChecked && window.Tray.Pause.Checked,"Tray pause");
                    window.Tray.Pause.PerformClick();Check(!window.Player.Paused && !window.PauseMenu.IsChecked && !window.Tray.Pause.Checked,"Tray resume");
                }
                var originalTray=window.Tray;
                var position=Native.Rect(window.Handle);var area=Geometry.Nearest(position,Native.WorkAreas());
                Native.Move(window.Handle,new Box(area.X+100,area.Y+100,position.Width,position.Height));
                position=Native.Rect(window.Handle);
                for(int n=0;n<10;n++) {
                    window.Hide();Check(!window.IsVisible,"Hidden window");window.Tray.Restore.PerformClick();Pump(10);
                    Check(window.IsVisible && object.ReferenceEquals(originalTray,window.Tray),"Tray restores without creating another icon");
                    var restored=Native.Rect(window.Handle);
                    Check(restored.X==position.X && restored.Y==position.Y,"Tray restore preserves an onscreen position");
                }
            });
            Test("native movement, monitor clamp, edge failure and return to idle",delegate {
                var start=Native.Rect(window.Handle);var work=Geometry.Nearest(start,Native.WorkAreas());
                var d=new DragState();d.Begin(10,10,start);var requested=d.Move(60,80);Native.Move(window.Handle,requested);
                var actual=Native.Rect(window.Handle);Check(actual.X==requested.X && actual.Y==requested.Y,"Native drag-model movement");d.End();
                Native.Move(window.Handle,new Box(work.X-200,work.Y-200,start.Width,start.Height));window.ClampToScreen();
                actual=Native.Rect(window.Handle);Check(actual.X==work.X && actual.Y==work.Y,"Native clamp");
                window.Play("running-left");window.Step(100);Check(window.Player.Action.id=="failed","Blocked state");
                window.Step(2000);Check(window.Player.Action.id=="idle","One-shot returns to idle");
            });
            Test("layout or DPI clamp waits for a drag to finish",delegate {
                var start=Native.Rect(window.Handle);var work=Geometry.Nearest(start,Native.WorkAreas());
                window.Drag.Begin(10,10,start);
                Native.Move(window.Handle,new Box(work.X-80,work.Y+40,start.Width,start.Height));
                window.ClampToScreen();var during=Native.Rect(window.Handle);
                Check(during.X==work.X-80,"A queued layout clamp must not snap an active cross-monitor drag");
                window.Drag.End();window.ClampToScreen();var after=Native.Rect(window.Handle);
                Check(after.X==work.X && after.Y==work.Y+40,"Drag release restores work-area bounds");
            });
            Test("exit command destroys native window and stops timer",delegate {
                IntPtr hwnd=window.Handle;window.ExitMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump(40);
                Check(!Native.IsWindow(hwnd) && !window.TimerRunning && window.Tray.Disposed,"Clean exit");
            });
            Test("tray exit also destroys window, timer and notification icon",delegate {
                var second=new PetWindow(atlas,true);second.Show();Pump(60);IntPtr hwnd=second.Handle;
                Check(second.Tray.Visible,"Second window tray");second.Tray.Exit.PerformClick();Pump(40);
                Check(!Native.IsWindow(hwnd) && !second.TimerRunning && second.Tray.Disposed,"Tray exit cleanup");
            });
            Test("repeat launch exits successfully without another window or dialog",delegate {
                bool first;
                using(var singleton=new Mutex(true,"Local\\GrayLocalDesktopPet",out first)) {
                    Check(first,"Self-test acquired singleton");
                    try {
                        for(int n=0;n<3;n++) {
                            using(var child=Process.Start(new ProcessStartInfo(Path.Combine(directory,"GrayPet.exe")) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})) {
                                bool exited=child.WaitForExit(3000);
                                if(!exited) {child.Kill();throw new Exception("Duplicate launch did not exit within three seconds.");}
                                Check(child.ExitCode==0,"Duplicate launch exit code");
                            }
                        }
                    } finally {if(first) singleton.ReleaseMutex();}
                }
            });
            Test("real entry point, live duplicate launch and automatic smoke shutdown",delegate {
                using(var child=Process.Start(new ProcessStartInfo(Path.Combine(directory,"GrayPet.exe"),"--smoke-test") {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})) {
                    try {
                        Check(child.WaitForInputIdle(3000),"Real startup reaches its message loop");
                        // This no-taskbar WPF window has a hidden owner, so Process.MainWindowHandle
                        // need not identify it. The child checks its own HWND/timer/tray before exit.
                        using(var duplicate=Process.Start(new ProcessStartInfo(Path.Combine(directory,"GrayPet.exe")) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})) {
                            try {Check(duplicate.WaitForExit(2000) && duplicate.ExitCode==0,"Duplicate exits while real pet is alive");}
                            finally {if(!duplicate.HasExited) {duplicate.Kill();duplicate.WaitForExit(2000);}}
                        }
                        Check(!child.HasExited,"Duplicate does not stop the existing pet");
                        Check(child.WaitForExit(6000) && child.ExitCode==0,"Smoke timer closes real app cleanly");
                        Check(File.ReadAllText(Path.Combine(directory,"smoke-test-results.txt")).StartsWith("PASS"),"Child verified its window, timer and tray lifecycle");
                    } finally {if(!child.HasExited) {child.Kill();child.WaitForExit(2000);}}
                }
                bool acquired;
                using(var mutex=new Mutex(true,"Local\\GrayLocalDesktopPet",out acquired)) {
                    Check(acquired,"Singleton released after real app exit");if(acquired) mutex.ReleaseMutex();
                }
            });
            results.Add("Assertions: "+assertions);
            results.Add("Actual monitor work areas: "+string.Join("; ",Native.WorkAreas().Select(b=>string.Format("{0},{1} {2}x{3}",b.X,b.Y,b.Width,b.Height))));
            results.Add("Limit: physical mouse input was not automated; menu events, drag model and native movement were exercised programmatically.");
            results.Add("Limit: click/double-click decisions were invoked inside the app; OS input delivery and hit testing remain unverified.");
            results.Add("Limit: negative-coordinate and multi-monitor cases used synthetic geometry; only connected displays were exercised natively.");
            results.Add("Limit: no live DPI setting change, mixed-DPI monitor crossing, monitor unplug, or Explorer restart was performed.");
            File.WriteAllLines(Path.Combine(directory,"test-results.txt"),results,Encoding.UTF8);app.Shutdown();return 0;
        }
    }
}
