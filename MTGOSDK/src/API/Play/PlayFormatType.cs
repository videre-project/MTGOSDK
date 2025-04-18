/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play;

// WotC.MtGO.Client.Model.Collection.PlayFormatType;
[Flags]
public enum PlayFormatType
{
  Null        = 0,
  Constructed = 1,
  Sealed      = 2,
  Draft       = 4,
}
