using System;
using System.Threading;


namespace ScubaDiver;

public class DllEntry
{
  public static void DiverHost(object pwzArgument)
  {
    try
    {
      Diver _instance = new();
      ushort port = ushort.Parse((string)pwzArgument);
      _instance.Start(port);

      // Diver killed (politely)
      Logger.Debug("[DiverHost] Diver finished gracefully, returning");
    }
    catch (Exception e)
    {
      Logger.Debug("[DiverHost] ScubaDiver crashed.");
      Logger.Debug(e.ToString());
      Logger.Debug("[DiverHost] Exiting entry point in 60 secs...");
      Thread.Sleep(TimeSpan.FromSeconds(60));
    }
  }

  public static int EntryPoint(string pwzArgument)
  {
    //
    // The Bootstrapper needs to call a C# function with exactly this signature,
    // so we use it to just create a diver, and run the Start func (blocking)
    //
    ParameterizedThreadStart func = DiverHost;
    Thread diverHostThread = new(func);
    diverHostThread.Start(pwzArgument);

    Logger.Debug("[EntryPoint] Returning");
    return 0;
  }
}
