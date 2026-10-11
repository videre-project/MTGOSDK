/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Chat;

// WotC.MtGO.Client.Model.Chat.ChatChannelType;
public enum ChannelType
{
  //#pragma import enum WotC.MtGO.Client.Model.Chat.ChatChannelType begin
  System = 0,
  Private = 1,
  UserCreated = 2,
  GameChat = 3,
  GameLog = 4,
  Alert = 5,
  PredefinedRoom = 6,
  Clan = 7,
  //#pragma import enum WotC.MtGO.Client.Model.Chat.ChatChannelType end
}
