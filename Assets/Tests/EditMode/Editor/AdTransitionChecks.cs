#if UNITY_EDITOR
using System;
using System.Reflection;
using ClubClash;
using UnityEngine;

public static class AdTransitionChecks
{
    sealed class FakeTracking : ITrackingAuthorization
    {
        public TrackingAuthorizationStatus Status { get; set; }
        public bool IsActive { get; set; } = true;
        public int Requests;
        public Action<TrackingAuthorizationStatus> Closed;
        public void Request(Action<TrackingAuthorizationStatus> closed) { Requests++; Closed=closed; }
    }
    sealed class FakeAds : IInterstitialAds
    {
        public bool PrivacyOptionsRequired => false;
        public Action Closed;
        public int Shows;
        public bool Immediate, Throw;
        public void Show(Action closed)
        {
            Shows++; Closed = closed;
            if (Throw) throw new Exception("SDK show failure");
            if (Immediate) closed();
        }
        public void ShowPrivacyOptions(Action closed) { closed(); }
    }
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }

    public static void WaitsForClose()
    {
        var ads = new FakeAds(); using (var exit = new MatchResultTransition(ads))
        {
            int navigations = 0;
            Assert(exit.Request(true, () => navigations++), "Result action rejected");
            Assert(exit.IsPending && navigations == 0 && ads.Shows == 1, "Navigated before ad close");
            ads.Closed();
            Assert(!exit.IsPending && navigations == 1, "Close did not resume navigation");
        }
    }
    public static void UnavailableAd()
    {
        var ads = new FakeAds { Immediate = true }; using (var exit = new MatchResultTransition(ads))
        {
            int count = 0; exit.Request(true, () => count++);
            Assert(count == 1 && !exit.IsPending, "Unavailable/offline ad blocked navigation");
        }
    }
    public static void MissingProvider()
    {
        using (var exit = new MatchResultTransition(null))
        {
            int count = 0; exit.Request(true, () => count++);
            Assert(count == 1 && !exit.IsPending, "Desktop fallback blocked navigation");
        }
    }
    public static void ShowFailure()
    {
        var ads = new FakeAds { Throw = true }; using (var exit = new MatchResultTransition(ads))
        {
            int count = 0; exit.Request(true, () => count++); ads.Closed();
            Assert(count == 1 && !exit.IsPending, "Show exception or late close repeated navigation");
        }
    }
    public static void DuplicateButtonsAndEvents()
    {
        var ads = new FakeAds(); using (var exit = new MatchResultTransition(ads))
        {
            int count = 0; exit.Request(true, () => count++);
            Assert(!exit.Request(true, () => count += 100), "Second result button accepted during ad");
            ads.Closed(); ads.Closed();
            Assert(count == 1 && ads.Shows == 1, "Duplicate SDK events navigated twice");
        }
    }
    public static void LateEventFromPreviousMatch()
    {
        var ads = new FakeAds(); using (var exit = new MatchResultTransition(ads))
        {
            int count = 0; exit.Request(true, () => count++); var old = ads.Closed; old();
            exit.Request(true, () => count++); old();
            Assert(exit.IsPending && count == 1 && ads.Shows == 2, "Old ad closed the next match's transition");
            ads.Closed(); Assert(count == 2 && !exit.IsPending, "Next match close was lost");
        }
    }
    public static void DisposalCancelsNavigation()
    {
        var ads = new FakeAds(); var exit = new MatchResultTransition(ads);
        int count = 0; exit.Request(true, () => count++); exit.Dispose(); ads.Closed();
        Assert(count == 0 && !exit.Request(true, () => count++), "Disposed scene navigated after late event");
    }
    public static void NoAdOutsideResults()
    {
        var ads = new FakeAds(); using (var exit = new MatchResultTransition(ads))
        {
            Assert(!exit.Request(false, () => { throw new Exception("not a result"); }) && ads.Shows == 0, "Ad shown outside final result");
        }
    }
    public static void DevelopmentAndProductionIds()
    {
        var settings = ClubAdSettings.Load();
        Assert(settings.Enabled && !settings.UseTestAds && settings.Valid(true), "Production iOS settings missing");
        Assert(settings.ApplicationId(true) == "ca-app-pub-4304526617205005~2604950711", "Wrong iOS app ID");
        Assert(settings.InterstitialId(true) == "ca-app-pub-4304526617205005/6424951235", "Wrong production interstitial");
        Assert(settings.InterstitialId(true, true) == ClubAdSettings.TestIosInterstitialId, "Development build requests live ads");
        settings.IosInterstitialId = "";
        Assert(!settings.Valid(true), "Empty production ID accepted");
        settings.UseTestAds = true;
        Assert(settings.Valid(true), "Official test settings rejected");
    }
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void CompleteMatch(GameController game)
    {
        var battle = game.CurrentBattle;
        for (int round = 0; round < 2; round++)
        {
            for (int i = 0; i < 400 && battle.Phase == BattlePhase.Countdown; i++) battle.Update(.01f, InputFrame.Empty);
            battle.Fighters[1].Hp = 0; battle.Update(.01f, InputFrame.Empty);
            if (round == 0) Assert(battle.NextRound(), "First round failed");
        }
        Assert(battle.Phase == BattlePhase.MatchOver, "Match did not finish");
        typeof(GameController).GetField("roundAge", Private).SetValue(game, 2f);
    }
    static void WithGame(Action<GameController> check)
    {
        string[] keys = { "ccu.p1", "ccu.p2", "ccu.stage", "ccu.difficulty" };
        bool[] existed = new bool[keys.Length]; string[] strings = new string[2]; int[] ints = new int[2];
        for (int i = 0; i < keys.Length; i++) existed[i] = PlayerPrefs.HasKey(keys[i]);
        for (int i = 0; i < 2; i++) { strings[i] = PlayerPrefs.GetString(keys[i]); ints[i] = PlayerPrefs.GetInt(keys[i+2]); }
        var go = new GameObject("Ad result verification");
        try
        {
            var game = go.AddComponent<GameController>();
            if (game.Visuals == null) typeof(GameController).GetMethod("Awake", Private).Invoke(game, null);
            game.SetReviewService(new ReviewFlowChecks.MemoryStore(),null);
            check(game);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
            for (int i = 0; i < keys.Length; i++)
            {
                if (!existed[i]) PlayerPrefs.DeleteKey(keys[i]);
                else if (i < 2) PlayerPrefs.SetString(keys[i], strings[i]);
                else PlayerPrefs.SetInt(keys[i], ints[i-2]);
            }
            PlayerPrefs.Save();
        }
    }
    public static void ResultRematchRoute()
    {
        WithGame(game =>
        {
            var ads = new FakeAds(); game.SetResultAdService(ads); game.StartBattle();
            Assert(!game.ExitMatchResult(true) && ads.Shows == 0, "Countdown showed an ad");
            CompleteMatch(game); var finished = game.CurrentBattle;
            game.RouteUiPoint(GameController.ResultRetryRect.center);
            game.RouteUiPoint(GameController.ResultTitleRect.center);
            Assert(game.ResultTransitionPending && game.CurrentBattle == finished && ads.Shows == 1, "Result input navigated before close");
            ads.Closed(); ads.Closed();
            Assert(game.InBattle && game.CurrentBattle != finished && game.CurrentBattle.Phase == BattlePhase.Countdown, "Close did not start one rematch");
        });
    }
    public static void ResultTitleRoute()
    {
        WithGame(game =>
        {
            var ads = new FakeAds(); game.SetResultAdService(ads); game.StartBattle(); CompleteMatch(game);
            game.RouteUiPoint(GameController.ResultTitleRect.center);
            Assert(game.InBattle && game.ResultTransitionPending, "Title appeared before close");
            ads.Closed();
            Assert(game.ScreenState == GameScreen.Title && !game.ResultTransitionPending, "Ad close did not return to title");
            Assert(!game.ExitMatchResult(false) && ads.Shows == 1, "Title requested another result ad");
        });
    }
    public static void PracticeAndEarlyKoHaveNoAds()
    {
        WithGame(game =>
        {
            var ads = new FakeAds { Immediate = true }; game.SetResultAdService(ads); game.StartTraining();
            Assert(!game.ExitMatchResult(false), "Practice requested result ad");
            game.Mode = BattleMode.Cpu; game.StartBattle(); CompleteMatch(game);
            typeof(GameController).GetField("roundAge", Private).SetValue(game, .2f);
            game.RouteUiPoint(GameController.ResultRetryRect.center);
            Assert(ads.Shows == 0, "Final KO animation was interrupted by an ad");
            typeof(GameController).GetField("roundAge", Private).SetValue(game, 2f);
            game.RouteUiPoint(GameController.ResultTitleRect.center);
            Assert(game.ScreenState == GameScreen.Title && ads.Shows == 1, "Unavailable ad did not permit title route");
        });
    }
    public static void AttExistingDecisionDoesNotPrompt()
    {
        foreach(var status in new[]{TrackingAuthorizationStatus.Authorized,TrackingAuthorizationStatus.Denied,TrackingAuthorizationStatus.Restricted})
        {
            var tracking=new FakeTracking{Status=status};int completed=0;
            var flow=StartupTrackingConsent.Run(tracking,()=>completed++);
            Assert(flow.MoveNext(),"Title did not render before ATT");
            Assert(!flow.MoveNext() && completed==1 && tracking.Requests==0,"Existing ATT decision prompted again or blocked startup");
        }
    }
    public static void AttWaitsForActiveAndResponse()
    {
        foreach(var result in new[]{TrackingAuthorizationStatus.Authorized,TrackingAuthorizationStatus.Denied,TrackingAuthorizationStatus.Restricted})
        {
            var tracking=new FakeTracking{Status=TrackingAuthorizationStatus.NotDetermined,IsActive=false};int completed=0;
            var flow=StartupTrackingConsent.Run(tracking,()=>completed++);
            flow.MoveNext();flow.MoveNext();Assert(tracking.Requests==0,"Inactive app requested ATT");
            tracking.IsActive=true;flow.MoveNext();Assert(tracking.Requests==1 && completed==0,"SDK started before ATT answer");
            tracking.Closed(result);Assert(!flow.MoveNext() && completed==1,"ATT answer did not continue startup");
        }
    }
    public static void AttCompetingDialogRetries()
    {
        var tracking=new FakeTracking{Status=TrackingAuthorizationStatus.NotDetermined};int completed=0;
        var flow=StartupTrackingConsent.Run(tracking,()=>completed++);
        flow.MoveNext();flow.MoveNext();tracking.Closed(TrackingAuthorizationStatus.NotDetermined);
        Assert(flow.MoveNext() && completed==0,"Undecided ATT treated as authorization");
        flow.MoveNext();Assert(tracking.Requests==2,"Competing dialog suppressed ATT permanently");
        tracking.Closed(TrackingAuthorizationStatus.Denied);Assert(!flow.MoveNext() && completed==1,"Retry denial blocked startup");
    }
    public static void PodDeploymentTargetRepair()
    {
        string source="IPHONEOS_DEPLOYMENT_TARGET = 12.0;\nIPHONEOS_DEPLOYMENT_TARGET = 15.0;\nIPHONEOS_DEPLOYMENT_TARGET = 16.4;";
        string fixedTargets=ClubAdsIosPostprocess.RaisePodDeploymentTargets(source);
        Assert(!fixedTargets.Contains("12.0") && fixedTargets.Contains("16.4"),"Old pod minimum retained or newer minimum lowered");
        Assert(ClubAdsIosPostprocess.RaisePodDeploymentTargets(fixedTargets)==fixedTargets,"Pod target repair not idempotent");
    }
}
#endif
