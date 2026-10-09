using System;
using System.Collections;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
using AOT;
#endif

namespace ClubClash
{
    public enum TrackingAuthorizationStatus { NotDetermined, Restricted, Denied, Authorized }
    public interface ITrackingAuthorization
    {
        TrackingAuthorizationStatus Status { get; }
        bool IsActive { get; }
        void Request(Action<TrackingAuthorizationStatus> completed);
    }
    public static class StartupTrackingConsent
    {
        sealed class Response { public volatile int Status = -1; }
        public static IEnumerator Run(ITrackingAuthorization tracking, Action completed)
        {
            // Render the title before presenting the OS prompt; only active apps can request ATT.
            yield return null;
            var status = tracking.Status;
            while (status == TrackingAuthorizationStatus.NotDetermined)
            {
                while (!tracking.IsActive) yield return null;
                var response = new Response();
                tracking.Request(value => response.Status = (int)value);
                while (response.Status < 0) yield return null;
                status = (TrackingAuthorizationStatus)response.Status;
                // A competing system dialog can leave ATT undecided. Retry only after it clears.
                if (status == TrackingAuthorizationStatus.NotDetermined) yield return new WaitForSecondsRealtime(.5f);
            }
            Debug.Log("[ClubAds] ATT completed: " + status);
            completed();
        }
    }
    public sealed class IosTrackingAuthorization : ITrackingAuthorization
    {
#if UNITY_IOS && !UNITY_EDITOR
        delegate void NativeCompletion(int status);
        static readonly NativeCompletion Callback = Completed;
        static Action<TrackingAuthorizationStatus> pending;
        [DllImport("__Internal")] static extern int ClubAttStatus();
        [DllImport("__Internal")] static extern int ClubAttActive();
        [DllImport("__Internal")] static extern void ClubAttRequest(NativeCompletion completed);
        public TrackingAuthorizationStatus Status => (TrackingAuthorizationStatus)ClubAttStatus();
        public bool IsActive => ClubAttActive() != 0;
        public void Request(Action<TrackingAuthorizationStatus> completed)
        {
            var status = Status;
            if (status != TrackingAuthorizationStatus.NotDetermined) { completed(status); return; }
            bool requesting = pending != null;
            pending += completed;
            if (!requesting) ClubAttRequest(Callback);
        }
        [MonoPInvokeCallback(typeof(NativeCompletion))]
        static void Completed(int status)
        {
            var callback = pending; pending = null;
            callback?.Invoke((TrackingAuthorizationStatus)status);
        }
#else
        public TrackingAuthorizationStatus Status => TrackingAuthorizationStatus.Restricted;
        public bool IsActive => true;
        public void Request(Action<TrackingAuthorizationStatus> completed) { completed(Status); }
#endif
    }
}
