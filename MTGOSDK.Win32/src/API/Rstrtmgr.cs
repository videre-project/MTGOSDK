/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Runtime.InteropServices;
using System.Text;


namespace MTGOSDK.Win32.API;

/// <summary>
/// Native API definitions for the Restart Manager.
/// </summary>
public static class Rstrtmgr
{
  [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
  public static extern int RmStartSession(
      out uint pSessionHandle,
      int dwSessionFlags,
      string strSessionKey);

  [DllImport("rstrtmgr.dll")]
  public static extern int RmEndSession(uint dwSessionHandle);

  [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
  public static extern int RmRegisterResources(
      uint dwSessionHandle,
      uint nFiles,
      string[] rgsFileNames,
      uint nApplications,
      [In] RM_UNIQUE_PROCESS[] rgApplications,
      uint nServices,
      string[] rgsServiceNames);

  [DllImport("rstrtmgr.dll")]
  public static extern int RmGetList(
      uint dwSessionHandle,
      out uint pnProcInfoNeeded,
      ref uint pnProcInfo,
      [In, Out] RM_PROCESS_INFO[] rgAffectedApps,
      out uint lpdwRebootReasons);

  [StructLayout(LayoutKind.Sequential)]
  public struct RM_UNIQUE_PROCESS
  {
    public int dwProcessId;
    public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
  }

  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct RM_PROCESS_INFO
  {
    public RM_UNIQUE_PROCESS Process;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string strAppName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string strServiceShortName;
    public RM_APP_TYPE ApplicationType;
    public uint AppStatus;
    public uint TSSessionId;
    [MarshalAs(UnmanagedType.Bool)]
    public bool bRestartable;
  }

  public enum RM_APP_TYPE
  {
    RmUnknownApp = 0,
    RmMainWindow = 1,
    RmOtherWindow = 2,
    RmService = 3,
    RmExplorer = 4,
    RmConsole = 5,
    RmCritical = 1000
  }
}
