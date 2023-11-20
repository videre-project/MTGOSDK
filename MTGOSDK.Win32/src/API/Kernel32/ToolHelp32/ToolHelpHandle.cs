/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using Microsoft.Win32.SafeHandles;


namespace MTGOSDK.Win32.API;

public class ToolHelpHandle : SafeHandleZeroOrMinusOneIsInvalid
{
  private ToolHelpHandle() : base(true) { }

  protected override bool ReleaseHandle()
  {
    return Kernel32.CloseHandle(this.handle);
  }
}
