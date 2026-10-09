using System;
using UnityEngine;

namespace ClubClash
{
    // Measured from the original 1536x1024 atlases. Coordinates are top-left pixels.
    // One source sprite and one diameter are shared by windup and actual flight.
    internal sealed class BallArtProfile
    {
        public readonly string Id, Kind;
        public readonly Rect Canonical, Windup, Contact;
        public readonly bool Ellipse;
        public readonly float Diameter;
        public readonly Vector2 WindupCenter;
        public Sprite Sprite;
        public float WorldDiameter;
        public Vector2 WindupPosition;

        BallArtProfile(string id, string kind, Rect canonical, Rect windup, Rect contact,
            float diameter, bool ellipse, Vector2 windupCenter)
        { Id=id;Kind=kind;Canonical=canonical;Windup=windup;Contact=contact;Diameter=diameter;Ellipse=ellipse;WindupCenter=windupCenter; }

        static Rect Box(int left, int top, int right, int bottom)
        { return new Rect(left,top,right-left,bottom-top); }

        public static bool UsesBall(string id)
        { return id=="soccer"||id=="baseball"||id=="volleyball"||id=="tennis"||id=="golf"||id=="handball"||id=="basketball"||id=="badminton"; }

        public static BallArtProfile For(string id)
        {
            switch(id)
            {
                case "soccer": return new BallArtProfile(id,id,Box(1469,655,1518,704),Box(1211,709,1260,756),Box(1469,655,1518,704),49,true,new Vector2(1235.5f,732.5f));
                case "baseball": return new BallArtProfile(id,id,Box(1482,548,1509,574),Box(1084,541,1113,565),Box(1482,548,1509,574),27,false,new Vector2(1098.5f,553));
                case "volleyball": return new BallArtProfile(id,id,Box(1175,516,1222,563),Box(1175,516,1222,563),Box(1454,518,1502,567),47,true,new Vector2(1198.5f,539.5f));
                case "tennis": return new BallArtProfile(id,id,Box(1183,520,1202,540),Box(1183,520,1202,540),Box(1445,521,1462,540),20,false,new Vector2(1192.5f,530));
                case "golf": return new BallArtProfile(id,id,Box(1495,714,1513,730),new Rect(),Box(1495,714,1513,730),18,false,new Vector2(1248,722));
                case "handball": return new BallArtProfile(id,id,Box(1497,531,1528,562),Box(1090,526,1129,563),Box(1497,531,1528,562),39,false,new Vector2(1109.5f,544.5f));
                case "basketball": return new BallArtProfile(id,id,Box(1479,175,1524,222),Box(1124,510,1171,555),new Rect(),47,true,new Vector2(1147.5f,532.5f));
                case "badminton": return new BallArtProfile(id,"shuttle",Box(1475,518,1507,545),Box(1194,525,1219,552),Box(1475,518,1507,545),32,false,new Vector2(1206.5f,538.5f));
                default:return null;
            }
        }

        public Color32[] CanonicalPixels(Color32[] source, int width, int height)
        {
            int w=(int)Canonical.width,h=(int)Canonical.height;
            var pixels=new Color32[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                float nx=(x+.5f-w*.5f)/(w*.5f),ny=(y+.5f-h*.5f)/(h*.5f);
                if(Ellipse && nx*nx+ny*ny>1.02f)continue;
                int sourceY=height-1-((int)Canonical.y+h-1-y);
                Color32 c=source[sourceY*width+(int)Canonical.x+x];
                if(c.a>8)pixels[y*w+x]=c;
            }
            return pixels;
        }

        public void RemovePaintedBall(Color32[] pixels, Rect sourceBounds, int textureHeight, int frame)
        {
            Rect roi=frame==16?Windup:frame==17?Contact:new Rect();
            if(roi.width<=0)return;
            int w=(int)sourceBounds.width,h=(int)sourceBounds.height;
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                float sx=sourceBounds.x+x+.5f,sy=textureHeight-(sourceBounds.y+y+.5f);
                Color32 c=pixels[y*w+x];
                // These contact boxes were measured around independently floating
                // balls only. Their dark corner pixels and red panels are ball art,
                // so neither an ellipse nor the held-finger rule applies here.
                if(frame==17 && !Ellipse && roi.Contains(new Vector2(sx,sy)))
                { pixels[y*w+x]=new Color32();continue; }
                // The atlas slicer retains four pixels of antialias fringe around
                // opaque cores. Clear that faint ball outline as well, without
                // extending the opaque cut into fingers, shoes, or a racket.
                if(c.a>0 && c.a<=128)
                {
                    float fx=(sx-roi.center.x)/(roi.width*.5f+4),fy=(sy-roi.center.y)/(roi.height*.5f+4);
                    if(Mathf.Abs(fx)<=1 && Mathf.Abs(fy)<=1
                        && (Id=="badminton" || fx*fx+fy*fy<=1.02f))
                    { pixels[y*w+x]=new Color32();continue; }
                }
                float nx=(sx-roi.center.x)/(roi.width*.5f),ny=(sy-roi.center.y)/(roi.height*.5f);
                if(Mathf.Abs(nx)>1||Mathf.Abs(ny)>1)continue;
                if(Id!="badminton" && nx*nx+ny*ny>1.02f)continue;
                // Preserve fingers in front of a held ball. Orange/red/yellow panels
                // fail the blue/green skin relation, so they cannot leave a second ball.
                bool skin=c.r>110 && c.g>65 && c.r>c.g*1.10f
                    && c.g>c.b*1.15f && c.b>c.g*.38f && c.g>c.r*.42f;
                if(!skin)pixels[y*w+x]=new Color32();
            }
        }
    }
}
