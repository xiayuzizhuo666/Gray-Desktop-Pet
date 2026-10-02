using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
[assembly: System.Reflection.AssemblyTitle("Gray Desktop Pet")]
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]

namespace GrayPet {
    internal static class Program {
        [STAThread] static int Main(string[] args) {
            string directory=AppDomain.CurrentDomain.BaseDirectory;
            bool testing=args.Length>0 && args[0]=="--self-test";
            bool smoke=args.Length>0 && args[0]=="--smoke-test";
            try {
                if(testing) return Tests.Run(new SpriteAtlas(directory),directory);
                bool first;
                using(var singleton=new Mutex(true,"Local\\GrayLocalDesktopPet",out first)) {
                    if(!first) return 0;
                    var atlas=new SpriteAtlas(directory);
                    var app=new Application();app.ShutdownMode=ShutdownMode.OnMainWindowClose;
                    var window=new PetWindow(atlas,true);
                    // Exercise the real startup/mutex/shutdown path, then exit unaided.
                    // No pointer or keyboard input is injected by this diagnostic mode.
                    DispatcherTimer smokeTimer=null;
                    if(smoke) {
                        smokeTimer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(3)};
                        smokeTimer.Tick+=delegate {
                            smokeTimer.Stop();
                            if(!window.IsVisible || !Native.IsWindow(window.Handle) || !window.TimerRunning || window.Tray==null || !window.Tray.Visible)
                                throw new InvalidOperationException("Smoke startup did not create the pet, timer and tray.");
                            var hwnd=window.Handle;window.Close();
                            if(Native.IsWindow(hwnd) || window.TimerRunning || !window.Tray.Disposed)
                                throw new InvalidOperationException("Smoke shutdown left window resources active.");
                            File.WriteAllText(Path.Combine(directory,"smoke-test-results.txt"),"PASS real startup, window, timer, tray and clean shutdown\nUTC: "+DateTime.UtcNow.ToString("o"));
                        };
                        window.Loaded+=delegate {smokeTimer.Start();};
                    }
                    app.Run(window);singleton.ReleaseMutex();
                }
                return 0;
            } catch(Exception error) {
                if(testing || smoke) File.WriteAllText(Path.Combine(directory,testing?"test-results.txt":"smoke-test-results.txt"),"FAIL\n"+error);
                else MessageBox.Show("Gray 启动失败：\n"+error.Message+"\n请保留程序旁的 assets 文件夹和 frames.json。","Gray",MessageBoxButton.OK,MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
