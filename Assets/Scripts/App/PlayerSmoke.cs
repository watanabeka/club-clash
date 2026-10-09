using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ClubClash
{
    // Explicit command-line verification only; normal launches never inject input.
    public sealed class PlayerSmoke : MonoBehaviour
    {
        readonly List<string> checks=new List<string>();
        sealed class ReviewStore : IReviewStateStore
        {
            readonly Dictionary<string,int> values=new Dictionary<string,int>();
            public int Read(string key)=>values.TryGetValue(key,out var value)?value:0;
            public void Write(string key,int value){values[key]=value;}
            public void Save(){}
        }
        sealed class ReviewProbe : IReviewRequester
        {
            public int Calls;
            public bool Request(){Calls++;return true;}
        }
        sealed class ResultAdProbe : IInterstitialAds
        {
            public bool PrivacyOptionsRequired => false;
            public int Shows;
            public Action Closed;
            public void Show(Action closed){Shows++;Closed=closed;}
            public void ShowPrivacyOptions(Action closed){closed();}
        }
        GameController game;string directory;float bikeWhiffTravel;
        Sprite[] reviewFrames;string reviewClub;Texture2D reviewBackground;bool reviewProportions;
        int ClubIndex(string id){for(int i=0;i<Catalog.Clubs.Count;i++)if(Catalog.Clubs[i].Id==id)return i;throw new Exception("Missing club "+id);}
        bool BaseballShufflePose()
        {
            var method=typeof(BattleRenderer).GetMethod("Atlas",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            var poses=(Sprite[])method.Invoke(game.Visuals,new object[]{"baseball"});
            foreach(var renderer in game.Visuals.GetComponentsInChildren<SpriteRenderer>(true))
                if(renderer.name=="Animated Sprite" && renderer.transform.parent.name=="Fighter 1")return renderer.sprite==poses[8] || renderer.sprite==poses[9];
            return false;
        }
        Sprite PlayerBodySprite()
        {
            foreach(var renderer in game.Visuals.GetComponentsInChildren<SpriteRenderer>(true))
                if(renderer.name=="Animated Sprite" && renderer.transform.parent.name=="Fighter 1")return renderer.sprite;
            return null;
        }
        SpriteRenderer VisibleRenderer(string name)
        {
            foreach(var renderer in game.Visuals.GetComponentsInChildren<SpriteRenderer>(true))
                if(renderer.name==name && renderer.enabled && renderer.gameObject.activeInHierarchy)return renderer;
            return null;
        }
        bool SameBall(SpriteRenderer renderer,string id)
        {
            if(renderer==null || renderer.sprite!=game.Visuals.BallSprite(id))return false;
            float diameter=Mathf.Max(renderer.sprite.rect.width,renderer.sprite.rect.height)*renderer.transform.localScale.x;
            return Mathf.Abs(diameter-game.Visuals.BallDiameter(id))<.01f;
        }
        void OnGUI()
        {
            if(reviewFrames==null && !reviewProportions)return;GUI.depth=-100;
            if(reviewBackground==null){reviewBackground=new Texture2D(1,1);reviewBackground.SetPixel(0,0,new Color(.08f,.12f,.16f));reviewBackground.Apply();}
            float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,Vector3.one*scale);
            GUI.DrawTexture(new Rect(0,0,1280,720),reviewBackground);
            if(reviewProportions)
            {
                var labelStyle=new GUIStyle(GUI.skin.label){font=Resources.Load<Font>("Fonts/NotoSansCJKjp-Regular"),fontSize=20,alignment=TextAnchor.MiddleCenter};
                GUI.Label(new Rect(18,5,1244,30),"BODY PROPORTIONS / SAME BASEBALL REFERENCE SCALE",labelStyle);
                for(int i=0;i<Catalog.Clubs.Count;i++)
                {
                    var club=Catalog.Clubs[i];Rect cell=new Rect(18+i%5*250,42+i/5*164,244,160);
                    Sprite sprite=game.Visuals.Portrait(club.Id);Rect dst=game.Visuals.PortraitPlacement(club.Id,sprite,new Rect(cell.x,cell.y-16,cell.width,160));
                    Rect t=sprite.textureRect;GUI.DrawTextureWithTexCoords(dst,sprite.texture,new Rect(t.x/sprite.texture.width,t.y/sprite.texture.height,t.width/sprite.texture.width,t.height/sprite.texture.height));
                    GUI.Label(new Rect(cell.x,cell.y+141,cell.width,24),club.Name,labelStyle);
                }
                GUI.matrix=Matrix4x4.identity;GUI.depth=0;return;
            }
            GUI.Label(new Rect(18,5,600,30),"SPRITE REVIEW / "+reviewClub,new GUIStyle(GUI.skin.label){fontSize=21});
            for(int i=0;i<reviewFrames.Length;i++)
            {
                Sprite s=reviewFrames[i];if(s==null)continue;Rect cell=new Rect(13+i%6*210,42+i/6*167,202,157);
                float q=Mathf.Min((cell.width-12)/s.rect.width,(cell.height-25)/s.rect.height);
                Rect dst=new Rect(cell.center.x-s.rect.width*q*.5f,cell.yMax-5-s.rect.height*q,s.rect.width*q,s.rect.height*q);
                Rect t=s.textureRect;GUI.DrawTextureWithTexCoords(dst,s.texture,new Rect(t.x/s.texture.width,t.y/s.texture.height,t.width/s.texture.width,t.height/s.texture.height));
                GUI.Label(new Rect(cell.x,cell.y,100,20),i.ToString());
            }
            GUI.matrix=Matrix4x4.identity;GUI.depth=0;
        }
        void Check(string name,bool condition)
        {
            if(!condition)
            {
                string reason=name+" "+game.LastTextOverflow;
                File.WriteAllText(Path.Combine(directory,"failure.json"),JsonUtility.ToJson(new FailureReport { reason=reason,passed=checks.Count }));
                if(LaunchArguments.Has("-club-clash-smoke-exit"))Application.Quit(1);
                throw new Exception("Native check failed: "+reason);
            }
            checks.Add(name);
        }
        [Serializable] sealed class FailureReport { public string reason;public int passed; }
        IEnumerator Tap(Vector2 point)
        {
            game.TestTouchPoint=null;yield return null;
            game.TestTouchPoint=point;yield return new WaitForSeconds(.065f);
            game.TestTouchPoint=null;yield return new WaitForSeconds(.065f);
        }
        IEnumerator Photo(string name)
        {
            yield return new WaitForEndOfFrame();
            Check("text-fits-"+name,string.IsNullOrEmpty(game.LastTextOverflow));
            string path=Path.Combine(directory,name+".png");
            Texture2D capture=ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path,capture.EncodeToPNG());Destroy(capture);
            Check("screenshot-saved-"+name,File.Exists(path) && new FileInfo(path).Length>1000);
            yield return null;
        }

        AudioSource MusicSource()
        {
            foreach(var source in game.Visuals.GetComponentsInChildren<AudioSource>(true))if(source.loop)return source;
            return null;
        }
        AudioSource EffectsSource()
        {
            foreach(var source in game.Visuals.GetComponentsInChildren<AudioSource>(true))if(!source.loop)return source;
            return null;
        }
        bool MusicPlaying(){var source=MusicSource();return source!=null && source.isPlaying && !source.mute;}
        void TouchTargetAudit()
        {
            Rect inset=new Rect(59,21,726,369),view=GameController.FitDesignViewport(inset);
            Check("safe-area-viewport-contained",view.x>=inset.x && view.y>=inset.y && view.xMax<=inset.xMax+.001f && view.yMax<=inset.yMax+.001f);
            Check("safe-area-viewport-aspect-and-center",Mathf.Abs(view.width/view.height-1280f/720f)<.0001f && (view.center-inset.center).sqrMagnitude<.001f);
            Rect fallback=GameController.FitDesignViewport(new Rect(float.NaN,0,0,0));
            Check("invalid-safe-area-fallback",fallback.width>0 && fallback.height>0 && !float.IsNaN(fallback.x));
            var entries=new List<string>();
            var rects=new List<KeyValuePair<string,Rect>>();
            foreach(var field in typeof(GameController).GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static))
                if(field.FieldType==typeof(Rect) && field.Name!="TitleLocalRect" && !field.Name.EndsWith("PanelRect"))rects.Add(new KeyValuePair<string,Rect>(field.Name,(Rect)field.GetValue(null)));
            foreach(string fieldName in new[]{"jumpRect","guardRect","lightRect","strongRect"})
            {
                var field=typeof(GameController).GetField(fieldName,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                rects.Add(new KeyValuePair<string,Rect>(fieldName,(Rect)field.GetValue(game)));
            }
            for(int i=0;i<3;i++)rects.Add(new KeyValuePair<string,Rect>("stage-"+i,GameController.StageRect(i)));
            for(int i=0;i<5;i++)rects.Add(new KeyValuePair<string,Rect>("difficulty-"+i,GameController.DifficultyRect(i)));
            for(int i=0;i<Catalog.Clubs.Count;i++)
            {
                rects.Add(new KeyValuePair<string,Rect>("club-"+i,GameController.ClubSlotRect(i)));
                rects.Add(new KeyValuePair<string,Rect>("practice-club-"+i,GameController.PracticeChoiceRect(i)));
            }
            foreach(var entry in rects)
            {
                Rect r=entry.Value;float logicalMinimum=Mathf.Min(r.width,r.height)*390f/720f;float smallPhoneMinimum=Mathf.Min(r.width,r.height)*360f/720f;
                Check("360-height-touch-target-"+entry.Key,smallPhoneMinimum>=48 && r.x>=0 && r.y>=0 && r.xMax<=1280 && r.yMax<=720);
                entries.Add("{\"name\":\""+entry.Key+"\",\"width\":"+r.width+",\"height\":"+r.height+",\"logicalMinimumAt390\":"+logicalMinimum.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+"}");
            }
            File.WriteAllText(Path.Combine(directory,"touch-targets.json"),"{\"referenceWidth\":844,\"referenceHeight\":390,\"smallPhoneHeight\":360,\"minimum\":48,\"targets\":["+string.Join(",",entries.ToArray())+"]}");
        }
        IEnumerator Start()
        {
            game=GetComponent<GameController>();
            var reviewStore=new ReviewStore();var reviewProbe=new ReviewProbe();game.SetReviewService(reviewStore,reviewProbe);
            string originalOne=game.PlayerOne,originalTwo=game.PlayerTwo;
            StageId originalStage=game.Stage;CpuDifficulty originalLevel=game.Difficulty;CpuPersonality originalType=game.Personality;
            bool originalMusic=game.MusicEnabled,originalEffects=game.EffectsEnabled;
            directory=Application.platform==RuntimePlatform.IPhonePlayer?Path.Combine(Application.persistentDataPath,"Verification-v7"):Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../work/native-proof-v7"));
            string[] args=Environment.GetCommandLineArgs();for(int i=0;i<args.Length-1;i++)if(args[i]=="-club-clash-proof")directory=args[i+1];
            bool deferAudioDevice=LaunchArguments.Has("-club-clash-defer-hardware-audio");
            Directory.CreateDirectory(directory);yield return new WaitForSeconds(.8f);TouchTargetAudit();
            var disabledProperty=typeof(AudioSettings).GetProperty("unityAudioDisabled",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
            Debug.Log("AUDIO_INIT rate="+AudioSettings.outputSampleRate+" dsp="+AudioSettings.dspTime+" listenerPause="+AudioListener.pause+" listenerVolume="+AudioListener.volume+" disabled="+(disabledProperty==null?"unavailable":disabledProperty.GetValue(null).ToString())+" listenerCount="+FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length);
            Debug.Log("AUDIO_CONFIG "+JsonUtility.ToJson(AudioSettings.GetConfiguration()));
            if(LaunchArguments.Has("-club-clash-phone-proof"))
            {
                Check("phone-viewport",Screen.width>=640 && Screen.height>=350 && Screen.height<=2200);
                yield return Photo("phone-title");yield return Tap(GameController.TitleSettingsRect.center);yield return Photo("phone-settings");yield return Tap(GameController.SettingsCloseRect.center);yield return Tap(GameController.TitleHelpRect.center);yield return Photo("phone-help");yield return Tap(GameController.HelpCloseRect.center);
                game.BeginBattleSelection();game.PlayerOne="baseball";game.PlayerTwo="soccer";game.SetStage(StageId.Ground);game.SelectSide(0);
                yield return Photo("phone-select-player");game.SelectSide(1);yield return Photo("phone-select-opponent");
                game.PlayerOne="boxing";game.PlayerTwo="kendo";game.SetStage(StageId.Gym);yield return Photo("phone-select-gym");
                game.PlayerOne="science";game.PlayerTwo="shogi";game.SetStage(StageId.Classroom);yield return Photo("phone-select-classroom");
                game.PlayerOne="baseball";game.PlayerTwo="soccer";game.StartBattle();yield return Photo("phone-countdown");
                float phoneDeadline=Time.realtimeSinceStartup+5;
                while(game.CurrentBattle.Phase==BattlePhase.Countdown && Time.realtimeSinceStartup<phoneDeadline)yield return null;
                Check("phone-fight",game.CurrentBattle.Phase==BattlePhase.Fight);yield return Photo("phone-fight");
                game.CurrentBattle.Fighters[1].Hp=0;yield return new WaitForSeconds(.15f);Check("phone-ko-slows",game.PresentationTimeScale==.2f);yield return Photo("phone-ko1");
                yield return new WaitForSeconds(5);Check("phone-round2",game.CurrentBattle.Round==2 && game.CurrentBattle.Phase==BattlePhase.Fight);
                game.CurrentBattle.Fighters[0].Hp=0;yield return new WaitForSeconds(.15f);yield return Photo("phone-ko2");
                yield return new WaitForSeconds(5);Check("phone-round3",game.CurrentBattle.Round==3 && game.CurrentBattle.Phase==BattlePhase.Fight);
                game.CurrentBattle.Fighters[0].Hp=0;yield return new WaitForSeconds(.15f);Check("phone-final-ko-slows",game.PresentationTimeScale==.2f);yield return Photo("phone-ko3");
                yield return new WaitForSeconds(1.9f);Check("phone-result-lose",game.ResultCaption=="LOSE" && game.CurrentBattle.Wins[1]==2);yield return Photo("phone-result-lose");
                game.ShowTitle();yield return Tap(GameController.TitlePracticeRect.center);yield return Photo("phone-practice");
                yield return Tap(GameController.PracticeClubRect.center);yield return Photo("phone-practice-picker");
                game.ShowTitle();game.PlayerOne=originalOne;game.PlayerTwo=originalTwo;game.Stage=originalStage;game.Difficulty=originalLevel;game.Personality=originalType;
                PlayerPrefs.SetString("ccu.p1",originalOne);PlayerPrefs.SetString("ccu.p2",originalTwo);PlayerPrefs.SetInt("ccu.stage",(int)originalStage);PlayerPrefs.SetInt("ccu.difficulty",(int)originalLevel);PlayerPrefs.Save();
                File.WriteAllText(Path.Combine(directory,"phone-smoke.json"),"{\"passed\":"+checks.Count+",\"failed\":0,\"width\":"+Screen.width+",\"height\":"+Screen.height+"}");
                Debug.Log("CLUB_CLASH_PHONE_SMOKE_PASS "+directory);if(LaunchArguments.Has("-club-clash-smoke-exit"))Application.Quit();yield break;
            }
            Check("boot-title",game.ScreenState==GameScreen.Title && !game.InBattle);
            Check("music-loaded-and-title-silent",MusicSource()!=null && MusicSource().clip!=null && MusicSource().clip.loadState==AudioDataLoadState.Loaded && !MusicPlaying());
            Check("nineteen-playable-clubs-home-first",Catalog.Clubs.Count==19 && Catalog.Clubs[0].Id=="home");
            foreach(var club in Catalog.Clubs)Check("loaded-art-"+club.Id,game.Visuals.Portrait(club.Id)!=null);
            Check("teacher-art-hidden-from-roster",game.Visuals.Portrait("teacher")!=null && Catalog.Teacher.Id=="teacher");
            Check("animated-idle-preview",game.Visuals.IdlePortrait("boxing",0)!=game.Visuals.IdlePortrait("boxing",.56f));
            yield return Photo("title");yield return Tap(GameController.TitleSettingsRect.center);
            Check("title-settings-opens",game.TitleSettingsOpen && !game.TitleHelpOpen);yield return Photo("settings");
            bool musicBefore=game.MusicEnabled,effectsBefore=game.EffectsEnabled;
            yield return Tap(GameController.BgmToggleRect.center);
            Check("bgm-toggle-independent-and-persisted",game.MusicEnabled!=musicBefore && game.EffectsEnabled==effectsBefore && PlayerPrefs.GetInt("ccu.music",-1)==(game.MusicEnabled?1:0));
            Check("bgm-source-setting-independent",EffectsSource()!=null && game.Visuals.AudioDiagnostics.EffectsEnabled==effectsBefore && game.Visuals.AudioDiagnostics.MusicEnabled==game.MusicEnabled);
            yield return Tap(GameController.EffectsToggleRect.center);
            Check("sfx-toggle-independent-and-persisted",game.EffectsEnabled!=effectsBefore && game.MusicEnabled!=musicBefore && PlayerPrefs.GetInt("ccu.effects",-1)==(game.EffectsEnabled?1:0));
            Check("sfx-source-setting-independent",game.Visuals.AudioDiagnostics.EffectsEnabled==game.EffectsEnabled && game.Visuals.AudioDiagnostics.MusicEnabled==game.MusicEnabled);
            if(!game.MusicEnabled)yield return Tap(GameController.BgmToggleRect.center);
            if(!game.EffectsEnabled)yield return Tap(GameController.EffectsToggleRect.center);
            yield return Tap(GameController.SettingsCloseRect.center);Check("settings-close",!game.TitleSettingsOpen);
            yield return Tap(GameController.TitleHelpRect.center);Check("title-help-opens",game.TitleHelpOpen && !game.TitleSettingsOpen);yield return Photo("help");
            yield return Tap(GameController.HelpCloseRect.center);Check("help-close",!game.TitleHelpOpen);
            yield return Tap(GameController.TitleSettingsRect.center);yield return Tap(GameController.BgmToggleRect.center);yield return Tap(GameController.SettingsCloseRect.center);
            game.StartBattle();float mutedFightDeadline=Time.realtimeSinceStartup+5;
            while(game.CurrentBattle.Phase==BattlePhase.Countdown && Time.realtimeSinceStartup<mutedFightDeadline)yield return null;
            yield return null;Check("bgm-off-prevents-real-fight-playback",game.CurrentBattle.Phase==BattlePhase.Fight && !game.MusicEnabled && !MusicPlaying() && game.Visuals.AudioDiagnostics.EffectsEnabled);
            game.TestTouchPoint=GameController.JumpControlPoint;yield return new WaitForSeconds(.035f);game.TestTouchPoint=null;
            Check("bgm-off-keeps-effects-active",EffectsSource().isPlaying && !EffectsSource().mute && game.CurrentBattle.Fighters[0].Y>0);
            game.ShowTitle();yield return Tap(GameController.TitleSettingsRect.center);yield return Tap(GameController.BgmToggleRect.center);yield return Tap(GameController.EffectsToggleRect.center);yield return Tap(GameController.SettingsCloseRect.center);
            game.StartBattle();mutedFightDeadline=Time.realtimeSinceStartup+5;
            while(game.CurrentBattle.Phase==BattlePhase.Countdown && Time.realtimeSinceStartup<mutedFightDeadline)yield return null;
            yield return null;Check("sfx-off-keeps-real-fight-bgm",game.CurrentBattle.Phase==BattlePhase.Fight && MusicPlaying() && !game.EffectsEnabled);
            game.TestTouchPoint=GameController.JumpControlPoint;yield return new WaitForSeconds(.035f);game.TestTouchPoint=null;
            Check("sfx-off-silences-jump-effect",!EffectsSource().isPlaying && EffectsSource().mute);
            game.ShowTitle();yield return Tap(GameController.TitleSettingsRect.center);yield return Tap(GameController.EffectsToggleRect.center);yield return Tap(GameController.SettingsCloseRect.center);
            yield return Tap(GameController.TitleLocalRect.center);
            Check("construction-two-player-cannot-start",game.ScreenState==GameScreen.Title && !game.InBattle);
            Check("two-player-support-dialog-opens",game.SupportDialogOpen);yield return Photo("two-player-support");
            yield return Tap(GameController.TitleBattleRect.center);Check("support-dialog-blocks-underlying-input",game.ScreenState==GameScreen.Title && game.SupportDialogOpen);
            yield return Tap(GameController.SupportCloseRect.center);Check("support-close-does-not-review",!game.SupportDialogOpen && reviewProbe.Calls==0);
            yield return Tap(GameController.TitleLocalRect.center);yield return Tap(GameController.SupportReviewRect.center);
            yield return new WaitForSecondsRealtime(.65f);
            Check("support-review-action-saved",reviewProbe.Calls==1 && game.ReviewSupportRequested && !game.ReviewRequestPending);
            yield return Tap(GameController.TitleLocalRect.center);yield return Photo("two-player-supported");
            yield return Tap(GameController.SupportReviewRect.center);Check("support-repeat-action-hidden",game.SupportDialogOpen && reviewProbe.Calls==1);
            yield return Tap(GameController.SupportOnlyCloseRect.center);Check("support-only-close",!game.SupportDialogOpen);
            // Keep the remaining smoke scenario independent from review timing; the
            // three-match request and persistence are covered by focused review checks.
            yield return Tap(GameController.TitleBattleRect.center);
            Check("battle-button-opens-selection",game.ScreenState==GameScreen.Selection && game.SelectingSide==0);
            Check("twenty-slots-two-rows-ten-columns",GameController.ClubSlotRect(9).y==GameController.ClubSlotRect(0).y && GameController.ClubSlotRect(10).x==GameController.ClubSlotRect(0).x && GameController.ClubSlotRect(19).y>GameController.ClubSlotRect(9).y);
            string before=game.PlayerOne,opponent=game.PlayerTwo;
            yield return Tap(GameController.ClubSlotRect(19).center);
            Check("black-reserved-slots-disabled",game.PlayerOne==before && game.PlayerTwo==opponent && game.SelectingSide==0);
            yield return Tap(GameController.ClubSlotRect(ClubIndex("tennis")).center);
            Check("own-choice-advances-to-cpu",game.PlayerOne=="tennis" && game.SelectingSide==1);
            yield return Tap(GameController.ClubSlotRect(ClubIndex("swimming")).center);
            Check("manual-cpu-choice",game.PlayerTwo=="swimming" && !game.OpponentRandom);
            Check("opponent-selection-heading",game.SelectionHeading=="相手の部活");
            yield return Tap(GameController.PlayerSideRect.center);Check("full-player-card-selects-player",game.SelectingSide==0 && game.SelectionHeading=="自分の部活");
            yield return Tap(GameController.OpponentSideRect.center);Check("full-opponent-card-selects-opponent",game.SelectingSide==1);
            game.OpenSettings();Check("settings-not-routed-from-selection",!game.TitleSettingsOpen && game.MusicEnabled && game.EffectsEnabled);
            game.OpenHelp();Check("help-not-routed-from-selection",!game.TitleHelpOpen);
            var rollTimes=new List<float>();Action<int,string> rollHandler=(tick,id)=>rollTimes.Add(Time.realtimeSinceStartup);
            game.OpponentRollTick+=rollHandler;yield return Tap(GameController.RandomOpponentRect.center);
            Check("random-opponent-starts-animated-roll",game.IsOpponentRolling && game.OpponentRandom);
            string rollOwn=game.PlayerOne;StageId rollStage=game.Stage;
            yield return Tap(GameController.ClubSlotRect(ClubIndex("home")).center);yield return Tap(GameController.StageRect(((int)rollStage+1)%3).center);
            Check("rolling-locks-other-selection-input",game.PlayerOne==rollOwn && game.Stage==rollStage);
            // PNG capture stalls the main thread, so do not capture during cadence measurement.
            float rollDeadline=Time.realtimeSinceStartup+4;
            while(game.IsOpponentRolling && Time.realtimeSinceStartup<rollDeadline)yield return null;
            game.OpponentRollTick-=rollHandler;
            Check("random-roll-stays-on-selection",!game.InBattle && game.ScreenState==GameScreen.Selection && !game.IsOpponentRolling && rollTimes.Count==GameController.OpponentRollTotalTicks && game.OpponentRollTicks==GameController.OpponentRollTotalTicks );
            Check("random-opponent-resolves-playable",game.OpponentRandom && Catalog.Get(game.PlayerTwo).Id==game.PlayerTwo && game.PlayerTwo!="teacher");
            float rollGapSum=0,rollMaxJitter=0;
            for(int i=1;i<rollTimes.Count;i++){float gap=rollTimes[i]-rollTimes[i-1];rollGapSum+=gap;rollMaxJitter=Mathf.Max(rollMaxJitter,Mathf.Abs(gap-.12f));}
            File.WriteAllText(Path.Combine(directory,"random-roll.json"),"{\"ticks\":"+rollTimes.Count+",\"meanInterval\":"+(rollGapSum/(rollTimes.Count-1)).ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+",\"maxJitter\":"+rollMaxJitter.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+"}");
            Check("random-roll-visible-cadence",Mathf.Abs(rollGapSum/(rollTimes.Count-1)-.12f)<.025f && rollMaxJitter<.09f);
            string decided=game.PlayerTwo;yield return new WaitForSeconds(.5f);
            Check("random-final-choice-stays-fixed",game.PlayerTwo==decided && !game.InBattle);yield return Photo("selection-random-decided");
            yield return Tap(GameController.SelectionStartRect.center);Check("random-battle-only-start-button",game.InBattle && game.CurrentBattle.Fighters[1].ClubId==decided);
            game.ShowSelection();game.SelectSide(1);
            yield return Tap(GameController.ClubSlotRect(ClubIndex("boxing")).center);Check("manual-choice-clears-random",game.PlayerTwo=="boxing" && !game.OpponentRandom);
            for(int i=0;i<5;i++){yield return Tap(GameController.DifficultyRect(i).center);Check("cpu-level-ui-"+(i+1),(int)game.Difficulty==i+1);}
            yield return Tap(GameController.DifficultyRect(2).center);
            for(int stage=0;stage<3;stage++)
            {
                yield return Tap(GameController.StageRect(stage).center);Check("stage-ui-"+stage,(int)game.Stage==stage);
                foreach(var club in Catalog.Clubs)
                {
                    game.PlayerOne=game.PlayerTwo=club.Id;bool buffed=club.HomeStage==(StageId)stage;
                    Check("stage-buff-card-both-sides-"+stage+"-"+club.Id,game.SelectionBuffed(0)==buffed && game.SelectionBuffed(1)==buffed);
                }
                game.PlayerOne=stage==0?"baseball":stage==1?"science":"boxing";game.PlayerTwo=stage==0?"soccer":stage==1?"shogi":"kendo";
                yield return Photo("selection-stage-"+stage);
            }
            game.PlayerOne="baseball";game.PlayerTwo="boxing";yield return Photo("selection");yield return Tap(GameController.SelectionStartRect.center);
            Check("configured-cpu-match-starts",game.InBattle && game.Mode==BattleMode.Cpu && game.CurrentBattle.Difficulty==CpuDifficulty.Level3 && game.CurrentBattle.EffectiveCpuPersonality!=CpuPersonality.Random && game.CurrentBattle.Stage==StageId.Gym);
            Check("countdown-three-music-silent",game.CurrentBattle.Phase==BattlePhase.Countdown && !MusicPlaying() && game.CurrentBattle.Time==60);yield return Photo("countdown-3");
            yield return new WaitForSeconds(1.01f);Check("countdown-two-music-silent",!MusicPlaying() && game.CurrentBattle.Time==60);yield return Photo("countdown-2");
            yield return new WaitForSeconds(1.01f);Check("countdown-one-music-silent",!MusicPlaying() && game.CurrentBattle.Time==60);yield return Photo("countdown-1");
            float fightDeadline=Time.realtimeSinceStartup+2;
            while(game.CurrentBattle.Phase==BattlePhase.Countdown && Time.realtimeSinceStartup<fightDeadline)yield return null;
            yield return null;Check("cpu-match-fight-phase",game.CurrentBattle.Phase==BattlePhase.Fight);Check("music-starts-on-fight",MusicPlaying() && MusicSource().timeSamples<MusicSource().clip.frequency);yield return Photo("fight");
            var musicSource=MusicSource();Check("music-preloaded-loop",musicSource.loop && musicSource.clip.loadType==AudioClipLoadType.DecompressOnLoad);
            if(deferAudioDevice)
            {
                File.WriteAllText(Path.Combine(directory,"audio-native.json"),"{\"hardwarePlaybackDeferred\":true,\"reason\":\"Mac is locked; native DSP clock is0. Phase and settings commands continue to be tested.\",\"clip\":\""+musicSource.clip.name+"\",\"samples\":"+musicSource.clip.samples+",\"sampleRate\":"+musicSource.clip.frequency+",\"channels\":"+musicSource.clip.channels+"}");
            }
            else
            {
            int seekTarget=musicSource.clip.samples-musicSource.clip.frequency/3;
            musicSource.timeSamples=seekTarget;yield return new WaitForSeconds(.05f);
            int beforeWrap=musicSource.timeSamples;
            Check("native-bgm-seek-reaches-tail",beforeWrap>=seekTarget-4096 && MusicPlaying());
            float wrapDeadline=Time.realtimeSinceStartup+1.5f;
            while(MusicPlaying() && musicSource.timeSamples>=musicSource.clip.frequency && Time.realtimeSinceStartup<wrapDeadline)yield return null;
            yield return null; // Let LateUpdate observe the audio thread's wrap as well.
            Debug.Log("MUSIC_WRAP before="+beforeWrap+" after="+musicSource.timeSamples+" total="+musicSource.clip.samples+" rate="+musicSource.clip.frequency+" playing="+MusicPlaying()+" loops="+game.Visuals.AudioDiagnostics.MusicLoopCount+" phase="+game.CurrentBattle.Phase+" paused="+game.CurrentBattle.Paused+" dsp="+AudioSettings.dspTime);
            Check("native-bgm-loop-wraps-without-stop",MusicPlaying() && musicSource.timeSamples<beforeWrap && musicSource.timeSamples<musicSource.clip.frequency && game.Visuals.AudioDiagnostics.MusicLoopCount>0);
            float[] output=new float[1024];musicSource.GetOutputData(output,0);float squared=0;foreach(float sample in output)squared+=sample*sample;float outputRms=Mathf.Sqrt(squared/output.Length);
            Check("native-bgm-output-nonzero-after-loop",outputRms>.001f);
            File.WriteAllText(Path.Combine(directory,"audio-native.json"),"{\"clip\":\""+musicSource.clip.name+"\",\"samples\":"+musicSource.clip.samples+",\"sampleRate\":"+musicSource.clip.frequency+",\"channels\":"+musicSource.clip.channels+",\"loopWrapped\":true,\"outputRmsAfterLoop\":"+outputRms.ToString("0.######",System.Globalization.CultureInfo.InvariantCulture)+"}");
            }
            yield return Photo("battle");yield return Tap(GameController.PauseRect.center);float clock=game.CurrentBattle.Time;int pausedSamples=musicSource.timeSamples;yield return new WaitForSeconds(.15f);
            Check("pause-button-freezes-time",game.CurrentBattle.Paused && game.CurrentBattle.Time==clock);Check("pause-freezes-bgm-position",!MusicPlaying() && musicSource.timeSamples==pausedSamples);yield return Photo("pause");
            yield return Tap(GameController.PauseResumeRect.center);Check("pause-resume-button",!game.CurrentBattle.Paused);Check("resume-continues-bgm",MusicPlaying() && musicSource.timeSamples>=pausedSamples && musicSource.timeSamples<pausedSamples+musicSource.clip.frequency);
            game.CurrentBattle.Fighters[1].Hp=0;yield return new WaitForSeconds(.15f);Check("ko-awards-round",game.CurrentBattle.Wins[0]==1);Check("first-ko-slows-game",game.PresentationTimeScale==.2f);Check("ko-replaces-fight-banner",game.BattleBannerCaption=="KO");yield return Photo("ko-round1");Check("round-end-stops-bgm",!MusicPlaying());
            yield return new WaitForSeconds(5f);Check("next-round-restarts",game.CurrentBattle.Round==2 && game.CurrentBattle.Phase==BattlePhase.Fight);Check("next-fight-restarts-bgm-from-start",MusicPlaying() && MusicSource().timeSamples<MusicSource().clip.frequency);
            game.CurrentBattle.Fighters[1].Hp=0;yield return new WaitForSeconds(.15f);Check("final-ko-slows-game",game.PresentationTimeScale==.2f);Check("final-ko-visible-before-result",game.BattleBannerCaption=="KO");yield return Photo("ko-round2");yield return new WaitForSeconds(1.9f);Check("two-wins-match-result",game.CurrentBattle.Phase==BattlePhase.MatchOver && game.CurrentBattle.Winner==0);Check("match-result-stops-bgm",!MusicPlaying());
            Check("result-player-win",game.ResultCaption=="WIN");yield return Photo("result");
            var resultAds=new ResultAdProbe();game.SetResultAdService(resultAds);var completedMatch=game.CurrentBattle;
            yield return Tap(GameController.ResultRetryRect.center);
            Check("result-ad-rematch-waits-for-close",game.ResultTransitionPending && game.CurrentBattle==completedMatch && resultAds.Shows==1);
            yield return Tap(GameController.ResultTitleRect.center);Check("result-ad-repeat-tap-ignored",resultAds.Shows==1 && game.CurrentBattle==completedMatch);
            resultAds.Closed();var rematch=game.CurrentBattle;resultAds.Closed();
            Check("result-ad-close-navigates-once",!game.ResultTransitionPending && game.CurrentBattle==rematch && game.CurrentBattle!=completedMatch);
            Check("result-rematch-button",game.CurrentBattle.Wins[0]==0 && game.CurrentBattle.Phase==BattlePhase.Countdown);
            yield return new WaitForSeconds(3.3f);game.CurrentBattle.Wins[0]=1;game.CurrentBattle.Fighters[1].Hp=0;yield return new WaitForSeconds(2f);
            yield return Tap(GameController.ResultTitleRect.center);Check("result-ad-title-waits-for-close",game.ResultTransitionPending && game.InBattle && resultAds.Shows==2);
            resultAds.Closed();Check("result-ad-title-after-close",game.ScreenState==GameScreen.Title && !game.ResultTransitionPending);
            game.ShowTitle();game.Mode=BattleMode.Local;game.StartBattle();Check("legacy-local-start-normalized-to-cpu",game.Mode==BattleMode.Cpu && game.CurrentBattle.Mode==BattleMode.Cpu);
            game.ShowTitle();game.PlayerOne="boxing";game.Stage=StageId.Ground;yield return Tap(GameController.TitlePracticeRect.center);
            Check("title-practice-starts-immediately",game.InBattle && game.Mode==BattleMode.Practice && game.CurrentBattle.Phase==BattlePhase.Fight);Check("practice-bgm-silent",!MusicPlaying());
            Check("practice-opponent-is-teacher",game.CurrentBattle.Fighters[1].ClubId=="teacher");yield return Photo("practice");
            float x=game.CurrentBattle.Fighters[0].X;
            game.TestTouchPoint=new Vector2(130,550);yield return new WaitForSeconds(.15f);Check("stick-up-has-no-action",Mathf.Abs(game.CurrentBattle.Fighters[0].X-x)<1 && game.CurrentBattle.Fighters[0].Y==0);
            game.TestTouchPoint=null;yield return null;game.TestTouchPoint=new Vector2(194,620);yield return new WaitForSeconds(.35f);Check("horizontal-stick-moves",game.CurrentBattle.Fighters[0].X>x+60);
            game.TestTouchPoint=null;yield return null;game.TestTouchPoint=GameController.JumpControlPoint;yield return new WaitForSeconds(.10f);Check("round-jump-button",game.CurrentBattle.Fighters[0].Y>20);
            game.TestTouchPoint=null;yield return new WaitForSeconds(.95f);game.TestTouchPoint=GameController.GuardControlPoint;yield return new WaitForSeconds(.1f);Check("round-guard-button",game.CurrentBattle.Fighters[0].Guard);
            game.TestTouchPoint=null;yield return null;game.CurrentBattle.Fighters[0].X=780;int hits=game.CurrentBattle.PracticeHits;
            yield return Tap(GameController.LightControlPoint);yield return new WaitForSeconds(.10f);Check("round-light-hits-teacher",game.CurrentBattle.PracticeHits>hits);
            Check("teacher-remains-standing-in-place",game.CurrentBattle.Fighters[1].X==870 && game.CurrentBattle.Fighters[1].Y==0 && game.CurrentBattle.Fighters[1].Hp==120);yield return Photo("practice-hit");
            yield return Tap(GameController.PracticeClubRect.center);Check("round-avatar-opens-picker",game.PracticePickerOpen && game.CurrentBattle.Paused);yield return Photo("practice-picker");
            x=game.CurrentBattle.Fighters[0].X;yield return Tap(GameController.PracticeChoiceRect(ClubIndex("swimming")).center);
            Check("round-icon-switches-swimming",!game.PracticePickerOpen && !game.CurrentBattle.Paused && game.CurrentBattle.Fighters[0].ClubId=="swimming" && game.CurrentBattle.Fighters[0].X==x);
            Check("practice-choice-persists",PlayerPrefs.GetString("ccu.p1")=="swimming");
            game.CurrentBattle.Fighters[0].X=350;game.TestTouchPoint=GameController.StrongControlPoint;yield return new WaitForSeconds(.35f);
            Check("round-strong-fires-water",game.CurrentBattle.Projectiles.Count>0 && game.CurrentBattle.Projectiles[0].Kind=="water");game.TestTouchPoint=null;yield return new WaitForSeconds(.6f);yield return Photo("practice-water");
            foreach(var club in Catalog.Clubs)
            {
                yield return Tap(GameController.PracticeClubRect.center);int index=0;for(int i=0;i<Catalog.Clubs.Count;i++)if(Catalog.Clubs[i].Id==club.Id)index=i;
                yield return Tap(GameController.PracticeChoiceRect(index).center);
                Check("practice-live-choice-"+club.Id,game.CurrentBattle.Fighters[0].ClubId==club.Id && game.CurrentBattle.Fighters[1].ClubId=="teacher" && !game.PracticePickerOpen && !game.CurrentBattle.Paused);
                if(club.Id=="home" || club.Id=="shogi" || club.Id=="handball" || club.Id=="badminton")yield return Photo("practice-"+club.Id);
            }
            game.Pause(true);game.OpenPracticePicker();game.ClosePracticePicker();Check("picker-preserves-existing-pause",game.CurrentBattle.Paused);game.Pause(false);
            game.OpenPracticePicker();Rect corner=GameController.PracticeChoiceRect(0);yield return Tap(new Vector2(corner.x+.5f,corner.y+.5f));Check("picker-circle-corner-is-not-choice",game.PracticePickerOpen);
            yield return Tap(GameController.PracticePickerCloseRect.center);Check("picker-close-button-resumes",!game.PracticePickerOpen && !game.CurrentBattle.Paused);
            game.ChoosePracticeClub("science");game.CurrentBattle.Fighters[0].X=350;game.TestTouchPoint=GameController.StrongControlPoint;yield return new WaitForSeconds(.56f);
            game.TestTouchPoint=null;yield return Photo("practice-bottle");float deadline=Time.realtimeSinceStartup+4;
            while(!game.CurrentBattle.Fighters[1].IsPoisoned && Time.realtimeSinceStartup<deadline)yield return null;
            Check("teacher-accepts-bottle-poison",game.CurrentBattle.Fighters[1].IsPoisoned);yield return Photo("practice-poison");yield return new WaitForSeconds(7.2f);
            Check("teacher-poison-never-kills-or-moves",game.CurrentBattle.Fighters[1].Hp==120 && game.CurrentBattle.Fighters[1].X==870 && game.CurrentBattle.Phase==BattlePhase.Fight);
            Check("practice-clock-remains-untimed",game.CurrentBattle.Time==60 && game.CurrentBattle.Wins[0]==0 && game.CurrentBattle.Wins[1]==0);
            foreach(string id in new[]{"soccer","baseball","volleyball","tennis","golf","handball","basketball","badminton"})
            {
                game.ChoosePracticeClub(id);game.CurrentBattle.Fighters[0].X=350;
                float startup=game.CurrentBattle.Fighters[0].Club.StrongMove.Startup;
                game.TestTouchPoint=GameController.StrongControlPoint;yield return new WaitForSeconds(startup*.84f);
                Check("held-canonical-ball-and-diameter-"+id,SameBall(VisibleRenderer("Same Ball Before Release"),id));yield return Photo("projectile-"+id+"-contact");
                yield return new WaitForSeconds(startup*.16f+.04f);game.TestTouchPoint=null;
                Check("projectile-visible-after-release-"+id,game.CurrentBattle.Projectiles.Count>0);
                Check("released-same-ball-and-diameter-"+id,SameBall(VisibleRenderer("Ball or Club Projectile"),id) && VisibleRenderer("Same Ball Before Release")==null);
                yield return Photo("projectile-"+id+"-flight");yield return null;
            }
            game.ChoosePracticeClub("baseball");game.CurrentBattle.Fighters[0].X=350;game.TestTouchPoint=new Vector2(194,620);yield return new WaitForSeconds(.20f);Check("forward-movement-uses-shuffle-pose",BaseballShufflePose());yield return Photo("practice-shuffle-forward");
            x=game.CurrentBattle.Fighters[0].X;game.TestTouchPoint=null;yield return null;game.TestTouchPoint=new Vector2(66,620);yield return new WaitForSeconds(.35f);
            Check("backward-shuffle-keeps-facing-opponent",game.CurrentBattle.Fighters[0].X<x && game.CurrentBattle.Fighters[0].Facing>0 && BaseballShufflePose());yield return Photo("practice-shuffle-backward");game.TestTouchPoint=null;yield return null;
            game.ChoosePracticeClub("home");game.CurrentBattle.Fighters[0].X=60;hits=game.CurrentBattle.PracticeHits;
            game.TestTouchPoint=GameController.StrongControlPoint;yield return new WaitForSeconds(.50f);
            Check("home-bicycle-rides-with-actor",game.CurrentBattle.Fighters[0].IsBicycling && game.CurrentBattle.Fighters[0].X>200);yield return Photo("practice-bike-charge");
            game.TestTouchPoint=null;float bikeDeadline=Time.realtimeSinceStartup+3;
            while(game.CurrentBattle.Fighters[0].Attack!=null && Time.realtimeSinceStartup<bikeDeadline)yield return null;
            bikeWhiffTravel=game.CurrentBattle.Fighters[0].X-60;
            Debug.Log("CLUB_CLASH_BIKE_WHIFF_TRAVEL "+bikeWhiffTravel);
            // Native frames contain a variable remainder after 1/120s substeps;
            // mounting/end sampling can shorten travel by at most one substep.
            Check("home-bicycle-finite-range",!game.CurrentBattle.Fighters[0].IsBicycling && bikeWhiffTravel>=632 && bikeWhiffTravel<=640.01f && game.CurrentBattle.PracticeHits==hits);yield return Photo("practice-bike-ended");
            game.ChoosePracticeClub("home");game.CurrentBattle.Fighters[0].X=600;hits=game.CurrentBattle.PracticeHits;
            game.TestTouchPoint=GameController.StrongControlPoint;yield return new WaitForSeconds(.7f);game.TestTouchPoint=null;
            Check("home-bicycle-hits-teacher-and-disappears",game.CurrentBattle.PracticeHits==hits+1 && !game.CurrentBattle.Fighters[0].IsBicycling);yield return Photo("practice-bike-hit");
            game.TestTouchPoint=null;yield return null;game.ChoosePracticeClub("home");game.CurrentBattle.Fighters[0].X=350;
            game.TestTouchPoint=GameController.JumpControlPoint;yield return new WaitForSeconds(.10f);game.TestTouchPoint=null;yield return null;
            game.TestTouchPoint=GameController.StrongControlPoint;yield return new WaitForSeconds(.28f);
            Check("home-air-strong-is-kick-not-bicycle",game.CurrentBattle.Fighters[0].Attack!=null && game.CurrentBattle.Fighters[0].Attack.Air && !game.CurrentBattle.Fighters[0].IsBicycling && PlayerBodySprite().name=="home frame 13");
            yield return Photo("practice-home-air-kick");game.TestTouchPoint=null;
            game.ShowTitle();game.BeginBattleSelection();game.PlayerOne="baseball";game.PlayerTwo="soccer";yield return Photo("selection-baseball-soccer");
            game.PlayerTwo="volleyball";yield return Photo("selection-baseball-volleyball");game.PlayerTwo="shogi";yield return Photo("selection-baseball-shogi");
            game.ShowTitle();reviewProportions=true;yield return Photo("proportions-all-clubs");reviewProportions=false;
            var atlasMethod=typeof(BattleRenderer).GetMethod("Atlas",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            foreach(var club in Catalog.Clubs){reviewClub=club.Id;reviewFrames=(Sprite[])atlasMethod.Invoke(game.Visuals,new object[]{club.Id});Check("all-24-slices-"+club.Id,reviewFrames!=null && reviewFrames.Length==24);yield return Photo("sprites-"+club.Id);}
            reviewFrames=null;
            game.ShowTitle();yield return Tap(GameController.TitleBattleRect.center);yield return Tap(GameController.DifficultyRect(4).center);yield return Photo("selection-level5");
            yield return Tap(GameController.SelectionStartRect.center);yield return new WaitForSeconds(3.3f);Check("level5-match-runs",game.CurrentBattle.Difficulty==CpuDifficulty.Level5 && game.CurrentBattle.Phase==BattlePhase.Fight);Check("level5-bgm-playing",MusicPlaying());yield return Photo("battle-level5");game.ShowTitle();yield return null;Check("return-title-stops-bgm",!MusicPlaying());
            game.PlayerOne=originalOne;game.PlayerTwo=originalTwo;game.Stage=originalStage;game.Difficulty=originalLevel;game.Personality=originalType;game.Mode=BattleMode.Cpu;
            PlayerPrefs.SetString("ccu.p1",originalOne);PlayerPrefs.SetString("ccu.p2",originalTwo);PlayerPrefs.SetInt("ccu.stage",(int)originalStage);PlayerPrefs.SetInt("ccu.difficulty",(int)originalLevel);PlayerPrefs.SetInt("ccu.personality",(int)originalType);PlayerPrefs.Save();
            yield return Tap(GameController.TitleSettingsRect.center);
            if(game.MusicEnabled!=originalMusic)yield return Tap(GameController.BgmToggleRect.center);
            if(game.EffectsEnabled!=originalEffects)yield return Tap(GameController.EffectsToggleRect.center);
            yield return Tap(GameController.SettingsCloseRect.center);
            var quoted=new List<string>();foreach(string item in checks)quoted.Add("\""+item+"\"");File.WriteAllText(Path.Combine(directory,"player-smoke.json"),"{\"passed\":"+checks.Count+",\"failed\":0,\"hardwareAudioDeferred\":"+(deferAudioDevice?"true":"false")+",\"bicycleWhiffTravel\":"+bikeWhiffTravel.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+",\"checks\":["+string.Join(",",quoted.ToArray())+"]}");
            Debug.Log("CLUB_CLASH_NATIVE_SMOKE_PASS "+directory+" checks="+checks.Count);if(LaunchArguments.Has("-club-clash-smoke-exit"))Application.Quit();
        }
    }
}
