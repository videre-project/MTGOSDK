/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;

using Shiny.Core;
using Shiny.Core.Interfaces;


namespace MTGOSDK.API.Interface.ViewModels;

public sealed class BasicToastViewModel(dynamic basicToastViewModel)
    : DLRWrapper<IBasicDialogViewModelBase>, IDisposable
{
  /// <summary>
  /// Stores an internal reference to the BasicToastViewModel object.
  /// </summary>
  internal override dynamic obj => basicToastViewModel;

  /// <summary>
  /// Creates a new remote instance of the BasicToastViewModel class.
  /// </summary>
  private static BasicToastViewModel NewInstance(params dynamic[] args) =>
    new(RemoteClient.CreateInstance(
      "Shiny.Toast.ViewModels.BasicToastViewModel",
      Unbind(args)
    ));

  public BasicToastViewModel(
    string title,
    string text,
    IToastRelatedView relatedView,
    bool showForever = false)
      : this(NewInstance(text, relatedView, title, showForever))
  { }

  //
  // BasicToastViewModel wrapper properties
  //

  /// <summary>
  /// The title of the toast.
  /// </summary>
  public string Title
  {
    get => @base.m_header;
    set => @base.m_header = value;
  }

  /// <summary>
  /// The message text of the toast.
  /// </summary>
  public string Text
  {
    get => @base.m_message;
    set => @base.m_message = value;
  }

  /// <summary>
  /// Whether the toast should be shown forever.
  /// </summary>
  public bool ShowForever
  {
    get => @base.m_showForever;
    set => @base.m_showForever = value;
  }

  //
  // BasicToastViewModel wrapper methods
  //

  public void Dispose() => @base.Dispose();
}
