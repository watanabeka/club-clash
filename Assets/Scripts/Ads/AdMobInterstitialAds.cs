using System;
using System.Collections;
using GoogleMobileAds.Api;
using GoogleMobileAds.Common;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

namespace ClubClash
{
    public sealed class AdMobInterstitialAds : MonoBehaviour, IInterstitialAds
    {
        const float CacheLifetime = 55 * 60, LoadTimeout = 60;
        ClubAdSettings settings;
        InterstitialAd cached;
        bool prepared, consenting, initializing, initialized, loading, presenting, destroyed;
        bool audioWasPaused;
        int generation;
        float loadedAt, retryAt, consentRetryAt, loadStartedAt, retryDelay = 30;
        Action pendingClose;
        public bool TrackingPromptPending { get; private set; }
        public bool InterstitialReady => Ready;
        public int InterstitialOpenedCount { get; private set; }
        public bool PrivacyOptionsRequired => prepared &&
            ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required;
        bool CanRequest => prepared && initialized && !consenting && ConsentInformation.CanRequestAds() &&
            Application.internetReachability != NetworkReachability.NotReachable;
        bool Ready => CanRequest && !presenting && cached != null &&
            Time.realtimeSinceStartup - loadedAt < CacheLifetime && cached.CanShowAd();

        IEnumerator Start()
        {
            // Desktop verification/capture runs must never make real ad requests.
            if (Application.isEditor || !Application.isMobilePlatform ||
                LaunchArguments.Has("-club-clash-smoke") || LaunchArguments.Has("-club-clash-store-capture")) yield break;
            TrackingPromptPending = true;
            yield return StartupTrackingConsent.Run(new IosTrackingAuthorization(), () => TrackingPromptPending = false);
            settings = ClubAdSettings.Load();
#if UNITY_IOS
            const bool ios = true;
#else
            const bool ios = false;
#endif
            if (settings == null || !settings.Enabled || !settings.Valid(ios)) yield break;
            MobileAdsEventExecutor.Initialize();
            prepared = true;
            GatherConsent();
        }

        void Main(Action action) => MobileAdsEventExecutor.ExecuteInUpdate(() =>
        {
            if (this != null && !destroyed) action();
        });

        void GatherConsent()
        {
            if (!prepared || consenting || presenting) return;
            var game = GetComponent<GameController>();
            if (game != null && game.ScreenState != GameScreen.Title) return;
            consenting = true;
            consentRetryAt = Time.realtimeSinceStartup + 60;
            try
            {
                ConsentInformation.Update(new ConsentRequestParameters
                {
                    TagForUnderAgeOfConsent = settings.UnderAgeOfConsent
                }, error => Main(() =>
                {
                    if (error != null)
                    {
                        Debug.LogWarning("[ClubAds] Consent update failed: " + error.Message);
                        consenting = false;
                        InitializeIfPermitted();
                        return;
                    }
                    try
                    {
                        ConsentForm.LoadAndShowConsentFormIfRequired(formError => Main(() =>
                        {
                            if (formError != null) Debug.LogWarning("[ClubAds] Consent form failed: " + formError.Message);
                            consenting = false;
                            InitializeIfPermitted();
                        }));
                    }
                    catch (Exception e) { Debug.LogWarning("[ClubAds] " + e.Message); consenting = false; InitializeIfPermitted(); }
                }));
            }
            catch (Exception e) { Debug.LogWarning("[ClubAds] " + e.Message); consenting = false; }
        }

        void InitializeIfPermitted()
        {
            if (!ConsentInformation.CanRequestAds() || initializing) return;
            if (initialized) { retryAt = 0; return; }
            initializing = true;
            try
            {
                MobileAds.SetRequestConfiguration(new RequestConfiguration
                {
                    TagForUnderAgeOfConsent = settings.UnderAgeOfConsent
                        ? TagForUnderAgeOfConsent.True : TagForUnderAgeOfConsent.False
                });
                MobileAds.Initialize(status => Main(() =>
                {
                    initializing = false;
                    if (status == null) return;
                    initialized = true;
                    Load();
                }));
            }
            catch (Exception e) { initializing = false; Debug.LogWarning("[ClubAds] " + e.Message); }
        }

        void Update()
        {
            if (!prepared || destroyed || presenting) return;
            if (!initialized && !initializing && !consenting && Time.realtimeSinceStartup >= consentRetryAt) GatherConsent();
            if (loading && Time.realtimeSinceStartup - loadStartedAt >= LoadTimeout)
            {
                generation++; loading = false; ScheduleRetry();
            }
            if (CanRequest && Time.realtimeSinceStartup >= retryAt && !Ready) Load();
        }

        void Load()
        {
            if (!CanRequest || presenting || loading || Ready) return;
            ClearCache();
            loading = true;
            loadStartedAt = Time.realtimeSinceStartup;
            int requestGeneration = generation;
#if UNITY_IOS
            string id = settings.InterstitialId(true, Debug.isDebugBuild);
#else
            string id = settings.InterstitialId(false, Debug.isDebugBuild);
#endif
            try
            {
                InterstitialAd.Load(id, new AdRequest(), (ad, error) => MobileAdsEventExecutor.ExecuteInUpdate(() =>
                {
                    if (this == null || destroyed || requestGeneration != generation) { ad?.Destroy(); return; }
                    loading = false;
                    if (error != null || ad == null)
                    {
                        ad?.Destroy(); ScheduleRetry();
                        Debug.LogWarning("[ClubAds] Interstitial load failed: " + error);
                        return;
                    }
                    cached = ad;
                    loadedAt = Time.realtimeSinceStartup;
                    retryDelay = 30;
                    if(Debug.isDebugBuild)Debug.Log("[ClubAds] Interstitial ready.");
                    ad.OnAdFullScreenContentOpened += () => Main(() => InterstitialOpenedCount++);
                    ad.OnAdFullScreenContentClosed += () => Main(() => Finish(ad));
                    ad.OnAdFullScreenContentFailed += failure => Main(() => Finish(ad));
                }));
            }
            catch (Exception e) { loading = false; ScheduleRetry(); Debug.LogWarning("[ClubAds] " + e.Message); }
        }

        void ScheduleRetry()
        {
            retryAt = Time.realtimeSinceStartup + retryDelay;
            retryDelay = Mathf.Min(retryDelay * 2, 300);
        }

        public void Show(Action closed)
        {
            // Never wait for a load at a result button: absence of an ad is a normal path.
            if (!Ready) { closed?.Invoke(); return; }
            var ad = cached;
            BeginPresentation(closed);
            try { ad.Show(); }
            catch (Exception e) { Debug.LogWarning("[ClubAds] " + e.Message); Finish(ad); }
        }

        void BeginPresentation(Action closed)
        {
            pendingClose = closed;
            presenting = true;
            audioWasPaused = AudioListener.pause;
            AudioListener.pause = true;
        }

        void Finish(InterstitialAd ad)
        {
            if (!presenting || cached != ad) return;
            try { ClearCache(); }
            finally { EndPresentation(); }
        }

        void EndPresentation()
        {
            if (!presenting) return;
            var callback = pendingClose;
            pendingClose = null;
            presenting = false;
            AudioListener.pause = audioWasPaused;
            retryAt = 0;
            // The next Update preloads a new, single-use ad after navigation.
            callback?.Invoke();
        }

        public void ShowPrivacyOptions(Action closed)
        {
            if (!PrivacyOptionsRequired || presenting) { closed?.Invoke(); return; }
            ClearCache();
            BeginPresentation(closed);
            try
            {
                ConsentForm.ShowPrivacyOptionsForm(error => Main(() =>
                {
                    if (error != null) Debug.LogWarning("[ClubAds] Privacy form failed: " + error.Message);
                    EndPresentation();
                    InitializeIfPermitted();
                }));
            }
            catch (Exception e) { Debug.LogWarning("[ClubAds] " + e.Message); EndPresentation(); }
        }

        void ClearCache()
        {
            generation++;
            loading = false;
            var old = cached;
            cached = null;
            old?.Destroy();
        }

        void OnDestroy()
        {
            destroyed = true;
            ClearCache();
            EndPresentation();
        }
    }
}
