using System;

namespace ClubClash
{
    // A result action owns the transition until its ad finishes. Duplicate SDK events
    // and repeated taps must never start two matches or navigate twice.
    public sealed class MatchResultTransition : IDisposable
    {
        readonly IInterstitialAds ads;
        int generation;
        bool disposed;
        public bool IsPending { get; private set; }

        public MatchResultTransition(IInterstitialAds ads) { this.ads = ads; }

        public bool Request(bool resultVisible, Action navigate)
        {
            if (disposed || IsPending || !resultVisible || navigate == null) return false;
            IsPending = true;
            int request = ++generation;
            Action finish = () =>
            {
                if (disposed || !IsPending || request != generation) return;
                IsPending = false;
                generation++;
                navigate();
            };
            if (ads == null) finish();
            else
            {
                try { ads.Show(finish); }
                catch (Exception) { finish(); }
            }
            return true;
        }

        public void Dispose() { disposed = true; IsPending = false; generation++; }
    }
}
