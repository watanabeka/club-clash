using System;
using UnityEngine;

namespace ClubClash
{
    public interface IReviewRequester { bool Request(); }
    public interface IReviewStateStore
    {
        int Read(string key);
        void Write(string key, int value);
        void Save();
    }
    public sealed class PlayerPrefsReviewStore : IReviewStateStore
    {
        public int Read(string key) => PlayerPrefs.GetInt(key, 0);
        public void Write(string key, int value) => PlayerPrefs.SetInt(key, value);
        public void Save() => PlayerPrefs.Save();
    }
    public sealed class IosReviewRequester : IReviewRequester
    {
        public bool Request()
        {
#if UNITY_IOS && !UNITY_EDITOR
            // The return value means the API is supported, never that a review was posted.
            return UnityEngine.iOS.Device.RequestStoreReview();
#else
            return false;
#endif
        }
    }
    public sealed class ReviewPrompter
    {
        public const string MatchesKey = "ccu.review.completedMatches";
        public const string AutomaticKey = "ccu.review.automaticRequested";
        public const string SupportKey = "ccu.review.supportRequested";
        readonly IReviewStateStore store;
        readonly IReviewRequester requester;
        public int CompletedMatches => Mathf.Max(0, store.Read(MatchesKey));
        public bool SupportRequested => store.Read(SupportKey) == 1;
        public bool AutomaticDue => CompletedMatches >= 3 && store.Read(AutomaticKey) != 1 && !SupportRequested;

        public ReviewPrompter(IReviewStateStore store, IReviewRequester requester)
        {
            this.store = store; this.requester = requester;
        }
        public void RecordCompletedMatch()
        {
            store.Write(MatchesKey, CompletedMatches < int.MaxValue ? CompletedMatches + 1 : int.MaxValue);
            store.Save();
        }
        public bool RequestAutomatically()
        {
            if (!AutomaticDue) return false;
            // One automatic attempt per install, even if StoreKit chooses not to show UI.
            store.Write(AutomaticKey, 1); store.Save();
            return TryRequest();
        }
        public bool RequestSupport()
        {
            if (SupportRequested || !TryRequest()) return false;
            // StoreKit exposes no display, dismissal or submission callback. This is an
            // explicit support-action history, not an assertion that a review was posted.
            store.Write(SupportKey, 1); store.Save();
            return true;
        }
        bool TryRequest()
        {
            try { return requester != null && requester.Request(); }
            catch (Exception e) { Debug.LogWarning("[ClubReview] Review request failed: " + e.Message); return false; }
        }
    }
}
