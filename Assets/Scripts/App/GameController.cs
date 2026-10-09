using System;
using System.Collections;
using UnityEngine;

namespace ClubClash
{
    public enum GameScreen { Title, Selection, Battle }
    public sealed class GameController : MonoBehaviour
    {
        public Battle CurrentBattle { get; private set; }
        public BattleRenderer Visuals { get; private set; }
        public GameScreen ScreenState { get; private set; } = GameScreen.Title;
        public bool InBattle { get { return ScreenState==GameScreen.Battle; } }
        public bool ResultTransitionPending => resultTransition != null && resultTransition.IsPending;
        public bool PracticePickerOpen { get { return practicePicker; } }
        public bool TitleSettingsOpen { get { return settings; } }
        public string LastTextOverflow { get; private set; }
        public bool TitleHelpOpen { get { return help; } }
        public bool SupportDialogOpen => supportDialog;
        public bool ReviewRequestPending => reviewPending;
        public bool ReviewSupportRequested => reviews != null && reviews.SupportRequested;
        public int CompletedReviewMatches => reviews == null ? 0 : reviews.CompletedMatches;
        public bool MusicEnabled { get { return musicEnabled; } }
        public bool EffectsEnabled { get { return effectsEnabled; } }
        public bool IsOpponentRolling { get; private set; }
        public int OpponentRollTicks { get; private set; }
        public int OpponentRollCount { get; private set; }
        public float OpponentRollElapsed { get; private set; }
        public const int OpponentRollTotalTicks=12;
        public const float OpponentRollIntervalSeconds=.12f;
        public event Action<int,string> OpponentRollTick;
        public event Action OpponentRollCompleted;
        public int SelectingSide { get { return selecting; } }
        public string SelectionHeading { get { return selecting==0?"自分の部活":"相手の部活"; } }
        public bool OpponentRandom { get; private set; }
        public string PlayerOne="boxing", PlayerTwo="soccer";
        public BattleMode Mode=BattleMode.Cpu;
        public CpuDifficulty Difficulty=CpuDifficulty.Level3;
        public CpuPersonality Personality=CpuPersonality.Random;
        public StageId Stage=StageId.Ground;
        [NonSerialized] public InputFrame? TestInputOne, TestInputTwo;
        [NonSerialized] public Vector2? TestTouchPoint;
        public static readonly Rect TitleBattleRect=new Rect(457,264,366,96), TitlePracticeRect=new Rect(457,392,366,96), TitleLocalRect=new Rect(457,520,366,96);
        public static readonly Rect TitleSettingsRect=new Rect(20,16,180,96), TitleHelpRect=new Rect(1080,16,180,96), BackRect=new Rect(16,8,160,96);
        public static readonly Rect SelectionPanelRect=new Rect(374,86,532,410), SelectionStartRect=new Rect(392,394,496,96);
        public static readonly Rect PlayerSideRect=new Rect(24,128,326,364), OpponentSideRect=new Rect(930,128,326,364), RandomOpponentRect=new Rect(930,8,326,96);
        public static readonly Rect PracticeClubRect=new Rect(18,14,96,96), PracticePickerPanelRect=new Rect(258,106,764,508), PracticePickerCloseRect=new Rect(910,118,96,96);
        public static readonly Rect SettingsPanelRect=new Rect(324,112,632,496), SettingsCloseRect=new Rect(844,128,96,96);
        public static readonly Rect BgmToggleRect=new Rect(348,240,584,96), EffectsToggleRect=new Rect(348,372,584,96), DebugToggleRect=new Rect(348,456,584,96);
        public static readonly Rect AdPrivacyRect=new Rect(348,492,584,96);
        public static readonly Rect HelpPanelRect=new Rect(232,96,816,548), HelpCloseRect=new Rect(928,112,96,96);
        public static readonly Rect PauseRect=new Rect(1168,12,96,96), PausePanelRect=new Rect(424,160,432,456);
        public static readonly Rect PauseResumeRect=new Rect(456,252,368,96), PauseRetryRect=new Rect(456,372,368,96), PauseExitRect=new Rect(456,492,368,96);
        public static readonly Rect ResultPanelRect=new Rect(366,186,548,326), ResultRetryRect=new Rect(388,388,240,96), ResultTitleRect=new Rect(652,388,240,96);
        public static readonly Rect SupportPanelRect=new Rect(324,158,632,390), SupportCloseRect=new Rect(348,428,276,96), SupportReviewRect=new Rect(656,428,276,96), SupportOnlyCloseRect=new Rect(496,428,288,96);
        public static Rect ClubSlotRect(int i){return new Rect(24+i%10*124,508+i/10*108,112,96);}
        public static Rect StageRect(int i){return new Rect(392+i*168,124,152,96);}
        public static Rect DifficultyRect(int i){return new Rect(376+i*108,270,96,96);}
        public static Rect PracticeChoiceRect(int i)
        {
            int row=i/7,col=i%7;return new Rect(278+(col+(row==2?1:0))*104,222+row*124,96,96);
        }
        readonly Vector2 stickCenter=new Vector2(130,620);
        readonly Rect jumpRect=new Rect(1056,446,96,96), guardRect=new Rect(972,530,96,96), lightRect=new Rect(1056,614,96,96), strongRect=new Rect(1140,530,96,96);
        public static Vector2 JumpControlPoint => new Vector2(1104,494);
        public static Vector2 GuardControlPoint => new Vector2(1020,578);
        public static Vector2 LightControlPoint => new Vector2(1104,662);
        public static Vector2 StrongControlPoint => new Vector2(1188,578);
        public float PresentationTimeScale => CurrentBattle!=null && !CurrentBattle.Paused && (CurrentBattle.Phase==BattlePhase.RoundOver || CurrentBattle.Phase==BattlePhase.MatchOver) && (CurrentBattle.Fighters[0].Hp<=0 || CurrentBattle.Fighters[1].Hp<=0) && roundAge<.75f ? .2f : 1f;
        public string BattleBannerCaption
        {
            get
            {
                if(CurrentBattle==null || Mode==BattleMode.Practice)return "";
                if(CurrentBattle.Phase==BattlePhase.Countdown)return Mathf.Max(1,Mathf.CeilToInt(CurrentBattle.Countdown)).ToString();
                if(CurrentBattle.Phase==BattlePhase.RoundOver || (CurrentBattle.Phase==BattlePhase.MatchOver && roundAge<=1.7f))
                    return CurrentBattle.RoundWinner<0?"DRAW":CurrentBattle.Fighters[0].Hp<=0 || CurrentBattle.Fighters[1].Hp<=0?"KO":"TIME";
                return CurrentBattle.Phase==BattlePhase.Fight && fightBanner>0?"FIGHT":"";
            }
        }
        public string ResultCaption => CurrentBattle==null || CurrentBattle.Winner<0 ? "DRAW" : CurrentBattle.Winner==0 ? "WIN" : "LOSE";
        readonly Color ink=new Color(.025f,.045f,.075f,1), panel=new Color(.07f,.105f,.16f,.97f), line=new Color(.2f,.29f,.38f), cream=new Color(.97f,.96f,.89f), cyan=new Color(.44f,.91f,.87f), orange=new Color(1,.62f,.36f);
        readonly float[] hpTrail={1,1},hpLast={1,1},hpAge={0,0};
        int selecting,stickFinger=int.MinValue;
        float stickAxis,roundAge,fightBanner,rollClock;
        Vector2 lastUiPoint;
        float lastUiPress=-100;
        GameScreen lastUiScreen;
        bool musicEnabled=true,effectsEnabled=true,settings,help,debug,textAudit,practicePicker,pickerWasPaused,stickHeld,testPointHeld,touchLeft,touchRight,touchJump,touchGuard,touchA,touchB;
        BattlePhase lastPhase;
        IInterstitialAds ads;
        MatchResultTransition resultTransition;
        AdMobInterstitialAds nativeAds;
        bool privacyFormOpen, supportDialog, reviewPending, completedMatchRecorded;
        ReviewPrompter reviews;
        Coroutine reviewRoutine;
        Font font;Texture2D white,menuStage;ArcadeSkin skin;
        GUIStyle text,title,small,centered,button,number,fighterName,cardName;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot(){if(FindAnyObjectByType<GameController>()==null)new GameObject("Club Clash").AddComponent<GameController>();}
        void Awake()
        {
            Application.targetFrameRate=60;Application.runInBackground=true;
            UnityEngine.Screen.orientation=ScreenOrientation.LandscapeLeft;
            UnityEngine.Screen.autorotateToPortrait=UnityEngine.Screen.autorotateToPortraitUpsideDown=false;
            UnityEngine.Screen.autorotateToLandscapeLeft=UnityEngine.Screen.autorotateToLandscapeRight=true;
            font=Resources.Load<Font>("Fonts/NotoSansCJKjp-Regular");white=Texture2D.whiteTexture;menuStage=Resources.Load<Texture2D>("Art/stage");skin=new ArcadeSkin();
            Visuals=gameObject.AddComponent<BattleRenderer>();Visuals.SetVisible(false);
            PlayerOne=PlayableId(PlayerPrefs.GetString("ccu.p1","boxing"));PlayerTwo=PlayableId(PlayerPrefs.GetString("ccu.p2","soccer"));
            Stage=(StageId)Mathf.Clamp(PlayerPrefs.GetInt("ccu.stage",0),0,2);Difficulty=(CpuDifficulty)Mathf.Clamp(PlayerPrefs.GetInt("ccu.difficulty",3),1,5);
            int legacyEnabled=PlayerPrefs.GetInt("ccu.muted",0)==1?0:1;
            musicEnabled=PlayerPrefs.GetInt("ccu.music",legacyEnabled)==1;effectsEnabled=PlayerPrefs.GetInt("ccu.effects",legacyEnabled)==1;debug=false;
            ApplyAudio();Visuals.SetDebug(debug);
            textAudit=LaunchArguments.Has("-club-clash-smoke") || LaunchArguments.Has("-club-clash-review-proof");if(LaunchArguments.Has("-club-clash-smoke"))gameObject.AddComponent<PlayerSmoke>();
            if(LaunchArguments.Has("-club-clash-store-capture"))gameObject.AddComponent<StoreScreenshotCapture>();
            SetReviewService(new PlayerPrefsReviewStore(),new IosReviewRequester());
            nativeAds=gameObject.AddComponent<AdMobInterstitialAds>();SetResultAdService(nativeAds);
            if(Debug.isDebugBuild && LaunchArguments.Has("-club-clash-ad-sdk-proof"))gameObject.AddComponent<AdMobNativeProof>();
            if(Debug.isDebugBuild && LaunchArguments.Has("-club-clash-review-proof"))gameObject.AddComponent<ReviewNativeProof>();
        }
        public void SetResultAdService(IInterstitialAds service)
        {
            resultTransition?.Dispose();ads=service;resultTransition=new MatchResultTransition(service);
        }
        public bool ExitMatchResult(bool rematch)
        {
            bool visible=InBattle && Mode!=BattleMode.Practice && CurrentBattle!=null && CurrentBattle.Phase==BattlePhase.MatchOver && roundAge>1.7f;
            if(visible)RecordCompletedReviewMatch();
            return resultTransition!=null && resultTransition.Request(visible,()=>
            {
                if(this==null)return;
                ResetStick();TestTouchPoint=null;testPointHeld=false;
                if(rematch)StartBattle();else ShowTitle();
            });
        }
        static string PlayableId(string id){foreach(ClubDefinition club in Catalog.Clubs)if(club.Id==id)return id;return Catalog.Clubs[0].Id;}
        void Update()
        {
            if(ResultTransitionPending || privacyFormOpen || reviewPending || (nativeAds!=null && nativeAds.TrackingPromptPending)){ResetStick();return;}
            UpdateViewport();SamplePointers();UpdateOpponentRoll(Time.unscaledDeltaTime);
            if(Input.GetKeyDown(KeyCode.Escape))
            {
                if(IsOpponentRolling){}else if(supportDialog)CloseSupportDialog();else if(settings)CloseSettings();else if(help)CloseHelp();else if(practicePicker)ClosePracticePicker();else if(InBattle && CurrentBattle!=null && CurrentBattle.Phase!=BattlePhase.MatchOver)Pause(!CurrentBattle.Paused);else if(ScreenState==GameScreen.Selection)ShowTitle();
            }
            if(!InBattle){if(!help && !settings && !supportDialog && !IsOpponentRolling)MenuKeys();return;}if(CurrentBattle==null)return;
            if(Mode==BattleMode.Practice && Input.GetKeyDown(KeyCode.R) && !help && !practicePicker)StartTraining();
            bool canPlay=!help && !practicePicker && !CurrentBattle.Paused;
            InputFrame p1=canPlay?new InputFrame{Left=Input.GetKey(KeyCode.A)||touchLeft,Right=Input.GetKey(KeyCode.D)||touchRight,Jump=Input.GetKey(KeyCode.W)||Input.GetKey(KeyCode.Space)||touchJump,Guard=Input.GetKey(KeyCode.S)||touchGuard,A=Input.GetKey(KeyCode.J)||touchA,B=Input.GetKey(KeyCode.K)||touchB}:InputFrame.Empty;
            CurrentBattle.Update(Mathf.Min(Time.unscaledDeltaTime,.05f)*PresentationTimeScale,TestInputOne??p1,TestInputTwo??InputFrame.Empty);
            if(CurrentBattle.Paused)return;
            if(lastPhase!=CurrentBattle.Phase){lastPhase=CurrentBattle.Phase;roundAge=0;if(lastPhase==BattlePhase.Fight && Mode!=BattleMode.Practice)fightBanner=.6f;}
            fightBanner=Mathf.Max(0,fightBanner-Time.unscaledDeltaTime);
            if(CurrentBattle.Phase==BattlePhase.RoundOver){roundAge+=Time.unscaledDeltaTime;if(roundAge>1.7f)CurrentBattle.NextRound();}
            if(CurrentBattle.Phase==BattlePhase.MatchOver){roundAge+=Time.unscaledDeltaTime;RecordCompletedReviewMatch();}
        }
        void LateUpdate()
        {
            if(!InBattle || CurrentBattle==null)return;
            for(int i=0;i<2;i++)
            {
                float ratio=Mathf.Clamp01(CurrentBattle.Fighters[i].Hp/CurrentBattle.Fighters[i].MaxHp);
                if(ratio!=hpLast[i])hpAge[i]=0;else if(!CurrentBattle.Paused)hpAge[i]+=Time.unscaledDeltaTime;
                if(ratio>hpLast[i])hpTrail[i]=ratio;else if(hpAge[i]>.3f)hpTrail[i]=Mathf.MoveTowards(hpTrail[i],ratio,Time.unscaledDeltaTime*.6f);hpLast[i]=ratio;
            }
            Visuals.Render(CurrentBattle.Paused || ResultTransitionPending?0:Time.unscaledDeltaTime*PresentationTimeScale);
        }
        void MenuKeys()
        {
            if(ScreenState==GameScreen.Title){if(Input.GetKeyDown(KeyCode.Return))BeginBattleSelection();if(Input.GetKeyDown(KeyCode.P))StartTraining();if(Input.GetKeyDown(KeyCode.O))OpenSettings();if(Input.GetKeyDown(KeyCode.F1))OpenHelp();return;}
            if(Input.GetKeyDown(KeyCode.Tab))SelectSide(1-selecting);
            int index=0;string id=selecting==0?PlayerOne:PlayerTwo;for(int i=0;i<Catalog.Clubs.Count;i++)if(Catalog.Clubs[i].Id==id)index=i;
            int dir=Input.GetKeyDown(KeyCode.RightArrow)?1:Input.GetKeyDown(KeyCode.LeftArrow)?-1:Input.GetKeyDown(KeyCode.DownArrow)?10:Input.GetKeyDown(KeyCode.UpArrow)?-10:0;
            if(dir!=0)
            {
                string next=Catalog.Clubs[(index+dir+Catalog.Clubs.Count)%Catalog.Clubs.Count].Id;
                if(selecting==0)PlayerOne=next;else{PlayerTwo=next;OpponentRandom=false;}
            }
            if(Input.GetKeyDown(KeyCode.F1))SetStage(StageId.Ground);if(Input.GetKeyDown(KeyCode.F2))SetStage(StageId.Classroom);if(Input.GetKeyDown(KeyCode.F3))SetStage(StageId.Gym);
            if(Input.GetKeyDown(KeyCode.Return))StartBattle();
        }
        public void SetReviewService(IReviewStateStore store,IReviewRequester requester)
        {
            CancelReviewRequest();reviews=new ReviewPrompter(store,requester);
        }
        void RecordCompletedReviewMatch()
        {
            if(completedMatchRecorded || !InBattle || Mode==BattleMode.Practice || CurrentBattle==null || CurrentBattle.Phase!=BattlePhase.MatchOver)return;
            completedMatchRecorded=true;reviews?.RecordCompletedMatch();
        }
        void CancelReviewRequest()
        {
            if(reviewRoutine!=null)StopCoroutine(reviewRoutine);
            reviewRoutine=null;reviewPending=false;
        }
        void QueueReview(bool automatic)
        {
            if(reviewPending)return;reviewPending=true;reviewRoutine=StartCoroutine(RequestReviewAfterTitle(automatic));
        }
        IEnumerator RequestReviewAfterTitle(bool automatic)
        {
            // Render the title, then let the interstitial dismissal finish before StoreKit.
            yield return new WaitForEndOfFrame();yield return new WaitForSecondsRealtime(.5f);
            while(!Application.isFocused || (nativeAds!=null && nativeAds.TrackingPromptPending))yield return null;
            reviewRoutine=null;reviewPending=false;
            if(ScreenState!=GameScreen.Title || settings || help || supportDialog || ResultTransitionPending)yield break;
            bool requested=automatic?reviews.RequestAutomatically():reviews.RequestSupport();
            Debug.Log("[ClubReview] "+(automatic?"Automatic":"Support")+" request supported: "+requested);
        }
        public void OpenSupportDialog()
        {
            if(ScreenState!=GameScreen.Title || reviewPending || settings || help)return;
            supportDialog=true;ResetStick();
        }
        public void CloseSupportDialog(){supportDialog=false;}
        public void RequestSupportReview()
        {
            if(!supportDialog || ReviewSupportRequested || reviewPending)return;
            supportDialog=false;QueueReview(false);
        }
        public void ShowTitle(){RecordCompletedReviewMatch();LeaveBattle();ScreenState=GameScreen.Title;if(reviews!=null && reviews.AutomaticDue)QueueReview(true);}
        public void BeginBattleSelection(){LeaveBattle();ScreenState=GameScreen.Selection;Mode=BattleMode.Cpu;selecting=0;}
        public void ShowSelection(){BeginBattleSelection();}
        void LeaveBattle(){CancelReviewRequest();supportDialog=false;if(CurrentBattle!=null)CurrentBattle.SetPaused(true);settings=help=practicePicker=false;IsOpponentRolling=false;TestInputOne=TestInputTwo=null;TestTouchPoint=null;testPointHeld=false;ResetStick();Visuals.SetVisible(false);}
        public void SelectSide(int side){if(!IsOpponentRolling)selecting=Mathf.Clamp(side,0,1);}
        public void ChooseClub(string id){if(IsOpponentRolling || PlayableId(id)!=id)return;if(selecting==0){PlayerOne=id;selecting=1;}else{PlayerTwo=id;OpponentRandom=false;}}
        public bool SelectionBuffed(int side){return Catalog.Get(side==0?PlayerOne:PlayerTwo).HomeStage==Stage;}
        public void RandomOpponent()
        {
            if(ScreenState!=GameScreen.Selection || IsOpponentRolling)return;
            selecting=1;OpponentRandom=true;IsOpponentRolling=true;OpponentRollTicks=0;OpponentRollElapsed=rollClock=0;
        }
        void UpdateOpponentRoll(float dt)
        {
            if(!IsOpponentRolling)return;OpponentRollElapsed+=dt;rollClock+=dt;
            while(IsOpponentRolling && rollClock>=OpponentRollIntervalSeconds)
            {
                rollClock-=OpponentRollIntervalSeconds;
                int chosen=UnityEngine.Random.Range(0,Catalog.Clubs.Count-1),current=0;
                for(int i=0;i<Catalog.Clubs.Count;i++)if(Catalog.Clubs[i].Id==PlayerTwo){current=i;break;}
                if(chosen>=current)chosen++;PlayerTwo=Catalog.Clubs[chosen].Id;OpponentRollTicks++;Visuals.PlaySelectionTick(OpponentRollTicks,OpponentRollTicks==OpponentRollTotalTicks);
                if(OpponentRollTick!=null)OpponentRollTick(OpponentRollTicks,PlayerTwo);
                if(OpponentRollTicks>=OpponentRollTotalTicks)
                {
                    IsOpponentRolling=false;OpponentRollCount++;if(OpponentRollCompleted!=null)OpponentRollCompleted();
                }
            }
        }
        public void SetStage(StageId value){if(!IsOpponentRolling)Stage=(StageId)Mathf.Clamp((int)value,0,2);}
        public void SetDifficulty(CpuDifficulty value){if(!IsOpponentRolling)Difficulty=(CpuDifficulty)Mathf.Clamp((int)value,1,5);}
        public void SetPersonality(CpuPersonality value){Personality=CpuPersonality.Random;}
        public void StartTraining(){if(IsOpponentRolling)return;RecordCompletedReviewMatch();Mode=BattleMode.Practice;StartBattle();}
        public void StartBattle()
        {
            if(IsOpponentRolling)return;
            RecordCompletedReviewMatch();CancelReviewRequest();supportDialog=false;completedMatchRecorded=false;
            if(Mode!=BattleMode.Practice)Mode=BattleMode.Cpu;PlayerOne=PlayableId(PlayerOne);PlayerTwo=PlayableId(PlayerTwo);
            settings=help=practicePicker=false;Personality=CpuPersonality.Random;ScreenState=GameScreen.Battle;roundAge=fightBanner=0;ResetStick();TestTouchPoint=null;testPointHeld=false;
            for(int i=0;i<2;i++){hpTrail[i]=hpLast[i]=1;hpAge[i]=0;}
            CurrentBattle=new Battle(new BattleOptions{P1=PlayerOne,P2=PlayerTwo,Mode=Mode,Difficulty=Difficulty,Personality=CpuPersonality.Random,Stage=Stage,Seed=(uint)DateTime.UtcNow.Ticks});lastPhase=CurrentBattle.Phase;
            Visuals.Bind(CurrentBattle);Visuals.SetVisible(true);ApplyAudio();Visuals.SetDebug(debug);
            PlayerPrefs.SetString("ccu.p1",PlayerOne);PlayerPrefs.SetString("ccu.p2",PlayerTwo);PlayerPrefs.SetInt("ccu.stage",(int)Stage);PlayerPrefs.SetInt("ccu.difficulty",(int)Difficulty);PlayerPrefs.Save();
        }
        public void OpenPracticePicker(){if(!InBattle || Mode!=BattleMode.Practice || CurrentBattle==null || practicePicker)return;pickerWasPaused=CurrentBattle.Paused;CurrentBattle.SetPaused(true);practicePicker=true;ResetStick();}
        public void ClosePracticePicker(){if(!practicePicker)return;practicePicker=false;if(CurrentBattle!=null)CurrentBattle.SetPaused(pickerWasPaused);ResetStick();}
        public void ChoosePracticeClub(string id)
        {
            if(!InBattle || Mode!=BattleMode.Practice || CurrentBattle==null || PlayableId(id)!=id)return;
            if(CurrentBattle.SetPracticeClub(id)){PlayerOne=id;PlayerPrefs.SetString("ccu.p1",id);PlayerPrefs.Save();ClosePracticePicker();}
        }
        public void Pause(bool value){if(CurrentBattle!=null && CurrentBattle.Phase!=BattlePhase.MatchOver)CurrentBattle.SetPaused(value);ResetStick();}
        void OnApplicationFocus(bool focused){if(!focused && InBattle)Pause(true);}void OnApplicationPause(bool paused){if(paused && InBattle)Pause(true);}
        public static string StageName(StageId value){return value==StageId.Classroom?"教室":value==StageId.Gym?"体育館":"グラウンド";}
        public static Rect FitDesignViewport(Rect safeAreaPixels)
        {
            if(!Finite(safeAreaPixels.x) || !Finite(safeAreaPixels.y) || !Finite(safeAreaPixels.width) || !Finite(safeAreaPixels.height) || safeAreaPixels.width<=0 || safeAreaPixels.height<=0)
                safeAreaPixels=new Rect(0,0,Mathf.Max(1,UnityEngine.Screen.width),Mathf.Max(1,UnityEngine.Screen.height));
            float scale=Mathf.Min(safeAreaPixels.width/1280f,safeAreaPixels.height/720f),width=1280*scale,height=720*scale;
            return new Rect(safeAreaPixels.x+(safeAreaPixels.width-width)*.5f,safeAreaPixels.y+(safeAreaPixels.height-height)*.5f,width,height);
        }
        static bool Finite(float value){return !float.IsNaN(value) && !float.IsInfinity(value);}
        void UpdateViewport()
        {
            if(Visuals==null || Visuals.SceneCamera==null || UnityEngine.Screen.width<=0 || UnityEngine.Screen.height<=0)return;
            Rect view=FitDesignViewport(UnityEngine.Screen.safeArea);
            Visuals.SceneCamera.rect=new Rect(0,0,1,1);
        }
        Vector2 DesignPoint(Vector2 point){Rect view=FitDesignViewport(UnityEngine.Screen.safeArea);float scale=view.width/1280;return new Vector2((point.x-view.x)/scale,(view.yMax-point.y)/scale);}
        void SamplePointers()
        {
            touchLeft=touchRight=touchJump=touchGuard=touchA=touchB=false;stickHeld=false;
            if(stickFinger!=int.MinValue)
            {
                bool alive=stickFinger==-42?TestTouchPoint.HasValue:stickFinger==-1?Input.touchCount==0 && Input.GetMouseButton(0):false;
                if(stickFinger>=0)for(int i=0;i<Input.touchCount;i++){Touch t=Input.GetTouch(i);if(t.fingerId==stickFinger && t.phase!=TouchPhase.Ended && t.phase!=TouchPhase.Canceled)alive=true;}if(!alive)ResetStick();
            }
            for(int i=0;i<Input.touchCount;i++){Touch t=Input.GetTouch(i);if(t.phase!=TouchPhase.Ended && t.phase!=TouchPhase.Canceled)Pointer(t.fingerId,DesignPoint(t.position),t.phase==TouchPhase.Began);}
            if(Input.touchCount==0 && Input.GetMouseButton(0))Pointer(-1,DesignPoint(Input.mousePosition),Input.GetMouseButtonDown(0));
            if(TestTouchPoint.HasValue)Pointer(-42,TestTouchPoint.Value,!testPointHeld);testPointHeld=TestTouchPoint.HasValue;
            if(!stickHeld)ResetStick();touchLeft=stickHeld && stickAxis<-.18f;touchRight=stickHeld && stickAxis>.18f;
        }
        void Pointer(int finger,Vector2 point,bool began)
        {
            if(began && RouteUiPoint(point))return;if(!InBattle || CurrentBattle==null || help || practicePicker || CurrentBattle.Paused || CurrentBattle.Phase==BattlePhase.MatchOver)return;
            if(finger==stickFinger || (stickFinger==int.MinValue && began && (point-stickCenter).sqrMagnitude<104*104)){stickFinger=finger;stickHeld=true;stickAxis=Mathf.Clamp((point.x-stickCenter.x)/48,-1,1);return;}
            touchJump|=InCircle(jumpRect,point);touchGuard|=InCircle(guardRect,point);touchA|=InCircle(lightRect,point);touchB|=InCircle(strongRect,point);
        }
        void ResetStick(){stickFinger=int.MinValue;stickAxis=0;stickHeld=false;}static bool InCircle(Rect rect,Vector2 point){return (point-rect.center).sqrMagnitude<=rect.width*rect.width*.25f;}
        // Native touch, mouse and Smoke injection use one action route; IMGUI paints only.
        public bool RouteUiPoint(Vector2 p)
        {
            if(IsOpponentRolling || ResultTransitionPending || privacyFormOpen || reviewPending || (nativeAds!=null && nativeAds.TrackingPromptPending))return true;
            lastUiPoint=p;lastUiPress=Time.unscaledTime;lastUiScreen=ScreenState;
            if(supportDialog){if((ReviewSupportRequested?SupportOnlyCloseRect:SupportCloseRect).Contains(p))CloseSupportDialog();else if(!ReviewSupportRequested && SupportReviewRect.Contains(p))RequestSupportReview();return true;}
            if(settings){if(SettingsCloseRect.Contains(p))CloseSettings();else if(BgmToggleRect.Contains(p))ToggleMusic();else if(EffectsToggleRect.Contains(p))ToggleEffects();else if(AdPrivacyRect.Contains(p) && ads!=null && ads.PrivacyOptionsRequired){privacyFormOpen=true;ads.ShowPrivacyOptions(()=>privacyFormOpen=false);}return true;}
            if(help){if(HelpCloseRect.Contains(p))CloseHelp();return true;}
            if(practicePicker){if(PracticePickerCloseRect.Contains(p)){ClosePracticePicker();return true;}for(int i=0;i<Catalog.Clubs.Count;i++)if(InCircle(PracticeChoiceRect(i),p)){ChoosePracticeClub(Catalog.Clubs[i].Id);break;}return true;}
            if(ScreenState==GameScreen.Title || ScreenState==GameScreen.Selection)
            {
                if(ScreenState==GameScreen.Title){if(TitleSettingsRect.Contains(p))OpenSettings();else if(TitleHelpRect.Contains(p))OpenHelp();else if(TitleBattleRect.Contains(p))BeginBattleSelection();else if(TitlePracticeRect.Contains(p))StartTraining();else if(TitleLocalRect.Contains(p))OpenSupportDialog();return true;}
                if(BackRect.Contains(p)){ShowTitle();return true;}if(PlayerSideRect.Contains(p)){SelectSide(0);return true;}if(OpponentSideRect.Contains(p)){SelectSide(1);return true;}if(RandomOpponentRect.Contains(p)){RandomOpponent();return true;}
                for(int i=0;i<20;i++)if(ClubSlotRect(i).Contains(p)){if(i<Catalog.Clubs.Count)ChooseClub(Catalog.Clubs[i].Id);return true;}
                for(int i=0;i<3;i++)if(StageRect(i).Contains(p)){SetStage((StageId)i);return true;}
                for(int i=0;i<5;i++)if(DifficultyRect(i).Contains(p)){SetDifficulty((CpuDifficulty)(i+1));return true;}
                if(SelectionStartRect.Contains(p))StartBattle();return true;
            }
            if(CurrentBattle==null)return false;
            if(CurrentBattle.Paused)
            {
                if(PauseResumeRect.Contains(p))Pause(false);else if(PauseRetryRect.Contains(p))StartBattle();
                else if(PauseExitRect.Contains(p)){if(Mode==BattleMode.Practice)ShowTitle();else ShowSelection();}return true;
            }
            if(CurrentBattle.Phase==BattlePhase.MatchOver){if(roundAge>1.7f){if(ResultRetryRect.Contains(p))ExitMatchResult(true);else if(ResultTitleRect.Contains(p))ExitMatchResult(false);}return true;}
            if(PauseRect.Contains(p)){Pause(true);return true;}if(Mode==BattleMode.Practice && InCircle(PracticeClubRect,p)){OpenPracticePicker();return true;}return false;
        }
        void ApplyAudio(){Visuals.SetMusicEnabled(musicEnabled);Visuals.SetEffectsEnabled(effectsEnabled);}
        public void ToggleMusic(){musicEnabled=!musicEnabled;Visuals.SetMusicEnabled(musicEnabled);PlayerPrefs.SetInt("ccu.music",musicEnabled?1:0);PlayerPrefs.Save();}
        public void ToggleEffects(){effectsEnabled=!effectsEnabled;Visuals.SetEffectsEnabled(effectsEnabled);PlayerPrefs.SetInt("ccu.effects",effectsEnabled?1:0);PlayerPrefs.Save();}
        public void ToggleDebug(){debug=!debug;Visuals.SetDebug(debug);PlayerPrefs.SetInt("ccu.debug",debug?1:0);PlayerPrefs.Save();}
        public void OpenSettings(){if(ScreenState==GameScreen.Title){help=false;settings=true;}}
        public void CloseSettings(){settings=false;}
        public void OpenHelp(){if(ScreenState==GameScreen.Title){settings=false;help=true;}}
        public void CloseHelp(){help=false;}
        void InitStyles()
        {
            if(text!=null)return;text=new GUIStyle(GUI.skin.label){font=font,fontSize=20,alignment=TextAnchor.MiddleLeft,wordWrap=false,padding=new RectOffset(0,0,0,0),contentOffset=Vector2.zero};text.normal.textColor=cream;
            title=new GUIStyle(text){fontSize=28,fontStyle=FontStyle.Bold};small=new GUIStyle(text){fontSize=16};small.normal.textColor=new Color(.64f,.74f,.8f);
            centered=new GUIStyle(text){alignment=TextAnchor.MiddleCenter};button=new GUIStyle(centered){fontSize=24,fontStyle=FontStyle.Bold};number=new GUIStyle(centered){fontSize=72,fontStyle=FontStyle.Bold};fighterName=new GUIStyle(text){fontSize=28,fontStyle=FontStyle.Bold};cardName=new GUIStyle(centered){fontSize=16,fontStyle=FontStyle.Bold};
        }
        void OnDestroy(){resultTransition?.Dispose();if(skin!=null)skin.Dispose();}
        void OnGUI()
        {
            InitStyles();Rect view=FitDesignViewport(UnityEngine.Screen.safeArea);float s=view.width/1280;GUI.matrix=Matrix4x4.TRS(new Vector3(view.x,UnityEngine.Screen.height-view.yMax,0),Quaternion.identity,new Vector3(s,s,1));
            if(ScreenState==GameScreen.Title)DrawTitle();else if(ScreenState==GameScreen.Selection)DrawSelection();else DrawBattle();if(practicePicker)DrawPracticePicker();if(settings)DrawSettings();if(help)DrawHelp();if(supportDialog)DrawSupportDialog();GUI.matrix=Matrix4x4.identity;
        }
        void Fill(Rect rect,Color color){Color old=GUI.color;GUI.color=color;GUI.DrawTexture(rect,white);GUI.color=old;}
        void Border(Rect r,Color c,int w=2){Fill(new Rect(r.x,r.y,r.width,w),c);Fill(new Rect(r.x,r.yMax-w,r.width,w),c);Fill(new Rect(r.x,r.y,w,r.height),c);Fill(new Rect(r.xMax-w,r.y,w,r.height),c);}
        void Label(Rect rect,string value,GUIStyle style=null,Color? color=null){GUIStyle s=style??text;Color old=s.normal.textColor;if(color.HasValue)s.normal.textColor=color.Value;
            if(textAudit && !string.IsNullOrEmpty(value)){Vector2 size=s.CalcSize(new GUIContent(value));if(size.x>rect.width+1 || size.y>rect.height+1){string overflow=value+" measured="+size+" available="+rect.size;if(string.IsNullOrEmpty(LastTextOverflow)){Debug.LogWarning("UI_TEXT_OVERFLOW "+overflow);LastTextOverflow=overflow;}}}
            GUI.Label(rect,value,s);s.normal.textColor=old;}
        void Button(Rect rect,string value,Color color,GUIStyle style=null,bool disabled=false)
        {
            bool pressed=!disabled && lastUiScreen==ScreenState && Time.unscaledTime-lastUiPress<.12f && rect.Contains(lastUiPoint);
            Rect painted=new Rect(rect.x,rect.y+(pressed?3:0),rect.width,rect.height);Color face=pressed?Color.Lerp(color,ink,.18f):color;
            ArcadeSkin.RoundRect(new Rect(rect.x,rect.y+4,rect.width,rect.height),new Color(0,0,0,.45f),10);ArcadeSkin.RoundRect(painted,face,10);ArcadeSkin.RoundRect(new Rect(painted.x+3,painted.y+2,painted.width-6,painted.height*.35f),new Color(1,1,1,pressed?.025f:.055f),9);
            GUIStyle centeredStyle=new GUIStyle(style??button){alignment=TextAnchor.MiddleCenter,wordWrap=false,padding=new RectOffset(0,0,0,0),contentOffset=Vector2.zero};
            Label(painted,value,centeredStyle,disabled?new Color(.40f,.47f,.53f):color==cyan||color==orange||color==cream?ink:cream);
        }
        void Portrait(Rect rect,string id,bool flip=false,bool idle=false)
        {
            Sprite sprite=idle?Visuals.IdlePortrait(id,Time.unscaledTime):Visuals.Portrait(id);if(sprite==null)return;Rect tr=sprite.textureRect;
            Rect uv=new Rect(tr.x/sprite.texture.width,tr.y/sprite.texture.height,tr.width/sprite.texture.width,tr.height/sprite.texture.height);
            Rect slot=new Rect(rect.x,rect.y-rect.height*.06f,rect.width,rect.height);
            Rect painted=Visuals.PortraitPlacement(id,sprite,slot);
            Matrix4x4 before=GUI.matrix;if(flip)GUIUtility.ScaleAroundPivot(new Vector2(-1,1),rect.center);
            GUI.DrawTextureWithTexCoords(painted,sprite.texture,uv,true);GUI.matrix=before;
        }
        Rect FullScreenDesignRect()
        {
            Rect view=FitDesignViewport(UnityEngine.Screen.safeArea);float scale=view.width/1280f;
            return new Rect(-view.x/scale,-(UnityEngine.Screen.height-view.yMax)/scale,UnityEngine.Screen.width/scale,UnityEngine.Screen.height/scale);
        }
        void MenuBackground(bool titleScreen)
        {
            Rect full=FullScreenDesignRect();Fill(full,ink);
            if(menuStage!=null){Color old=GUI.color;GUI.color=new Color(.65f,.72f,.78f,titleScreen?.65f:.3f);GUI.DrawTexture(full,menuStage,ScaleMode.ScaleAndCrop,true);GUI.color=old;}
            Fill(full,new Color(.01f,.025f,.055f,.35f));
        }
        void DrawLogo()
        {
            PixelLogo.Draw(new Rect(210,36,860,170));
        }
        void DrawTitle()
        {
            MenuBackground(true);DrawLogo();Button(TitleSettingsRect,"設定",panel);Button(TitleHelpRect,"操作",panel);
            Portrait(new Rect(8,267,425,425),"boxing",false,true);Portrait(new Rect(850,267,425,425),"soccer",true,true);
            Button(TitleBattleRect,"バトル",orange,new GUIStyle(button){fontSize=32});Button(TitlePracticeRect,"練習",cyan,new GUIStyle(button){fontSize=32});
            Button(TitleLocalRect,"2人対戦",panel,new GUIStyle(button){fontSize=28});
            ArcadeSkin.Construction(new Rect(478,550,34,34),orange);Label(new Rect(723,549,86,38),"準備中",new GUIStyle(centered){fontSize=20},new Color(.7f,.73f,.75f));
        }
        void DrawSupportDialog()
        {
            Fill(FullScreenDesignRect(),new Color(0,0,0,.7f));ArcadeSkin.RoundRect(SupportPanelRect,panel,20);
            Label(new Rect(348,182,584,56),"2人対戦は準備中",new GUIStyle(title){alignment=TextAnchor.MiddleCenter});
            if(ReviewSupportRequested)
            {
                Label(new Rect(348,274,584,48),"応援ありがとうございます",new GUIStyle(centered){fontSize=26});
                Button(SupportOnlyCloseRect,"閉じる",line);
            }
            else
            {
                Label(new Rect(348,250,584,48),"このアプリを応援してください",new GUIStyle(centered){fontSize=26});
                Label(new Rect(348,310,584,40),"オンライン対戦をご希望の方は、",new GUIStyle(centered){fontSize=24});
                Label(new Rect(348,350,584,40),"レビューで評価・ご意見をお寄せください",new GUIStyle(centered){fontSize=24});
                Button(SupportCloseRect,"閉じる",line);Button(SupportReviewRect,"レビューで応援",orange);
            }
        }
        void DrawSelection()
        {
            MenuBackground(false);Button(BackRect,"戻る",panel,null,IsOpponentRolling);Label(new Rect(184,10,704,62),SelectionHeading,new GUIStyle(title){alignment=TextAnchor.MiddleCenter});
            DrawPreview(PlayerSideRect,PlayerOne,0);DrawPreview(OpponentSideRect,PlayerTwo,1);ArcadeSkin.RoundRect(SelectionPanelRect,panel,18);
            Label(new Rect(392,90,496,28),"ステージ",centered);Label(new Rect(392,234,496,30),"レベル",centered);
            for(int i=0;i<3;i++)Button(StageRect(i),StageName((StageId)i),IsOpponentRolling?line:Stage==(StageId)i?orange:line,null,IsOpponentRolling);
            for(int i=0;i<5;i++)
            {
                Rect r=DifficultyRect(i);Color c=IsOpponentRolling?line:(int)Difficulty==i+1?orange:line;Button(r,"",c,null,IsOpponentRolling);
                Color label=IsOpponentRolling?new Color(.4f,.47f,.53f):c==orange?ink:cream;
                Label(new Rect(r.x,r.y+10,r.width,30),"レベル",new GUIStyle(centered){fontSize=18},label);Label(new Rect(r.x,r.y+40,r.width,48),(i+1).ToString(),new GUIStyle(button){fontSize=32},label);
            }
            Button(RandomOpponentRect,IsOpponentRolling?"おまかせ …":"CPU おまかせ",cyan,new GUIStyle(button){fontSize=26},IsOpponentRolling);
            Button(SelectionStartRect,"バトル",IsOpponentRolling?line:orange,new GUIStyle(button){fontSize=28},IsOpponentRolling);
            for(int i=0;i<20;i++)
            {
                Rect r=ClubSlotRect(i);if(i>=Catalog.Clubs.Count)continue;
                ClubDefinition club=Catalog.Clubs[i];bool own=club.Id==PlayerOne,enemy=club.Id==PlayerTwo;ArcadeSkin.RoundRect(r,panel,10);if(own||enemy)Border(new Rect(r.x+1,r.y+1,r.width-2,r.height-2),own?cyan:orange,2);
                if(own && enemy)Fill(new Rect(r.xMax-4,r.y+8,3,r.height-16),orange);Portrait(new Rect(r.x+19,r.y-1,74,76),club.Id);Label(new Rect(r.x+2,r.y+70,r.width-4,24),club.Name,cardName);
                if(own||enemy)Label(new Rect(r.x+6,r.y+3,52,18),own?"自分":"CPU",new GUIStyle(small){fontSize=12},own?cyan:orange);
            }
        }
        void DrawPreview(Rect r,string id,int side)
        {
            bool active=selecting==side,buffed=SelectionBuffed(side);ClubDefinition club=Catalog.Get(id);Color red=new Color(1,.39f,.36f);
            ArcadeSkin.RoundRect(r,active?cyan:line,18);ArcadeSkin.RoundRect(new Rect(r.x+3,r.y+3,r.width-6,r.height-6),side==0?new Color(.065f,.18f,.205f,.97f):new Color(.22f,.125f,.09f,.97f),16);
            Label(new Rect(r.x+14,r.y+8,66,32),side==0?"自分":"CPU",small,side==0?cyan:orange);
            if(buffed)Label(new Rect(r.xMax-224,r.y+8,210,32),"ステージ効果 ×1.3",new GUIStyle(centered){fontSize=20,fontStyle=FontStyle.Bold},red);
            Portrait(new Rect(r.x+10,r.y+34,r.width-20,r.height-110),id,side==1,true);
            ArcadeSkin.RoundRect(new Rect(r.x+12,r.yMax-82,r.width-24,44),ink,9);Label(new Rect(r.x+16,r.yMax-82,r.width-32,44),club.Name,new GUIStyle(button){fontSize=28});
            MiniStats(new Rect(r.x+10,r.yMax-36,r.width-20,30),club,buffed);
        }
        void MiniStats(Rect r,ClubDefinition club,bool buffed)
        {
            GUIStyle style=new GUIStyle(small){fontSize=16,alignment=TextAnchor.MiddleCenter};float width=r.width/3;Color color=buffed?new Color(1,.39f,.36f):new Color(.72f,.8f,.83f);
            Label(new Rect(r.x,r.y,width,r.height),"攻 "+club.PowerRating+"/10",style,color);
            Label(new Rect(r.x+width,r.y,width,r.height),"速 "+club.SpeedRating+"/10",style,color);
            Label(new Rect(r.x+width*2,r.y,width,r.height),"守 "+club.GuardRating+"/10",style,color);
        }
        void DrawBattle()
        {
            if(CurrentBattle==null)return;
            if(Mode==BattleMode.Practice)
            {
                RoundClubIcon(PracticeClubRect,CurrentBattle.Fighters[0].Club.Id,cyan,false);
                string name=CurrentBattle.Fighters[0].Club.Name;
                GUIStyle nameStyle=new GUIStyle(fighterName){fontSize=28,wordWrap=false,padding=new RectOffset(0,0,0,0)};
                float nameWidth=Mathf.Clamp(nameStyle.CalcSize(new GUIContent(name)).x+24,102,260);
                ArcadeSkin.RoundRect(new Rect(126,34,nameWidth,56),new Color(.025f,.045f,.07f,.94f),11);
                Label(new Rect(138,39,nameWidth-24,46),name,nameStyle,Color.white);
            }
            else
            {
                DrawHealth(0,new Rect(24,12,500,96));DrawHealth(1,new Rect(756,12,388,96));ArcadeSkin.RoundRect(new Rect(580,12,120,96),ink,14);
                Label(new Rect(580,14,120,24),CurrentBattle.Round+"本目",new GUIStyle(small){alignment=TextAnchor.MiddleCenter});PixelDisplay.Draw(new Rect(584,41,112,58),Mathf.CeilToInt(CurrentBattle.Time).ToString("00"),cream,7);
            }
            if(CurrentBattle.Phase!=BattlePhase.MatchOver)Button(PauseRect,"Ⅱ",ink,new GUIStyle(button){fontSize=28});DrawJoystick();Control(jumpRect,"跳ぶ",touchJump,cream);Control(guardRect,"守る",touchGuard,cream);Control(lightRect,"小攻撃",touchA,cyan);Control(strongRect,"強攻撃",touchB,orange);
            Fighter f=CurrentBattle.Fighters[0];if(f.BCooldown>0){float ratio=Mathf.Clamp01(f.BCooldown/Mathf.Max(.1f,f.Club.StrongMove.Cooldown));ArcadeSkin.Arc(strongRect.center,strongRect.width*.48f,ratio,orange,4);Label(new Rect(strongRect.x+7,strongRect.y+70,80,24),f.BCooldown.ToString("F1"),new GUIStyle(small){alignment=TextAnchor.MiddleCenter},cream);}
            if(Mode!=BattleMode.Practice){string banner=BattleBannerCaption;if(!string.IsNullOrEmpty(banner))Banner(banner);if(CurrentBattle.Phase==BattlePhase.MatchOver && roundAge>1.7f)DrawResult();}
            if(CurrentBattle.Paused && !practicePicker && CurrentBattle.Phase!=BattlePhase.MatchOver)DrawPause();
        }
        void RoundClubIcon(Rect r,string id,Color accent,bool selected){ArcadeSkin.Texture(new Rect(r.x,r.y+4,r.width,r.height),skin.Disc,new Color(0,0,0,.55f));ArcadeSkin.Texture(r,skin.Disc,selected?accent:new Color(.42f,.53f,.59f));ArcadeSkin.Arc(r.center,r.width*.45f,1,accent,2);Portrait(new Rect(r.x+11,r.y+7,r.width-22,r.height-18),id);}
        void DrawPracticePicker()
        {
            Fill(FullScreenDesignRect(),new Color(0,0,0,.67f));ArcadeSkin.RoundRect(PracticePickerPanelRect,panel,20);Button(PracticePickerCloseRect,"×",line);
            for(int i=0;i<Catalog.Clubs.Count;i++){ClubDefinition club=Catalog.Clubs[i];Rect r=PracticeChoiceRect(i);RoundClubIcon(r,club.Id,cyan,club.Id==PlayerOne);Label(new Rect(r.x-4,r.yMax+3,r.width+8,24),club.Name,cardName);}
        }
        void DrawHealth(int owner,Rect frame)
        {
            Fighter f=CurrentBattle.Fighters[owner];Color accent=owner==0?cyan:orange;
            ArcadeSkin.RoundRect(frame,new Color(.025f,.045f,.07f,.9f),12);
            Rect name=new Rect(frame.x+14,frame.y+2,frame.width-110,42);
            Label(name,f.Club.Name,new GUIStyle(fighterName){fontSize=26},cream);
            if(f.HomeAdvantage)Label(new Rect(frame.xMax-78,frame.y+7,64,28),"×1.3",new GUIStyle(centered){fontSize=18},orange);
            Rect r=new Rect(frame.x+14,frame.y+44,frame.width-28,22);
            ArcadeSkin.RoundRect(r,line,5);float ratio=Mathf.Clamp01(f.Hp/f.MaxHp),ghost=Mathf.Clamp01(hpTrail[owner]);
            if(ghost>0)ArcadeSkin.RoundRect(new Rect(r.x,r.y,r.width*ghost,r.height),new Color(1,.87f,.39f,.75f),4);
            if(ratio>0)GUI.DrawTexture(new Rect(r.x,r.y,r.width*ratio,r.height),skin.Gauge,ScaleMode.StretchToFill,true,0,accent,0,4);
            Rect guard=new Rect(r.x,frame.y+77,r.width*.55f,6);ArcadeSkin.RoundRect(guard,line,3);float stamina=Mathf.Clamp01(f.Stamina/f.MaxStamina);
            if(stamina>0)ArcadeSkin.RoundRect(new Rect(guard.x,guard.y,guard.width*stamina,6),f.GuardBroken>0?orange:cream,3);
            for(int i=0;i<2;i++)ArcadeSkin.Texture(new Rect(frame.xMax-50+i*20,frame.y+73,14,14),skin.Disc,i<CurrentBattle.Wins[owner]?accent:line);
            if(f.IsPoisoned)Label(new Rect(frame.xMax-116,frame.y+68,58,24),"毒 "+Mathf.CeilToInt(f.PoisonRemaining),new GUIStyle(small){alignment=TextAnchor.MiddleCenter},new Color(.85f,.62f,1));
        }
        void Control(Rect r,string caption,bool active,Color accent)
        {
            ArcadeSkin.Texture(new Rect(r.x,r.y+4,r.width,r.height),skin.Disc,new Color(0,0,0,.32f));
            ArcadeSkin.Texture(r,active?skin.PressedDisc:skin.Disc,active?Color.Lerp(accent,Color.white,.25f):new Color(.73f,.8f,.84f,.9f));
            ArcadeSkin.Arc(r.center,r.width*.45f,1,accent,2);
            Label(r,caption,new GUIStyle(button){fontSize=24},accent);
        }
        void DrawJoystick()
        {
            Rect r=new Rect(stickCenter.x-89,stickCenter.y-89,178,178);ArcadeSkin.Texture(new Rect(r.x,r.y+6,r.width,r.height),skin.Disc,new Color(0,0,0,.4f));ArcadeSkin.Texture(r,skin.StickBase,new Color(.56f,.7f,.76f,.9f));float axis=stickHeld?stickAxis:CurrentBattle.Paused?0:Input.GetKey(KeyCode.D)?1:Input.GetKey(KeyCode.A)?-1:0;
            Vector2 knob=stickCenter+new Vector2(axis*48,0);ArcadeSkin.Line(stickCenter+Vector2.left*50,stickCenter+Vector2.right*50,new Color(.43f,.76f,.79f,.5f),4);ArcadeSkin.Texture(new Rect(knob.x-34,knob.y-30,68,68),skin.StickKnob,new Color(0,0,0,.5f));ArcadeSkin.Texture(new Rect(knob.x-34,knob.y-34,68,68),skin.StickKnob,stickHeld||axis!=0?cyan:new Color(.71f,.83f,.85f));
        }
        void Banner(string value){PixelDisplay.Draw(new Rect(344,233,594,142),value,new Color(0,0,0,.85f),value.Length==1?16:12);PixelDisplay.Draw(new Rect(340,229,594,142),value,value=="FIGHT"?orange:cream,value.Length==1?16:12);}
        void DrawPause()
        {
            Fill(FullScreenDesignRect(),new Color(0,0,0,.6f));ArcadeSkin.RoundRect(PausePanelRect,panel,18);Label(new Rect(456,178,368,56),"一時停止",new GUIStyle(title){alignment=TextAnchor.MiddleCenter});Button(PauseResumeRect,"再開",cyan);
            Button(PauseRetryRect,Mode==BattleMode.Practice?"リセット":"再戦",line);Button(PauseExitRect,Mode==BattleMode.Practice?"タイトル":"選択",line);
        }
        void DrawResult()
        {
            Fill(FullScreenDesignRect(),new Color(0,0,0,.48f));ArcadeSkin.RoundRect(ResultPanelRect,panel,20);string winner=ResultCaption;
            Label(new Rect(388,208,504,84),winner,new GUIStyle(title){fontSize=56,alignment=TextAnchor.MiddleCenter},CurrentBattle.Winner==1?orange:cyan);Label(new Rect(388,292,504,84),CurrentBattle.Wins[0]+" : "+CurrentBattle.Wins[1],new GUIStyle(centered){fontSize=56,fontStyle=FontStyle.Bold});Button(ResultRetryRect,"再戦",orange,null,ResultTransitionPending);Button(ResultTitleRect,"タイトル",line,null,ResultTransitionPending);
        }
        void DrawSettings()
        {
            Fill(FullScreenDesignRect(),new Color(0,0,0,.7f));ArcadeSkin.RoundRect(SettingsPanelRect,panel,20);Label(new Rect(348,140,448,60),"設定",title);Button(SettingsCloseRect,"×",line);
            Button(BgmToggleRect,musicEnabled?"BGM ON":"BGM OFF",musicEnabled?cyan:line);Button(EffectsToggleRect,effectsEnabled?"効果音 ON":"効果音 OFF",effectsEnabled?cyan:line);
            if(ads!=null && ads.PrivacyOptionsRequired)Button(AdPrivacyRect,"広告のプライバシー設定",line,null,privacyFormOpen);
        }
        void DrawHelp()
        {
            Fill(FullScreenDesignRect(),new Color(0,0,0,.7f));ArcadeSkin.RoundRect(HelpPanelRect,panel,20);Label(new Rect(272,132,620,60),"操作",title);Button(HelpCloseRect,"×",line);
            Label(new Rect(272,246,736,56),"左スティック：左右に移動",new GUIStyle(text){fontSize=24});Label(new Rect(272,322,736,56),"右ボタン：跳ぶ・守る・小攻撃・強攻撃",new GUIStyle(text){fontSize=24});Label(new Rect(272,418,736,48),"小攻撃を続けて押すと連撃。Ⅱで一時停止。",text);Label(new Rect(272,486,736,48),"練習は左上のアイコンから部活を変更。",text);
        }
    }
}
