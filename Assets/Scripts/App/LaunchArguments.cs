using System;
using System.Runtime.InteropServices;

namespace ClubClash
{
    // iOS does not populate managed command-line arguments. Use the native
    // process arguments for the explicitly requested verification launch only.
    public static class LaunchArguments
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern int ClubClashHasLaunchArgument(string argument);
#endif
        public static bool Has(string argument)
        {
#if UNITY_IOS && !UNITY_EDITOR
            return ClubClashHasLaunchArgument(argument)!=0;
#else
            return Array.IndexOf(Environment.GetCommandLineArgs(),argument)>=0;
#endif
        }
    }
}
