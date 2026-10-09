using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ClubClash
{
    // Explicit capture mode only. Uses original game sprites and native game UI.
    public sealed class StoreScreenshotCapture : MonoBehaviour
    {
        const int Width=2778,Height=1284;
        static readonly string[] Left={"home","kendo","boxing","judo","shogi","golf","science","handball","swimming","baseball"};
        static readonly string[] Right={"volleyball","tennis","archery","music","art","basketball","badminton","calligraphy","soccer"};
        GameController game;Texture2D stage;bool poster;
        string directory,oldOne,oldTwo;int oldStage,oldLevel;
        MethodInfo drawGameGui;

        IEnumerator Start()
        {
            game=GetComponent<GameController>();game.enabled=false;AudioListener.volume=0;
            oldOne=PlayerPrefs.GetString("ccu.p1","boxing");oldTwo=PlayerPrefs.GetString("ccu.p2","soccer");
            oldStage=PlayerPrefs.GetInt("ccu.stage",0);oldLevel=PlayerPrefs.GetInt("ccu.difficulty",3);
            directory=Path.GetFullPath("AppStore/Screenshots/ja-JP/landscape");
            string[] args=Environment.GetCommandLineArgs();for(int i=0;i<args.Length-1;i++)if(args[i]=="-store-output")directory=Path.GetFullPath(args[i+1]);
            Directory.CreateDirectory(directory);
            stage=Resources.Load<Texture2D>("Art/stage");
            drawGameGui=typeof(GameController).GetMethod("OnGUI",BindingFlags.Instance|BindingFlags.NonPublic);
            Require(stage!=null,"Screenshot resources missing");
            Require(PixelLogo.Available("title-logo") && PixelLogo.Available("title-lockup"),"Pixel logo images missing");
            var unique=new HashSet<string>(Left);unique.UnionWith(Right);
            Require(Left.Length==10 && Right.Length==9 && unique.Count==19 && unique.Count==Catalog.Clubs.Count,"Roster must include every club exactly once");
            foreach(var club in Catalog.Clubs)Require(unique.Contains(club.Id),"Missing club "+club.Id);
            UnityEngine.Screen.SetResolution(Width,Height,FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(1);
            Require(UnityEngine.Screen.width==Width && UnityEngine.Screen.height==Height,"Unexpected capture viewport");
            game.ShowTitle();poster=false;yield return Photo("title-logo-proof",true);
            poster=true;yield return Photo("01-all-clubs-vs");
            poster=false;game.BeginBattleSelection();game.PlayerOne="kendo";game.PlayerTwo="music";game.SetStage(StageId.Ground);game.SetDifficulty(CpuDifficulty.Level3);game.StartBattle();
            Battle battle=game.CurrentBattle;
            for(int i=0;i<190;i++)battle.Update(1f/60f,InputFrame.Empty,InputFrame.Empty);
            Require(battle.Phase==BattlePhase.Fight,"Fight has not started");
            battle.Fighters[0].X=480;battle.Fighters[1].X=720;
            battle.Fighters[0].Facing=1;battle.Fighters[1].Facing=-1;
            foreach(var f in battle.Fighters){f.Y=0;f.Grounded=true;f.Hp=f.MaxHp;f.Vx=f.Vy=0;f.Hitstun=f.Flash=0;f.Guard=false;}
            foreach(var f in battle.Fighters)
            {
                MoveDefinition move=f.Club.StrongMove.Copy();
                f.Attack=new AttackState{Move=move,Button="b",Elapsed=move.Startup+move.Active*.25f,Total=move.Total};
                f.State=f.Animation="attackB";
            }
            game.Visuals.Render(.02f);yield return Photo("02-kendo-vs-music");
            game.BeginBattleSelection();game.PlayerOne="science";game.PlayerTwo="tennis";game.SetStage(StageId.Ground);game.SelectSide(0);
            Require(game.ScreenState==GameScreen.Selection && game.PlayerOne=="science" && game.PlayerTwo=="tennis","Wrong selection state");
            yield return Photo("03-science-vs-tennis-selection");
            File.WriteAllText(Path.Combine(directory,"capture.json"),"{\"width\":2778,\"height\":1284,\"leftCount\":10,\"rightCount\":9,\"uniqueClubs\":19,\"images\":3,\"source\":\"Unity native capture; first image is requested marketing composition\"}");
            RestorePreferences();Debug.Log("STORE_SCREENSHOTS_CAPTURED "+directory);Application.Quit();
        }

        void Require(bool condition,string message){if(condition)return;Debug.LogError(message);RestorePreferences();Application.Quit(1);throw new InvalidOperationException(message);}
        void RestorePreferences()
        {
            if(oldOne==null)return;
            PlayerPrefs.SetString("ccu.p1",oldOne);PlayerPrefs.SetString("ccu.p2",oldTwo);PlayerPrefs.SetInt("ccu.stage",oldStage);PlayerPrefs.SetInt("ccu.difficulty",oldLevel);PlayerPrefs.Save();
        }
        IEnumerator Photo(string name,bool proof=false)
        {
            yield return new WaitForEndOfFrame();
            Texture2D image=ScreenCapture.CaptureScreenshotAsTexture();
            Require(image.width==Width && image.height==Height,"Screenshot resolution does not match requested landscape size");
            string output=proof?Path.GetFullPath(Path.Combine(directory,"../../../../Verification/pixel-logo")):directory;
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Destroy(image);
            yield return null;
        }

        void OnGUI()
        {
            if(game==null)return;
            if(!poster){if(drawGameGui!=null)drawGameGui.Invoke(game,null);return;}
            GUI.depth=-200;
            Matrix4x4 previous=GUI.matrix;Color previousColor=GUI.color;
            float scale=UnityEngine.Screen.width/(float)Width;
            GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,new Vector3(scale,scale,1));
            GUI.color=new Color(.78f,.82f,.88f,1);GUI.DrawTexture(new Rect(0,0,Width,Height),stage,ScaleMode.ScaleAndCrop);GUI.color=Color.white;
            // Draw rear, middle, featured, then front rows so overlaps have depth.
            for(int row=0;row<3;row++)
            {
                for(int column=0;column<3;column++)
                {
                    int i=row*3+column;
                    if(i<9)DrawClub(Left[i],new Vector2(150+column*255,630+row*270),335,false);
                    if(i<8)DrawClub(Right[i],new Vector2(2070+column*267,630+row*270),335,true);
                }
                if(row==1)
                {
                    DrawClub("baseball",new Vector2(1010,950),580,false);
                    DrawClub("soccer",new Vector2(1768,950),580,true);
                }
            }
            PixelLogo.Draw(new Rect(699,24,1380,304),"title-lockup");
            PixelDisplay.Draw(new Rect(1213+8,634+10,352,238),"VS",new Color(.025f,.055f,.1f),32);
            PixelDisplay.Draw(new Rect(1213,634,352,238),"VS",new Color(1,.65f,.32f),32);
            GUI.matrix=previous;GUI.color=previousColor;GUI.depth=0;
        }
        void DrawClub(string id,Vector2 feet,float height,bool flip)
        {
            Sprite sprite=game.Visuals.Portrait(id);
            Require(sprite!=null,"Missing sprite "+id);
            Rect slot=new Rect(feet.x-150,feet.y-height*256/187f,300,height*256/187f);
            Rect target=game.Visuals.PortraitPlacement(id,sprite,slot),source=sprite.textureRect;
            Matrix4x4 previous=GUI.matrix;
            if(flip)GUIUtility.ScaleAroundPivot(new Vector2(-1,1),feet);
            Rect uv=new Rect(source.x/sprite.texture.width,source.y/sprite.texture.height,source.width/sprite.texture.width,source.height/sprite.texture.height);
            Color previousColor=GUI.color;
            GUI.color=new Color(0,0,0,.65f);
            GUI.DrawTextureWithTexCoords(new Rect(target.x+8,target.y+12,target.width,target.height),sprite.texture,uv);
            GUI.color=new Color(0,0,0,.92f);
            foreach(Vector2 offset in new[]{new Vector2(-4,0),new Vector2(4,0),new Vector2(0,-4),new Vector2(0,4)})
                GUI.DrawTextureWithTexCoords(new Rect(target.x+offset.x,target.y+offset.y,target.width,target.height),sprite.texture,uv);
            GUI.color=Color.white;GUI.DrawTextureWithTexCoords(target,sprite.texture,uv);
            GUI.color=previousColor;
            GUI.matrix=previous;
        }
    }
}
