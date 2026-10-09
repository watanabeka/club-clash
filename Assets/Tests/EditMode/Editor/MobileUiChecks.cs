#if UNITY_EDITOR
using System;
using ClubClash;
using UnityEngine;

// Runs with the project's existing editor verification entry point.
public static class MobileUiChecks
{
    static void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
    public static void Run()
    {
        foreach (Rect safe in new[] { new Rect(59,21,726,369), new Rect(44,0,844,430), new Rect(0,20,1024,748) })
        {
            Rect view=GameController.FitDesignViewport(safe);
            Assert(safe.Contains(view.min) && view.xMax<=safe.xMax+.01f && view.yMax<=safe.yMax+.01f,"UI exceeds safe area");
        }
        Assert(GameController.RandomOpponentRect.yMax+24<=GameController.OpponentSideRect.y,"Random button must be above CPU with a gap");
        Assert(GameController.StageRect(0).yMax+40<=GameController.DifficultyRect(0).y,"Stage and level rows need separation");
        Assert(GameController.DifficultyRect(0).yMax+24<=GameController.SelectionStartRect.y,"Level and battle rows need separation");
        Assert(GameController.TitleBattleRect.yMax+32<=GameController.TitlePracticeRect.y && GameController.TitlePracticeRect.yMax+32<=GameController.TitleLocalRect.y,"Main button gaps");
        Vector2[] controls={GameController.JumpControlPoint,GameController.GuardControlPoint,GameController.LightControlPoint,GameController.StrongControlPoint};
        for(int i=0;i<controls.Length;i++)for(int j=i+1;j<controls.Length;j++)
            Assert(Vector2.Distance(controls[i],controls[j])>=118,"Face buttons overlap or lack spacing");
        Assert(controls[0].x==controls[2].x && controls[1].y==controls[3].y && controls[0].y<controls[1].y && controls[2].y>controls[1].y,"Controller diamond layout");
        Assert(AppReleaseConfig.HomeScreenName=="部活ファイト" && AppReleaseConfig.StoreName.StartsWith("部活対抗ファイト") && AppReleaseConfig.StoreName.Length<=30,"Japanese app names");
        Assert(BodyArtProfile.Scale("soccer",187)<.85f && BodyArtProfile.Scale("tennis",187)<.84f && BodyArtProfile.Scale("badminton",187)<.78f,"Oversized characters need smaller shared scales");
        Assert(Mathf.Abs(BodyArtProfile.Scale("archery",187)-180f/187f*Mathf.Sqrt(53f/52*116f/129))<.0001f,"Archery scale must stay unchanged");
    }
}
#endif
