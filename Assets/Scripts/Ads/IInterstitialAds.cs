using System;

namespace ClubClash
{
    public interface IInterstitialAds
    {
        bool PrivacyOptionsRequired { get; }
        void Show(Action closed);
        void ShowPrivacyOptions(Action closed);
    }
}
