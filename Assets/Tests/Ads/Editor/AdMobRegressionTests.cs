using System;
using System.Reflection;
using NUnit.Framework;

public sealed class AdMobRegressionTests
{
    // Existing runtime code intentionally stays in Assembly-CSharp. Reflection keeps
    // this test-only assembly from imposing a new assembly boundary on the game.
    [TestCase("WaitsForClose")]
    [TestCase("UnavailableAd")]
    [TestCase("MissingProvider")]
    [TestCase("ShowFailure")]
    [TestCase("DuplicateButtonsAndEvents")]
    [TestCase("LateEventFromPreviousMatch")]
    [TestCase("DisposalCancelsNavigation")]
    [TestCase("NoAdOutsideResults")]
    [TestCase("DevelopmentAndProductionIds")]
    [TestCase("ResultRematchRoute")]
    [TestCase("ResultTitleRoute")]
    [TestCase("PracticeAndEarlyKoHaveNoAds")]
    [TestCase("AttExistingDecisionDoesNotPrompt")]
    [TestCase("AttWaitsForActiveAndResponse")]
    [TestCase("AttCompetingDialogRetries")]
    [TestCase("PodDeploymentTargetRepair")]
    public void AdvertisementTransition(string check)
    {
        try { Assembly.Load("Assembly-CSharp-Editor").GetType("AdTransitionChecks").GetMethod(check).Invoke(null, null); }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
    }

    [Test]
    public void EditorSdkAndUiAssembliesLoad()
    {
        // A passing game test does not prove Unity loaded optional Editor plugins.
        // Force their types to resolve so a missing uGUI reference fails the suite.
        var ui = Assembly.Load("UnityEngine.UI");
        Assert.That(ui.GetType("UnityEngine.UI.Button", true), Is.Not.Null);
        foreach (string name in new[] { "GoogleMobileAds.Unity", "GoogleMobileAds.Ump.Unity" })
        {
            var assembly = Assembly.Load(name);
            Assert.That(assembly.GetTypes().Length, Is.GreaterThan(0), name);
        }
    }

    [Test]
    public void FullExistingCombatAndMobileLayoutSuite()
    {
        var type = Assembly.Load("Assembly-CSharp-Editor").GetType("CombatTests");
        bool passed = (bool)type.GetMethod("RunAll").Invoke(null, null);
        Assert.That(passed, Is.True, type.GetProperty("LastReport").GetValue(null).ToString());
    }
}
