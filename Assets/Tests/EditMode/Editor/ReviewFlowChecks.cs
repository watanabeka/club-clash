#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using ClubClash;
using UnityEngine;

public static class ReviewFlowChecks
{
    public sealed class MemoryStore : IReviewStateStore
    {
        readonly Dictionary<string,int> values=new Dictionary<string,int>();
        public int Read(string key)=>values.TryGetValue(key,out var value)?value:0;
        public void Write(string key,int value){values[key]=value;}
        public void Save(){}
    }
    sealed class Requester : IReviewRequester
    {
        public int Calls;public bool Supported=true,Throw;
        public bool Request(){Calls++;if(Throw)throw new Exception("review unavailable");return Supported;}
    }
    sealed class Ads : IInterstitialAds
    {
        public Action Closed;public int Calls;
        public bool PrivacyOptionsRequired=>false;
        public void Show(Action closed){Calls++;Closed=closed;}
        public void ShowPrivacyOptions(Action closed){closed();}
    }
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static void ThreeMatchesPersistAcrossLaunches()
    {
        var store=new MemoryStore();var request=new Requester();var prompts=new ReviewPrompter(store,request);
        prompts.RecordCompletedMatch();prompts.RecordCompletedMatch();Check(!prompts.AutomaticDue && !prompts.RequestAutomatically() && request.Calls==0,"Early review");
        prompts=new ReviewPrompter(store,request);Check(prompts.CompletedMatches==2,"Completion history lost");
        prompts.RecordCompletedMatch();Check(prompts.AutomaticDue && prompts.RequestAutomatically() && request.Calls==1,"Third completion did not request");
    }
    public static void AutomaticRequestIsOnceOnly()
    {
        foreach(bool supported in new[]{true,false})
        {
            var store=new MemoryStore();var request=new Requester{Supported=supported};var prompts=new ReviewPrompter(store,request);
            for(int i=0;i<3;i++)prompts.RecordCompletedMatch();prompts.RequestAutomatically();
            prompts=new ReviewPrompter(store,request);prompts.RecordCompletedMatch();
            Check(!prompts.AutomaticDue && !prompts.RequestAutomatically() && request.Calls==1,"Repeated automatic request");
            Check(!prompts.SupportRequested,"Automatic request falsely recorded a posted review/support action");
        }
    }
    public static void SupportHistorySuppressesFurtherRequests()
    {
        var store=new MemoryStore();var request=new Requester();var prompts=new ReviewPrompter(store,request);
        Check(prompts.RequestSupport() && prompts.SupportRequested,"Support action not saved");
        prompts=new ReviewPrompter(store,request);for(int i=0;i<3;i++)prompts.RecordCompletedMatch();
        Check(prompts.SupportRequested && !prompts.AutomaticDue && !prompts.RequestSupport() && request.Calls==1,"Support action repeated after restart");
    }
    public static void UnsupportedOrFailedSupportIsNotSaved()
    {
        foreach(bool throws in new[]{false,true})
        {
            var store=new MemoryStore();var prompts=new ReviewPrompter(store,new Requester{Supported=false,Throw=throws});
            Check(!prompts.RequestSupport() && !prompts.SupportRequested,"Unavailable API falsely saved support history");
        }
    }
    public static void SafeLegacyAndMaximumCounter()
    {
        var store=new MemoryStore();store.Write(ReviewPrompter.MatchesKey,-4);var prompts=new ReviewPrompter(store,new Requester());
        Check(prompts.CompletedMatches==0 && !prompts.AutomaticDue,"Legacy default unsafe");prompts.RecordCompletedMatch();Check(prompts.CompletedMatches==1,"Negative counter not repaired");
        store.Write(ReviewPrompter.MatchesKey,int.MaxValue);prompts.RecordCompletedMatch();Check(prompts.CompletedMatches==int.MaxValue,"Counter overflow");
    }
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    public static void Finish(GameController game)
    {
        var battle=game.CurrentBattle;
        for(int round=0;round<2;round++)
        {
            for(int i=0;i<400 && battle.Phase==BattlePhase.Countdown;i++)battle.Update(.01f,InputFrame.Empty);
            battle.Fighters[1].Hp=0;battle.Update(.01f,InputFrame.Empty);
            if(round==0)Check(battle.NextRound(),"First round missing");
        }
        Check(battle.Phase==BattlePhase.MatchOver,"Not a final result");
        typeof(GameController).GetField("roundAge",Private).SetValue(game,2f);
    }
    static void WithGame(Action<GameController,MemoryStore,Requester> test)
    {
        string[] keys={"ccu.p1","ccu.p2","ccu.stage","ccu.difficulty"};bool[] exists=new bool[4];
        string[] strings={PlayerPrefs.GetString(keys[0]),PlayerPrefs.GetString(keys[1])};int[] ints={PlayerPrefs.GetInt(keys[2]),PlayerPrefs.GetInt(keys[3])};
        for(int i=0;i<4;i++)exists[i]=PlayerPrefs.HasKey(keys[i]);
        var owner=new GameObject("Review integration check");
        try{var game=owner.AddComponent<GameController>();if(game.Visuals==null)typeof(GameController).GetMethod("Awake",Private).Invoke(game,null);var store=new MemoryStore();var requester=new Requester();game.SetReviewService(store,requester);game.SetResultAdService(null);test(game,store,requester);}
        finally
        {
            UnityEngine.Object.DestroyImmediate(owner);
            for(int i=0;i<4;i++)if(!exists[i])PlayerPrefs.DeleteKey(keys[i]);else if(i<2)PlayerPrefs.SetString(keys[i],strings[i]);else PlayerPrefs.SetInt(keys[i],ints[i-2]);
            PlayerPrefs.Save();
        }
    }
    public static void CompleteMatchCountsOnceThroughRematchAndTitle()
    {
        WithGame((game,store,request)=>
        {
            game.StartBattle();Finish(game);game.ExitMatchResult(true);
            Check(game.CompletedReviewMatches==1 && game.InBattle,"First match not recorded through rematch");
            game.ShowTitle();game.ShowTitle();Check(game.CompletedReviewMatches==1 && !game.ReviewRequestPending,"Aborted match counted");
            game.StartTraining();game.ShowTitle();Check(game.CompletedReviewMatches==1,"Practice counted");
            game.Mode=BattleMode.Cpu;game.StartBattle();Finish(game);game.ExitMatchResult(false);game.ShowTitle();
            Check(game.CompletedReviewMatches==2 && !game.ReviewRequestPending,"Repeated title counted twice or review came early");
        });
    }
    public static void ThirdMatchWaitsForAdThenTitle()
    {
        WithGame((game,store,request)=>
        {
            store.Write(ReviewPrompter.MatchesKey,2);var ads=new Ads();game.SetResultAdService(ads);
            game.StartBattle();Finish(game);game.RouteUiPoint(GameController.ResultTitleRect.center);
            Check(game.InBattle && game.CompletedReviewMatches==3 && game.ResultTransitionPending && !game.ReviewRequestPending && request.Calls==0,"Review requested over interstitial or before title");
            ads.Closed();Check(game.ScreenState==GameScreen.Title && game.ReviewRequestPending && request.Calls==0,"Title not rendered before review scheduling");
            ads.Closed();Check(game.CompletedReviewMatches==3,"Duplicate ad close counted twice");
        });
    }
    public static void TwoPlayerDialogBlocksNavigationAndCloseDoesNotReview()
    {
        WithGame((game,store,request)=>
        {
            game.RouteUiPoint(GameController.TitleLocalRect.center);Check(game.SupportDialogOpen && !game.InBattle,"Two-player dialog missing");
            game.RouteUiPoint(GameController.TitleBattleRect.center);Check(game.ScreenState==GameScreen.Title && game.SupportDialogOpen,"Underlying button received dialog input");
            game.RouteUiPoint(GameController.SupportCloseRect.center);Check(!game.SupportDialogOpen && request.Calls==0 && !game.ReviewSupportRequested,"Close requested or saved a review");
            game.OpenSupportDialog();game.RouteUiPoint(GameController.SupportReviewRect.center);Check(!game.SupportDialogOpen && game.ReviewRequestPending && request.Calls==0,"Review button did not queue after closing dialog");
        });
    }
    public static void SupportedUserOnlyHasCloseAction()
    {
        WithGame((game,store,request)=>
        {
            store.Write(ReviewPrompter.SupportKey,1);game.OpenSupportDialog();
            game.RouteUiPoint(GameController.SupportReviewRect.center);Check(game.SupportDialogOpen && !game.ReviewRequestPending && request.Calls==0,"Hidden review button accepted input");
            game.RouteUiPoint(GameController.SupportOnlyCloseRect.center);Check(!game.SupportDialogOpen,"Single close action missing");
        });
    }
}
#endif
