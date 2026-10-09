#import <Foundation/Foundation.h>

extern "C" int ClubClashHasLaunchArgument(const char *argument)
{
    if (argument == nullptr) return 0;
    NSString *flag = [NSString stringWithUTF8String:argument];
    return [[[NSProcessInfo processInfo] arguments] containsObject:flag] ? 1 : 0;
}
