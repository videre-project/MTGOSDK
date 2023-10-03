using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;


namespace ScubaDiver;

public class DllEntry
{
  #region P/Invoke Console Spawning

  [DllImport("kernel32.dll",
    EntryPoint = "GetStdHandle",
    SetLastError = true,
    CharSet = CharSet.Auto,
    CallingConvention = CallingConvention.StdCall)]
  private static extern IntPtr GetStdHandle(int nStdHandle);

  [DllImport("kernel32.dll",
    EntryPoint = "AllocConsole",
    SetLastError = true,
    CharSet = CharSet.Auto,
    CallingConvention = CallingConvention.StdCall)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool AllocConsole();

  private const int STD_OUTPUT_HANDLE = -11;

  #endregion

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

  #region Event Handler reflection

  static Dictionary<Type, List<FieldInfo>> dicEventFieldInfos = new();

  static BindingFlags AllBindings
  {
    get => BindingFlags.IgnoreCase
          | BindingFlags.Public
          | BindingFlags.NonPublic
          | BindingFlags.Instance
          | BindingFlags.Static;
  }

  static void BuildEventFields(Type t, List<FieldInfo> lst)
  {
    foreach (EventInfo ei in t.GetEvents(AllBindings))
    {
      Type dt = ei.DeclaringType;
      FieldInfo fi = dt.GetField(ei.Name, AllBindings);
      if (fi != null)
        lst.Add(fi);
    }
  }

  static List<FieldInfo> GetTypeEventFields(Type t)
  {
    if (dicEventFieldInfos.ContainsKey(t))
      return dicEventFieldInfos[t];

    List<FieldInfo> lst = new();
    BuildEventFields(t, lst);
    dicEventFieldInfos.Add(t, lst);
    return lst;
  }

  static EventHandlerList GetStaticEventHandlerList(Type t, object obj)
  {
      MethodInfo mi = t.GetMethod("get_Events", AllBindings);
      return (EventHandlerList)mi.Invoke(obj, new object[] { });
  }

  public static void RemoveEventHandler(object obj, string EventName = "")
  {
    if (obj == null)
      return;

    Type t = obj.GetType();
    List<FieldInfo> event_fields = GetTypeEventFields(t);
    EventHandlerList static_event_handlers = null;

    foreach (FieldInfo fi in event_fields)
    {
      if (EventName != "" && string.Compare(EventName, fi.Name, true) != 0)
        continue;

      var eventName = fi.Name;

      if (fi.IsStatic)
      {
        // STATIC EVENT
        static_event_handlers ??= GetStaticEventHandlerList(t, obj);

        object idx = fi.GetValue(obj);
        Delegate eh = static_event_handlers[idx];
        if (eh == null)
          continue;

        Delegate[] dels = eh.GetInvocationList();
        if (dels == null)
          continue;

        EventInfo ei = t.GetEvent(eventName, AllBindings);
        foreach (Delegate del in dels)
          ei.RemoveEventHandler(obj, del);
      }
      else
      {
        // INSTANCE EVENT
        EventInfo ei = t.GetEvent(eventName, AllBindings);
        if (ei != null)
        {
          object val = fi.GetValue(obj);
          Delegate mdel = (val as Delegate);
          if (mdel != null)
          {
            foreach (Delegate del in mdel.GetInvocationList())
            {
              ei.RemoveEventHandler(obj, del);
            }
          }
        }
      }
    }
  }

  #endregion

  // Bootstrapper needs to call a C# function with exactly this signature.
  // So we use it to just create a diver, and run the Start func (blocking)
  public static int EntryPoint(string pwzArgument)
  {
    if (Logger.IsDebug && !Debugger.IsAttached)
    {
      // If we need to log and a debugger isn't attached to the target process
      // then we need to allocate a console and redirect STDOUT to it.
      if (AllocConsole())
      {
        IntPtr stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
        SafeFileHandle safeFileHandle = new(stdHandle, true);
        FileStream fileStream = new(safeFileHandle, FileAccess.Write);
        Encoding encoding = Encoding.ASCII;
        StreamWriter standardOutput = new(fileStream, encoding) { AutoFlush = true };
        Console.SetOut(standardOutput);
      }
    }

    ParameterizedThreadStart func = DiverHost;
    Thread diverHostThread = new(func);
    diverHostThread.Start(pwzArgument);

    Logger.Debug("[EntryPoint] Returning");
    return 0;
  }
}
