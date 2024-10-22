/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;
using System.Deployment.Application;


namespace Launcher;

public class Program
{
  public static void Main(string[] args)
  {
    try
    {
      string manifestUri = args[0];
      InPlaceHostingManager iphm = new(new Uri(manifestUri), false);
      AutoResetEvent waitHandle = new(false);

      // Download the deployment manifest.
      iphm.GetManifestCompleted += (sender, e) =>
      {
        if (e.Error != null)
          throw new Exception(
              "Could not download manifest. Error: " + e.Error.Message);

        waitHandle.Set();
      };
      iphm.GetManifestAsync();
      waitHandle.WaitOne();

      // Verify and grant permissions specified in the application manifest.
      iphm.AssertApplicationRequirements(true);

      // Download the deployment manifest.
      iphm.DownloadApplicationCompleted += (sender, e) =>
      {
        if (e.Error != null)
          throw new Exception(
              "Could not download application. Error: " + e.Error.Message);

        waitHandle.Set();
      };
      iphm.DownloadApplicationAsync();
      waitHandle.WaitOne();

      Environment.Exit(0);
    }
    catch (Exception ex)
    {
      // Send the exception message to STDERR
      Console.Error.WriteLine(ex.Message);
      Environment.Exit(1);
    }
  }
}
