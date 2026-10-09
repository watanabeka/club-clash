using System.Collections.Generic;
using UnityEngine;

namespace ClubClash
{
    // These are authored PNG sprites, not runtime font rendering.
    public static class PixelLogo
    {
        sealed class Graphic { public Texture2D Texture;public Rect Pixels; }
        static readonly Dictionary<string,Graphic> cache=new Dictionary<string,Graphic>();
        static Graphic Load(string name)
        {
            if(cache.TryGetValue(name,out Graphic existing))return existing;
            Texture2D texture=Resources.Load<Texture2D>("Branding/"+name);
            if(texture==null)return null;
            texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
            int minX=texture.width,minY=texture.height,maxX=-1,maxY=-1;
            Color32[] pixels=texture.GetPixels32();
            // Ignore nearly invisible alpha fringes while preserving the source PNG.
            for(int y=0;y<texture.height;y++)for(int x=0;x<texture.width;x++)
                if(pixels[y*texture.width+x].a>=64){minX=Mathf.Min(minX,x);minY=Mathf.Min(minY,y);maxX=Mathf.Max(maxX,x);maxY=Mathf.Max(maxY,y);}
            if(maxX<minX)return null;
            Rect bounds=new Rect(Mathf.Max(0,minX-2),Mathf.Max(0,minY-2),Mathf.Min(texture.width-1,maxX+2)-Mathf.Max(0,minX-2)+1,Mathf.Min(texture.height-1,maxY+2)-Mathf.Max(0,minY-2)+1);
            var graphic=new Graphic{Texture=texture,Pixels=bounds};cache[name]=graphic;return graphic;
        }
        public static bool Available(string name){return Load(name)!=null;}
        public static void Draw(Rect area,string name="title-logo")
        {
            Graphic graphic=Load(name);if(graphic==null)return;
            Rect pixels=graphic.Pixels;float scale=Mathf.Min(area.width/pixels.width,area.height/pixels.height);
            float width=Mathf.Round(pixels.width*scale),height=Mathf.Round(pixels.height*scale);
            Rect destination=new Rect(Mathf.Round(area.center.x-width*.5f),Mathf.Round(area.center.y-height*.5f),width,height);
            Color previous=GUI.color;GUI.color=Color.white;
            GUI.DrawTextureWithTexCoords(destination,graphic.Texture,new Rect(pixels.x/graphic.Texture.width,pixels.y/graphic.Texture.height,pixels.width/graphic.Texture.width,pixels.height/graphic.Texture.height),true);
            GUI.color=previous;
        }
    }
}
