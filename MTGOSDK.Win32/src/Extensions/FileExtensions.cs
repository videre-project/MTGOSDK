/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;

using MTGOSDK.Win32.API;


namespace MTGOSDK.Win32.Extensions;

public static class FileExtensions
{
  /// <summary>
  /// Gets the processes that are locking the specified file.
  /// </summary>
  /// <param name="file">The file to check.</param>
  /// <returns>A list of processes locking the file.</returns>
  public static List<Process> GetLockingProcesses(this FileInfo file)
  {
    var processes = new List<Process>();
    uint handle;
    string key = Guid.NewGuid().ToString();

    int res = Rstrtmgr.RmStartSession(out handle, 0, key);
    if (res != 0) throw new Exception("Could not start restart manager session.");

    try
    {
      string[] resources = { file.FullName };
      res = Rstrtmgr.RmRegisterResources(handle, (uint)resources.Length, resources, 0, null, 0, null);
      if (res != 0) throw new Exception("Could not register resources.");

      uint pnProcInfoNeeded = 0;
      uint pnProcInfo = 0;
      uint lpdwRebootReasons = 0;

      res = Rstrtmgr.RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, null, out lpdwRebootReasons);

      if (res == 234) // ERROR_MORE_DATA
      {
        pnProcInfo = pnProcInfoNeeded;
        var processInfo = new Rstrtmgr.RM_PROCESS_INFO[pnProcInfo];
        res = Rstrtmgr.RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, out lpdwRebootReasons);

        if (res == 0)
        {
          foreach (var info in processInfo)
          {
            try
            {
              processes.Add(Process.GetProcessById(info.Process.dwProcessId));
            }
            catch (ArgumentException)
            {
              // Process might have exited
            }
          }
        }
      }
    }
    finally
    {
      Rstrtmgr.RmEndSession(handle);
    }

    return processes;
  }
}
