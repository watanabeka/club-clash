using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ClubClash
{
    [Serializable]
    public sealed class ClubAdSettings
    {
        public bool Enabled = true, UseTestAds = true, UnderAgeOfConsent;
        public string IosApplicationId = "", IosInterstitialId = "";
        public string AndroidApplicationId = "", AndroidInterstitialId = "";
        public const string TestIosApplicationId = "ca-app-pub-3940256099942544~1458002511";
        public const string TestAndroidApplicationId = "ca-app-pub-3940256099942544~3347511713";
        public const string TestIosInterstitialId = "ca-app-pub-3940256099942544/4411468910";
        public const string TestAndroidInterstitialId = "ca-app-pub-3940256099942544/1033173712";

        public string ApplicationId(bool ios) => UseTestAds
            ? (ios ? TestIosApplicationId : TestAndroidApplicationId)
            : (ios ? IosApplicationId : AndroidApplicationId);
        public string InterstitialId(bool ios, bool development = false) => UseTestAds || development
            ? (ios ? TestIosInterstitialId : TestAndroidInterstitialId)
            : (ios ? IosInterstitialId : AndroidInterstitialId);

        public bool Valid(bool ios) => ValidId(ApplicationId(ios), '~') && ValidId(InterstitialId(ios), '/');
        static bool ValidId(string id, char separator) => Regex.IsMatch(id ?? "", @"\Aca-app-pub-[0-9]{16}" + separator + @"[0-9]{10}\z");
        public static ClubAdSettings Load()
        {
            var asset = Resources.Load<TextAsset>("ClubAdSettings");
            return asset == null ? new ClubAdSettings() : JsonUtility.FromJson<ClubAdSettings>(asset.text);
        }
    }
}
