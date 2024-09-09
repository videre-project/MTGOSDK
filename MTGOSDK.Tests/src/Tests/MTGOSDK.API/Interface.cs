/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using MTGOSDK.API.Interface;
using MTGOSDK.API.Interface.Windows;
using MTGOSDK.API.Interface.ViewModels;
using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Logging;


namespace MTGOSDK.Tests.MTGOSDK_API;

public class Interface : InterfaceValidationFixture
{
  [Test]
  public void Test_WindowManager()
  {
    NotificationService.ShowToast(
      "Test1 - DefaultView",
      "This is a test toast.\nClick to jump to the main window.",
      persistent: true
    );

    // // Create a new non-blocking thread to display a dialog
    // _ = Task.Run(() =>
    // {
    //   DialogService.ShowModal(
    //     "Test Dialog 1",
    //     "This is a test dialog.",
    //     "OK",
    //     "Cancel"
    //   );
    // });

    System.Threading.Thread.Sleep(3_000);

    // IList<GenericWindow> windows = DialogService.RegisteredWindows[0];
    dynamic windows = DialogService.foo[0];
    Assert.That(windows, Is.Not.Null);
    Assert.That(windows.Count, Is.GreaterThan(0));
    Log.Trace($"Found {windows.Count} windows.");

    GenericWindow genericWindow = new(windows[0]);
    Log.Trace($"--> (0): {genericWindow.GetType().Name}");
    Log.Trace($"--> (0): {genericWindow.Foo}");
    Log.Trace($"--> (0): {genericWindow.IsActive}");
    // Log.Trace($"--> (1): {windows[0].IsActive}");

    int i = 0;
    foreach (dynamic collection in WindowUtilities.GetWindowCollections())
    {
      Log.Trace($"Collection {i++}");
      foreach (dynamic window in collection)
      {
        string type = window.GetType().Name;
        Log.Trace($"--> {type}");
        Log.Trace($"--> {window.get_IsActive()}");
        Log.Trace($"--> {window.GetHashCode()}");
        if (type == "BaseDialog")
          window.m_closable.DialogResult = true;
        else if (type == "ToastView")
          window.Close();

        // Log.Trace($"--> {window.DataContext.Height} x {window.DataContext.Width}");
      }
    }
    // foreach (dynamic window in WindowUtilities.GetWindowCollections())
    // {
    //   Log.Trace($"--> {window.GetType().Name}");
    //   Log.Trace($"--> {window.GetHashCode()}, {genericWindow.GetHashCode()}");
    // }

    // GenericWindow wrapper = new(window);
    // Log.Trace($"--> {wrapper.Height} x {wrapper.Width}");

    // dynamic collection = Unbind(WindowService.RegisteredWindows);
    // dynamic windows = collection[0];
    // Assert.That(windows, Is.Not.Null);
    // Assert.That(windows.Count, Is.GreaterThan(0));
    // dynamic mainWindow = windows[0];
    // Log.Trace($"Found {mainWindow.GetType().Name} window.");
    // Log.Trace($"--> {mainWindow.IsInView}");
    // Log.Trace($"--> Dispatcher access: {mainWindow.Dispatcher.CheckAccess()}");

    // Assert.That(mainWindow, Is.Not.Null);
    // Assert.That(mainWindow.IsActive, Is.True);
    // Assert.That(mainWindow.Height, Is.GreaterThan(0.0));
    // windows[0].MinHeight = 0.0;
    // windows[0].MinWidth = 0.0;
  }

  // // [Test, CancelAfter(/* 30 seconds */ 30_000)]
  // public async Task Test_DialogService()
  // {
  //   // Show a GenericDialogViewModel modal dialog.
  //   bool result1 = await WaitUntil(() => DialogService.ShowModal(
  //     "Test Dialog 1",
  //     "This is a test dialog.",
  //     "OK",
  //     "Cancel"
  //   ));
  //   Assert.That(result1, Is.True);

  //   using var dialog = new GenericDialogViewModel(
  //     title: "Test Dialog 2",
  //     text: "This is a test dialog.",
  //     okButton: "OK",
  //     cancelButton: "Cancel"
  //   );
  //   bool result2 = await WaitUntil(() => DialogService.ShowModal(dialog));
  //   Assert.That(result2, Is.True);

  //   // Show a GenericListDialogViewModel modal dialog.
  //   using var listDialog = new GenericListDialogViewModel(
  //     title: "Test Dialog 3",
  //     text: "This is a test dialog.",
  //     listText: "This is a list of items.\n\nItem 1\nItem 2\nItem 3\nItem 4\nItem 5\nItem 6\nItem 7\nItem 8\nItem 9\nItem 10\nItem 11\nItem 12\nItem 13\nItem 14\nItem 15\nItem 16\nItem 17\nItem 18\nItem 19\nItem 20",
  //     okButton: "OK",
  //     cancelButton: "Cancel"
  //   );

  //   bool result3 = await WaitUntil(() => DialogService.ShowModal(listDialog));
  //   Assert.That(result3, Is.True);
  // }

  // // [Test]
  // public void Test_ToastViewManager()
  // {
  //   Assert.DoesNotThrow(() =>
  //     ToastViewManager.ShowToast(
  //       $"Test1 - DefaultView",
  //       "This is a test toast.\nClick to jump to the main window."
  //   ));

  //   var playerEvent = GetEvent<Tournament>((e) => e.StartTime > DateTime.Now);
  //   Assert.DoesNotThrow(() =>
  //     ToastViewManager.ShowToast(
  //       $"Test2 - {playerEvent}",
  //       "This is a test toast.\nClick to view the event.",
  //       playerEvent
  //   ));
  // }

  // [Test]
  // public void Test_WindowUtilities()
  // {

  // }
}

public class InterfaceValidationFixture : EventValidationFixture
{ }
