/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play;

public enum QueueState
{
  //#pragma import enum WotC.MtGO.Client.Model.Play.Enums.QueueState begin
  NotSet = 0,
  NotJoined = 1,
  JoinRequested = 2,
  Joined = 3,
  RemoveRequested = 4,
  Closed = 5,
  //#pragma import enum WotC.MtGO.Client.Model.Play.Enums.QueueState end
}
