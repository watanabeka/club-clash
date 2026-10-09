#import <UIKit/UIKit.h>
#import <AppTrackingTransparency/AppTrackingTransparency.h>

typedef void (*ClubTrackingCompletion)(int status);
extern "C" int ClubAttStatus()
{
    if (@available(iOS 14.0, *)) return (int)ATTrackingManager.trackingAuthorizationStatus;
    return 1; // Restricted on platforms without ATT.
}
extern "C" int ClubAttActive()
{
    return UIApplication.sharedApplication.applicationState == UIApplicationStateActive;
}
extern "C" void ClubAttRequest(ClubTrackingCompletion completed)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        if (!ClubAttActive() || ClubAttStatus() != 0)
        {
            if (completed) completed(ClubAttStatus());
            return;
        }
        if (@available(iOS 14.0, *))
        {
            [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:^(ATTrackingManagerAuthorizationStatus status) {
                dispatch_async(dispatch_get_main_queue(), ^{ if (completed) completed((int)status); });
            }];
        }
        else if (completed) completed(1);
    });
}
