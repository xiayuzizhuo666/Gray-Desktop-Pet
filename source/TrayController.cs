using System;
using System.IO;
using System.Windows.Media.Imaging;
using Forms=System.Windows.Forms;
using Drawing=System.Drawing;

namespace GrayPet {
    internal sealed class TrayController : IDisposable {
        readonly PetWindow window;
        readonly Forms.NotifyIcon notification;
        readonly Forms.ContextMenuStrip menu=new Forms.ContextMenuStrip();
        readonly Drawing.Icon icon;
        internal readonly Forms.ToolStripMenuItem Pause=new Forms.ToolStripMenuItem("暂停动作");
        internal readonly Forms.ToolStripMenuItem Restore=new Forms.ToolStripMenuItem("显示 Gray");
        internal readonly Forms.ToolStripMenuItem Exit=new Forms.ToolStripMenuItem("退出");
        internal bool Disposed {get;private set;}
        internal bool Visible {get{return !Disposed && notification.Visible;}}
        internal TrayController(PetWindow owner,BitmapSource firstFrame) {
            window=owner;icon=MakeIcon(firstFrame);
            notification=new Forms.NotifyIcon {Icon=icon,Text="Gray 桌宠",ContextMenuStrip=menu};
            menu.Items.Add(Restore);menu.Items.Add(Pause);menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add(Exit);
            Restore.Click+=delegate {OnUi(RestoreWindow);};
            Pause.Click+=delegate {OnUi(delegate {window.SetPaused(!window.Player.Paused);});};
            Exit.Click+=delegate {OnUi(window.Close);};
            notification.DoubleClick+=delegate {OnUi(RestoreWindow);};
            menu.Opening+=delegate {SyncPause();};notification.Visible=true;
        }
        void OnUi(Action action) {if(window.Dispatcher.CheckAccess()) action();else window.Dispatcher.BeginInvoke(action);}
        void RestoreWindow() {window.Show();window.ClampToScreen();window.Activate();}
        internal void SyncPause() {Pause.Checked=window.Player.Paused;Pause.Text=window.Player.Paused?"继续动作":"暂停动作";}
        static Drawing.Icon MakeIcon(BitmapSource frame) {
            using(var stream=new MemoryStream()) {
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(frame));png.Save(stream);stream.Position=0;
                using(var original=new Drawing.Bitmap(stream)) using(var small=new Drawing.Bitmap(original,new Drawing.Size(32,32))) {
                    IntPtr nativeIcon=small.GetHicon();
                    try {return (Drawing.Icon)Drawing.Icon.FromHandle(nativeIcon).Clone();}
                    finally {Native.DestroyIcon(nativeIcon);}
                }
            }
        }
        public void Dispose() {
            if(Disposed) return;Disposed=true;
            notification.Visible=false;notification.Dispose();menu.Dispose();icon.Dispose();
        }
    }
}
