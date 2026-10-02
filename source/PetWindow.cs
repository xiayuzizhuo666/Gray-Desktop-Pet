using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GrayPet {
    internal sealed class SpriteAtlas {
        internal readonly AtlasSpec Spec;
        internal readonly BitmapSource[] Cells = new BitmapSource[88];
        internal readonly BitmapSource Source;
        internal SpriteAtlas(string directory) {
            Spec=AtlasSpec.Load(Path.Combine(directory,"frames.json"));
            string path=Path.Combine(directory,"assets","spritesheet-extended.png");
            using(var stream=File.OpenRead(path)) using(var hash=SHA256.Create()) {
                string actual=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
                if(actual!=Spec.sha256) throw new InvalidDataException("Gray PNG checksum does not match the approved original.");
            }
            var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;
            image.UriSource=new Uri(path,UriKind.Absolute);image.EndInit();image.Freeze();Source=image;
            if(image.PixelWidth!=Spec.width || image.PixelHeight!=Spec.height) throw new InvalidDataException("Gray PNG size mismatch.");
            foreach(int i in Spec.ValidCells()) {
                var crop=new CroppedBitmap(image,new Int32Rect((i%8)*192,(i/8)*208,192,208));
                crop.Freeze();Cells[i]=crop;
            }
        }
    }
    internal sealed class PetWindow : Window {
        internal readonly AnimationPlayer Player=new AnimationPlayer();
        internal readonly Image Sprite=new Image();
        internal readonly ContextMenu PetMenu=new ContextMenu();
        internal readonly MenuItem PauseMenu=new MenuItem {Header="暂停动作",IsCheckable=true};
        internal readonly MenuItem AutomaticMenu=new MenuItem {Header="自动动作",IsCheckable=true,IsChecked=true};
        internal readonly MenuItem GazeMenu=new MenuItem {Header="视线跟随鼠标",IsCheckable=true,IsChecked=true};
        internal readonly MenuItem ExitMenu=new MenuItem {Header="退出"};
        internal readonly MenuItem SizeMenu=new MenuItem {Header="大小"};
        internal readonly MenuItem ActionMenu=new MenuItem {Header="动作"};
        readonly SpriteAtlas atlas;
        readonly DispatcherTimer timer=new DispatcherTimer(DispatcherPriority.Render);
        readonly Stopwatch clock=Stopwatch.StartNew();
        readonly Random random=new Random();
        readonly DragState drag=new DragState();
        IntPtr handle; bool closing; int lastCell=-1,clickCount=1;
        double lastTime,actionAge,walkFraction,gazeAge=2000;
        Native.POINT lastCursor;
        internal TrayController Tray {get;private set;}
        internal IntPtr Handle {get{return handle;}}
        internal int VisibleCell {get{return lastCell;}}
        internal bool TimerRunning {get{return timer.IsEnabled;}}
        internal DragState Drag {get{return drag;}}
        internal PetWindow(SpriteAtlas sprites,bool startTimer) {
            atlas=sprites;Title="Gray 桌宠";Width=240;Height=260;
            WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
            AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;
            ShowInTaskbar=false;ShowActivated=false;WindowStartupLocation=WindowStartupLocation.Manual;
            Sprite.Stretch=Stretch.Fill;Content=Sprite;Sprite.ContextMenu=PetMenu;
            RenderOptions.SetBitmapScalingMode(Sprite,BitmapScalingMode.HighQuality);
            BuildMenu();Play("idle");
            Sprite.MouseLeftButtonDown+=OnDown;Sprite.MouseMove+=OnMove;Sprite.MouseLeftButtonUp+=OnUp;
            Sprite.LostMouseCapture+=delegate { if(drag.Active) {drag.End();ClampToScreen();} };
            KeyDown+=delegate(object sender,KeyEventArgs e) {
                if(e.Key==Key.Q && (Keyboard.Modifiers & ModifierKeys.Control)!=0) Close();
            };
            SourceInitialized+=delegate {
                handle=new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(handle).AddHook(WindowMessage);
            };
            Loaded+=delegate {
                var b=Native.Rect(handle);var work=Geometry.Nearest(b,Native.WorkAreas());
                Native.Move(handle,new Box(work.Right-b.Width-36,work.Bottom-b.Height-12,b.Width,b.Height));
                ClampToScreen();lastTime=clock.Elapsed.TotalMilliseconds;
                if(Tray==null) Tray=new TrayController(this,atlas.Cells[0]);
                if(startTimer) timer.Start();
            };
            SizeChanged+=delegate {if(handle!=IntPtr.Zero && !closing) Dispatcher.BeginInvoke(new Action(ClampToScreen));};
            Closed+=delegate {closing=true;timer.Stop();Sprite.ReleaseMouseCapture();if(Tray!=null) Tray.Dispose();};
            timer.Interval=TimeSpan.FromMilliseconds(33);
            timer.Tick+=delegate { double now=clock.Elapsed.TotalMilliseconds;double delta=Math.Min(200,now-lastTime);lastTime=now;Step(delta); };
            PetMenu.Opened+=delegate {lastTime=clock.Elapsed.TotalMilliseconds;};
            PetMenu.Closed+=delegate {lastTime=clock.Elapsed.TotalMilliseconds;};
        }
        void BuildMenu() {
            PetMenu.Items.Add(new MenuItem {Header="Gray",IsEnabled=false});
            double[] scales={0.75,1.0,1.25,1.5,2.0};
            foreach(double value in scales) {
                double scale=value;var item=new MenuItem {Header=(int)(scale*100)+"%",Tag=scale,IsCheckable=true,IsChecked=scale==1.25};
                item.Click+=delegate {ResizePet(scale);};SizeMenu.Items.Add(item);
            }
            foreach(var spec in atlas.Spec.actions) {
                string id=spec.id;var item=new MenuItem {Header=spec.label,Tag=id};
                item.Click+=delegate {AutomaticMenu.IsChecked=false;Play(id);};ActionMenu.Items.Add(item);
            }
            PetMenu.Items.Add(SizeMenu);PetMenu.Items.Add(ActionMenu);PetMenu.Items.Add(new Separator());
            PauseMenu.Click+=delegate {SetPaused(PauseMenu.IsChecked);};
            AutomaticMenu.Click+=delegate {actionAge=0;};
            GazeMenu.Click+=delegate {gazeAge=2000;RenderFrame();};
            PetMenu.Items.Add(PauseMenu);PetMenu.Items.Add(AutomaticMenu);PetMenu.Items.Add(GazeMenu);
            PetMenu.Items.Add(new Separator());ExitMenu.Click+=delegate {Close();};PetMenu.Items.Add(ExitMenu);
        }
        internal void Play(string id) {Player.Play(atlas.Spec.Find(id));actionAge=0;walkFraction=0;gazeAge=2000;RenderFrame();}
        internal void SetPaused(bool paused) {
            Player.Paused=paused;PauseMenu.IsChecked=paused;lastTime=clock.Elapsed.TotalMilliseconds;
            if(Tray!=null) Tray.SyncPause();
        }
        internal void ResizePet(double scale) {
            foreach(MenuItem i in SizeMenu.Items) i.IsChecked=(double)i.Tag==scale;
            if(handle!=IntPtr.Zero) {
                var work=Geometry.Nearest(Native.Rect(handle),Native.WorkAreas());
                var source=PresentationSource.FromVisual(this);
                if(source!=null) {
                    var matrix=source.CompositionTarget.TransformFromDevice;
                    scale=Math.Min(scale,Math.Min(work.Width*matrix.M11/192,work.Height*matrix.M22/208));
                }
            }
            Width=192*scale;Height=208*scale;
            Dispatcher.BeginInvoke(new Action(ClampToScreen));
        }
        internal void ClampToScreen() {
            // A DPI/layout callback can arrive while crossing a monitor edge.
            // Preserve the pointer's drag position until release or capture loss.
            if(handle==IntPtr.Zero || closing || drag.Active || !Native.IsWindow(handle)) return;
            var b=Native.Rect(handle);var work=Geometry.Nearest(b,Native.WorkAreas());
            if(b.Width>work.Width || b.Height>work.Height) {
                double fit=Math.Min((double)work.Width/b.Width,(double)work.Height/b.Height);
                Width*=fit;Height*=fit;return;
            }
            Native.Move(handle,Geometry.Clamp(b,work));
        }
        IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled) {
            if(message==0x007E || message==0x02E0 || message==0x001A) Dispatcher.BeginInvoke(new Action(ClampToScreen));
            return IntPtr.Zero;
        }
        void OnDown(object sender,MouseButtonEventArgs e) {
            Native.POINT p;if(!Native.GetCursorPos(out p)) return;
            clickCount=e.ClickCount;drag.Begin(p.X,p.Y,Native.Rect(handle));
            if(!Sprite.CaptureMouse()) drag.End();
            e.Handled=true;
        }
        void OnMove(object sender,MouseEventArgs e) {
            if(!drag.Active) return;Native.POINT p;if(Native.GetCursorPos(out p)) Native.Move(handle,drag.Move(p.X,p.Y));
        }
        void OnUp(object sender,MouseButtonEventArgs e) {
            if(!drag.Active) return;bool moved=drag.Moved;drag.End();Sprite.ReleaseMouseCapture();ClampToScreen();
            RespondToGesture(moved,clickCount);e.Handled=true;
        }
        internal void RespondToGesture(bool moved,int count) {
            if(!moved) Play(count>1?"jumping":"waving");
        }
        internal void Step(double delta) {
            if(closing || Player.Paused || drag.Active || PetMenu.IsOpen) return;
            actionAge+=delta;gazeAge+=delta;Player.Advance(delta);
            if(Player.Finished) Play("idle");
            Native.POINT p;
            if(Native.GetCursorPos(out p)) {
                if(p.X!=lastCursor.X || p.Y!=lastCursor.Y) gazeAge=0;
                lastCursor=p;
            }
            if(handle!=IntPtr.Zero && (Player.Action.id=="running-left" || Player.Action.id=="running-right")) {
                var b=Native.Rect(handle);var work=Geometry.Nearest(b,Native.WorkAreas());
                walkFraction+=(Player.Action.id=="running-left"?-1:1)*45*delta/1000;
                int dx=(int)walkFraction;walkFraction-=dx;
                var requested=new Box(b.X+dx,b.Y,b.Width,b.Height);var bounded=Geometry.Clamp(requested,work);
                Native.Move(handle,bounded);if(bounded.X!=requested.X) Play("failed");
            }
            if(AutomaticMenu.IsChecked && actionAge>7000) {
                string[] choices={"idle","idle","idle","running-left","running-right","waving","jumping","waiting","running","review"};
                Play(choices[random.Next(choices.Length)]);
            }
            RenderFrame();
        }
        internal void RenderFrame() {
            int cell=atlas.Spec.Cell(Player.Action,Player.Frame);
            if(GazeMenu.IsChecked && Player.Action.id=="idle" && gazeAge<1200 && handle!=IntPtr.Zero) {
                var b=Native.Rect(handle);double dx=lastCursor.X-(b.X+b.Width/2.0),dy=lastCursor.Y-(b.Y+b.Height/2.0);
                if(dx*dx+dy*dy>24*24) cell=72+Geometry.Gaze(dx,dy);
            }
            if(cell!=lastCell) {Sprite.Source=atlas.Cells[cell];lastCell=cell;}
        }
        internal void SaveRender(string path) {
            UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),96,96,PixelFormats.Pbgra32);
            bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var file=File.Create(path)) encoder.Save(file);
        }
    }
}
