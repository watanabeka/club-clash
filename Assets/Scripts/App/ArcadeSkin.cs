using UnityEngine;

namespace ClubClash
{
    // Code-native UI art: antialiased bevels, illuminated rims, and soft shadows.
    internal sealed class ArcadeSkin
    {
        public readonly Texture2D Disc, PressedDisc, StickBase, StickKnob, Gauge;

        public ArcadeSkin()
        {
            Disc=CreateDisc(false,false);PressedDisc=CreateDisc(true,false);
            StickBase=CreateDisc(false,true);StickKnob=CreateDisc(true,true);
            Gauge=new Texture2D(1,32,TextureFormat.RGBA32,false);
            Gauge.wrapMode=TextureWrapMode.Clamp;Gauge.filterMode=FilterMode.Bilinear;
            for(int y=0;y<32;y++)
            {
                float t=y/31f;
                float light=t>.78f?1.16f:.75f+t*.35f;
                Gauge.SetPixel(0,y,new Color(light,light,light,1));
            }
            Gauge.Apply(false,true);
        }

        static Texture2D CreateDisc(bool pressed,bool stick)
        {
            const int n=192;
            var texture=new Texture2D(n,n,TextureFormat.RGBA32,false);
            texture.filterMode=FilterMode.Bilinear;texture.wrapMode=TextureWrapMode.Clamp;
            var pixels=new Color[n*n];
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                float px=(x+.5f-n*.5f)/(n*.5f),py=(y+.5f-n*.5f)/(n*.5f);
                float r=Mathf.Sqrt(px*px+py*py),alpha=Mathf.Clamp01((1-r)*n*.5f);
                if(alpha<=0)continue;
                float shade;
                if(stick && !pressed)
                {
                    shade=r>.91f?.27f:r>.84f?.70f:r>.81f?.12f:.17f+(1-r)*.15f;
                    if(Mathf.Abs(r-.58f)<.013f)shade=.38f;
                    if((Mathf.Abs(px)<.012f||Mathf.Abs(py)<.012f)&&r<.68f)shade=.24f;
                }
                else
                {
                    shade=r>.96f?.15f:r>.89f?.73f+py*.2f:r>.84f?.22f:
                        (pressed?.47f:.59f)+py*.12f;
                    float gleam=Mathf.Exp(-((px+.23f)*(px+.23f)*6+(py-.46f)*(py-.46f)*35));
                    shade+=gleam*(pressed?.06f:.17f);
                    if(r<.76f && Mathf.Abs(r-.74f)<.01f)shade+=.07f;
                }
                pixels[y*n+x]=new Color(shade*.84f,shade*.93f,shade,alpha);
            }
            texture.SetPixels(pixels);texture.Apply(false,true);return texture;
        }

        public static void RoundRect(Rect rect,Color color,float radius=12)
        {
            GUI.DrawTexture(rect,Texture2D.whiteTexture,ScaleMode.StretchToFill,true,0,color,0,radius);
        }
        public static void Texture(Rect rect,Texture2D texture,Color tint)
        {
            Color before=GUI.color;GUI.color=tint;GUI.DrawTexture(rect,texture);GUI.color=before;
        }
        public static void Line(Vector2 a,Vector2 b,Color color,float width=2)
        {
            Matrix4x4 before=GUI.matrix;Vector2 direction=b-a;
            GUI.matrix=before*Matrix4x4.TRS(new Vector3(a.x,a.y,0),Quaternion.Euler(0,0,Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg),Vector3.one);
            Texture(new Rect(0,-width*.5f,direction.magnitude,width),Texture2D.whiteTexture,color);
            GUI.matrix=before;
        }
        public static void Arc(Vector2 center,float radius,float fraction,Color color,float width=3)
        {
            const int count=48;int segments=Mathf.CeilToInt(Mathf.Clamp01(fraction)*count);
            for(int i=0;i<segments;i++)
            {
                float a=-Mathf.PI*.5f+i*Mathf.PI*2/count;
                float b=-Mathf.PI*.5f+Mathf.Min(i+1,fraction*count)*Mathf.PI*2/count;
                Line(center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,
                    center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,color,width);
            }
        }
        public static void Construction(Rect rect,Color color)
        {
            Vector2 top=new Vector2(rect.center.x,rect.y+3),left=new Vector2(rect.x+3,rect.yMax-4),right=new Vector2(rect.xMax-3,rect.yMax-4);
            Line(top,left,color,3);Line(top,right,color,3);Line(left,right,color,3);
            Line(new Vector2(rect.center.x,rect.y+11),new Vector2(rect.center.x,rect.y+19),color,3);
            Texture(new Rect(rect.center.x-1.5f,rect.y+22,3,3),Texture2D.whiteTexture,color);
        }
        public void Dispose()
        {
            Object.Destroy(Disc);Object.Destroy(PressedDisc);Object.Destroy(StickBase);
            Object.Destroy(StickKnob);Object.Destroy(Gauge);
        }
    }
}
