using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace GrayPet {
    public sealed class ActionSpec {
        public string id { get; set; }
        public string label { get; set; }
        public int row { get; set; }
        public int[] durations { get; set; }
        public bool loop { get; set; }
    }
    public sealed class AtlasSpec {
        public int width { get; set; }
        public int height { get; set; }
        public int cellWidth { get; set; }
        public int cellHeight { get; set; }
        public int columns { get; set; }
        public string sha256 { get; set; }
        public ActionSpec[] actions { get; set; }
        public static AtlasSpec Load(string path) {
            var s = new JavaScriptSerializer().Deserialize<AtlasSpec>(File.ReadAllText(path));
            s.Validate(); return s;
        }
        public void Validate() {
            if(width != 1536 || height != 2288 || cellWidth != 192 || cellHeight != 208 || columns != 8)
                throw new InvalidDataException("Unexpected Gray atlas dimensions.");
            string[] ids = {"idle","running-right","running-left","waving","jumping","failed","waiting","running","review"};
            int[] counts = {6,8,8,4,5,8,6,6,6};
            if(actions == null || actions.Length != 9) throw new InvalidDataException("Nine actions are required.");
            for(int i=0;i<9;i++) {
                var a=actions[i];
                if(a.id!=ids[i] || a.row!=i || a.durations==null || a.durations.Length!=counts[i] || a.durations.Any(d=>d<=0))
                    throw new InvalidDataException("Invalid Gray action: " + ids[i]);
            }
            if(sha256 != "0e7d1a84f6d25e933e3a45dfdb858b9c263876632d83435f8aebfb031b7fcdeb")
                throw new InvalidDataException("Unexpected source identity.");
        }
        public ActionSpec Find(string id) { return actions.First(a=>a.id==id); }
        public int Cell(ActionSpec a,int frame) {
            if(frame<0 || frame>=a.durations.Length) throw new ArgumentOutOfRangeException("frame");
            return a.row*columns+frame;
        }
        public IEnumerable<int> ValidCells() {
            foreach(var a in actions) for(int f=0;f<a.durations.Length;f++) yield return Cell(a,f);
            for(int i=72;i<88;i++) yield return i;
        }
    }
    public sealed class AnimationPlayer {
        public ActionSpec Action {get; private set;}
        public int Frame {get; private set;}
        public bool Paused {get; set;}
        public bool Finished {get; private set;}
        public double Elapsed {get; private set;}
        public void Play(ActionSpec action) { Action=action; Frame=0; Elapsed=0; Finished=false; }
        public void Advance(double milliseconds) {
            if(Paused || Finished || Action==null || milliseconds<=0) return;
            Elapsed+=milliseconds;
            while(Elapsed>=Action.durations[Frame]) {
                Elapsed-=Action.durations[Frame];
                if(Frame+1<Action.durations.Length) Frame++;
                else if(Action.loop) Frame=0;
                else { Finished=true; Elapsed=0; break; }
            }
        }
    }
    public struct Box {
        public int X,Y,Width,Height;
        public Box(int x,int y,int w,int h) {X=x;Y=y;Width=w;Height=h;}
        public int Right {get{return X+Width;}}
        public int Bottom {get{return Y+Height;}}
    }
    public static class Geometry {
        public static Box Clamp(Box pet,Box work) {
            return new Box(Math.Max(work.X,Math.Min(pet.X,work.Right-pet.Width)),
                Math.Max(work.Y,Math.Min(pet.Y,work.Bottom-pet.Height)),pet.Width,pet.Height);
        }
        public static Box Nearest(Box pet,Box[] monitors) {
            if(monitors==null || monitors.Length==0) throw new ArgumentException("No monitor work area.");
            double x=pet.X+pet.Width/2.0,y=pet.Y+pet.Height/2.0,best=double.MaxValue;
            Box found=monitors[0];
            foreach(var b in monitors) {
                double dx=Math.Max(b.X-x,Math.Max(0,x-b.Right)),dy=Math.Max(b.Y-y,Math.Max(0,y-b.Bottom));
                double distance=dx*dx+dy*dy;
                if(distance<best) {best=distance;found=b;}
            }
            return found;
        }
        // Clockwise from up, in screen coordinates. Half-sector boundaries round clockwise.
        public static int Gaze(double dx,double dy) {
            double angle=Math.Atan2(dx,-dy)*180/Math.PI;
            if(angle<0) angle+=360;
            return ((int)Math.Floor(angle/22.5+0.5))%16;
        }
    }
    public sealed class DragState {
        int cursorX,cursorY; Box start;
        public bool Active {get;private set;}
        public bool Moved {get;private set;}
        public void Begin(int x,int y,Box rect) {cursorX=x;cursorY=y;start=rect;Active=true;Moved=false;}
        public Box Move(int x,int y) {
            int dx=x-cursorX,dy=y-cursorY;
            if(Math.Abs(dx)>3 || Math.Abs(dy)>3) Moved=true;
            return new Box(start.X+dx,start.Y+dy,start.Width,start.Height);
        }
        public void End() {Active=false;}
    }
}
