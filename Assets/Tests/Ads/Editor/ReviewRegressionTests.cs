using System;
using System.Reflection;
using NUnit.Framework;

public sealed class ReviewRegressionTests
{
    [TestCase("ThreeMatchesPersistAcrossLaunches")]
    [TestCase("AutomaticRequestIsOnceOnly")]
    [TestCase("SupportHistorySuppressesFurtherRequests")]
    [TestCase("UnsupportedOrFailedSupportIsNotSaved")]
    [TestCase("SafeLegacyAndMaximumCounter")]
    [TestCase("CompleteMatchCountsOnceThroughRematchAndTitle")]
    [TestCase("ThirdMatchWaitsForAdThenTitle")]
    [TestCase("TwoPlayerDialogBlocksNavigationAndCloseDoesNotReview")]
    [TestCase("SupportedUserOnlyHasCloseAction")]
    public void ReviewFlow(string check)
    {
        try{Assembly.Load("Assembly-CSharp-Editor").GetType("ReviewFlowChecks").GetMethod(check).Invoke(null,null);}
        catch(TargetInvocationException e){throw e.InnerException??e;}
    }
}
