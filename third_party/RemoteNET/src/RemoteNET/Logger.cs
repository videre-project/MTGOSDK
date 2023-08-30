using System;
using System.Diagnostics;


namespace RemoteNET
{
  internal class Logger
  {
    public static Lazy<bool> DebugInRelease = new Lazy<bool>(() =>
      !string.IsNullOrWhiteSpace(
          Environment.GetEnvironmentVariable("REMOTE_NET_DIVER_MAGIC_DEBUG")));

  #if DEBUG
    public static bool IsDebug = true;
  #else
    public static bool IsDebug = false;
  #endif

    internal static void Debug(string s)
    {
      if (IsDebug || Debugger.IsAttached)
      {
        System.Diagnostics.Debug.WriteLine(s);
      }
      // Allow debug logging in release only if the environment variable is set.
      else if(DebugInRelease.Value)
      {
        Console.WriteLine(s);
      }
    }
  }
}
