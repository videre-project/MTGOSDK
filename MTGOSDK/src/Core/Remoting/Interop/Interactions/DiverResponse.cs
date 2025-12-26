/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions;

/// <summary>
/// Wrapper for all diver responses. Either Error or Data will be populated.
/// </summary>
[MessagePackObject]
public class DiverResponse<T>
{
  [Key(0)]
  public bool IsError { get; set; }

  [Key(1)]
  public string ErrorMessage { get; set; }

  [Key(2)]
  public string ErrorStackTrace { get; set; }

  [Key(3)]
  public T Data { get; set; }

  public static DiverResponse<T> Success(T data) => new() { Data = data };

  public static DiverResponse<T> FromError(string message, string stackTrace = null) =>
    new()
    {
      IsError = true,
      ErrorMessage = message,
      ErrorStackTrace = stackTrace
    };
}
