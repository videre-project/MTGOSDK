using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;

using ScubaDiver.API;

using RemoteNET.Internal.Extensions;


namespace RemoteNET.Internal
{
  public enum DiverState
  {
    NoDiver,
    Alive,
    Corpse,
    HollowSnapshot
  }
  public static class DiverDiscovery
  {
    public static DiverState QueryStatus(
      Process target,
      string diverAddr,
      ushort diverPort)
    {
      DiverCommunicator com = new DiverCommunicator(diverAddr, diverPort);

      // We WANT to check liveness of the diver using HTTP but this might take a
      // LOT of time if it is dead (Trying to TCP SYN several times, with a
      // timeout between each). So a simple circuit-breaker is implemented
      // before that: If we manage to bind to the expected diver endpoint, we
      // assume it's not alive

      bool diverPortIsFree = false;
      try
      {
        IPAddress localAddr = IPAddress.Parse(diverAddr);
        TcpListener server = new TcpListener(localAddr, diverPort);
        server.Start();
        diverPortIsFree = true;
        server.Stop();
      }
      catch
      {
        // Had some issues, perhapse it's the diver holding that port.
      }

      if (!diverPortIsFree && com.CheckAliveness())
      {
        return DiverState.Alive;
      }

      // Diver isn't alive. It's possible that it was never injected or it was
      // injected and killed
      bool containsToolkitDll = false;
      try
      {
        containsToolkitDll |= target.Modules.AsEnumerable()
          .Any(module => module.ModuleName.Contains("Bootstrapper"));
      }
      catch
      {
        // Sometimes this happens because of x32 vs x64 process interaction
      }
      if (containsToolkitDll)
      {
        // Check if the this is a snapshot created by the diver.
        if (target.Threads.Count == 0)
          return DiverState.HollowSnapshot;

        return DiverState.Corpse;
      }

      return DiverState.NoDiver;
    }
  }
}
