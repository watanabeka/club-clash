using System.Collections;
using System.IO;
using UnityEngine;

namespace ClubClash
{
    // An explicit development-only launch exercises the real SDK using Google's test unit.
    public sealed class AdMobNativeProof : MonoBehaviour
    {
        IEnumerator Start()
        {
            var game=GetComponent<GameController>();var ads=GetComponent<AdMobInterstitialAds>();
            string directory=Path.Combine(Application.persistentDataPath,"AdMobProof");Directory.CreateDirectory(directory);
            yield return null;
            while(ads.TrackingPromptPending)yield return null;
            float deadline=Time.realtimeSinceStartup+120;
            while(!ads.InterstitialReady && Time.realtimeSinceStartup<deadline)yield return null;
            if(!ads.InterstitialReady){File.WriteAllText(Path.Combine(directory,"sdk-proof.json"),"{\"passed\":false,\"reason\":\"test ad unavailable or ATT not answered\"}");yield break;}
            game.StartBattle();
            while(game.CurrentBattle.Phase==BattlePhase.Countdown)yield return null;
            game.CurrentBattle.Fighters[1].Hp=0;
            while(game.CurrentBattle.Round<2 || game.CurrentBattle.Phase==BattlePhase.Countdown)yield return null;
            game.CurrentBattle.Fighters[1].Hp=0;yield return new WaitForSecondsRealtime(2.1f);
            yield return new WaitForEndOfFrame();
            var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(directory,"result.png"),image.EncodeToPNG());Destroy(image);
            int opened=ads.InterstitialOpenedCount;game.ExitMatchResult(false);
            deadline=Time.realtimeSinceStartup+180;
            while(game.ResultTransitionPending && Time.realtimeSinceStartup<deadline)yield return null;
            bool passed=ads.InterstitialOpenedCount>opened && game.ScreenState==GameScreen.Title && !game.ResultTransitionPending;
            File.WriteAllText(Path.Combine(directory,"sdk-proof.json"),"{\"passed\":"+(passed?"true":"false")+",\"realSdkOpened\":"+(ads.InterstitialOpenedCount>opened?"true":"false")+",\"returnedToTitle\":"+(game.ScreenState==GameScreen.Title?"true":"false")+",\"developmentTestAd\":true}");
            Debug.Log("CLUB_AD_SDK_PROOF "+passed);
        }
    }
}
