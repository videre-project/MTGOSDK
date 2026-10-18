/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Text.Json.Serialization;


namespace MTGOSDK.API.Interface.Controls;

/// <summary>
/// The prompt-box collection containing a client-local button.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PromptButtonGroup>))]
public enum PromptButtonGroup
{
  Standard,
  Mana
}
