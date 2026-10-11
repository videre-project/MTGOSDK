/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play;

// WotC.MtGO.Client.Model.Collection.PlayFormatType;
[Flags]
public enum PlayFormatType
{
  //#pragma import enum WotC.MtGO.Client.Model.Collection.PlayFormatType begin
  Null = 0,
  Constructed = 1,
  Sealed = 2,
  Draft = 4,
  //#pragma import enum WotC.MtGO.Client.Model.Collection.PlayFormatType end
}
