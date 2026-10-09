using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ClubClash
{
    // Explicit development-only verification. Does not write real review history.
    public sealed class ReviewNativeProof : MonoBehaviour
    {
        sealed class Store : IReviewStateStore
        {
            readonly Dictionary<string,int> values=new Dictionary<string,int>();
            public int Read(string key)=>values.TryGetValue(key,out var value)?value:0;
            public void Write(string key,int value){values[key]=value;}
            public void Save(){}
        }
        sealed class Requester : IReviewRequester
        {
            public int Calls;public bool Supported;
            public bool Request(){Calls++;Supported=new IosReviewRequester().Request();return Supported;}
        }
        IEnumerator Start()
        {
            var game=GetComponent<GameController>();var ads=GetComponent<AdMobInterstitialAds>();
            var store=new Store();var request=new Requester();game.SetReviewService(store,request);game.SetResultAdService(null);
            string directory=Path.Combine(Application.persistentDataPath,"ReviewProof");Directory.CreateDirectory(directory);
            yield return null;while(ads.TrackingPromptPending)yield return null;
            for(int match=0;match<3;match++)
            {
                game.Mode=BattleMode.Cpu;game.StartBattle();
                while(game.CurrentBattle.Phase==BattlePhase.Countdown)yield return null;
                game.CurrentBattle.Fighters[1].Hp=0;
                while(game.CurrentBattle.Round<2 || game.CurrentBattle.Phase==BattlePhase.Countdown)yield return null;
                game.CurrentBattle.Fighters[1].Hp=0;yield return new WaitForSecondsRealtime(2.1f);
                game.ExitMatchResult(match<2);
            }
            float deadline=Time.realtimeSinceStartup+60;
            while(request.Calls==0 && Time.realtimeSinceStartup<deadline)yield return null;
            File.WriteAllText(Path.Combine(directory,"automatic.json"),"{\"matches\":"+game.CompletedReviewMatches+",\"calls\":"+request.Calls+",\"supported\":"+(request.Supported?"true":"false")+",\"title\":"+(game.ScreenState==GameScreen.Title?"true":"false")+"}");
            Debug.Log("CLUB_REVIEW_AUTO_READY calls="+request.Calls+" supported="+request.Supported);
            // The reviewer dismisses Apple's sheet without posting, then exercises the
            // actual two-player button and support action through native touch.
            deadline=Time.realtimeSinceStartup+300;
            bool photographed=false;
            while(!game.ReviewSupportRequested && Time.realtimeSinceStartup<deadline)
            {
                if(game.SupportDialogOpen && !photographed){yield return Capture(directory,"support.png");photographed=true;}
                yield return null;
            }
            yield return new WaitForSecondsRealtime(.5f);
            File.WriteAllText(Path.Combine(directory,"support.json"),"{\"calls\":"+request.Calls+",\"supportHistory\":"+(game.ReviewSupportRequested?"true":"false")+",\"textFits\":"+(string.IsNullOrEmpty(game.LastTextOverflow)?"true":"false")+"}");
            deadline=Time.realtimeSinceStartup+120;
            while(!game.SupportDialogOpen && Time.realtimeSinceStartup<deadline)yield return null;
            if(game.SupportDialogOpen)yield return Capture(directory,"supported.png");
        }
        static IEnumerator Capture(string directory,string name)
        {
            yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(directory,name),image.EncodeToPNG());Destroy(image);
        }
    }
}
