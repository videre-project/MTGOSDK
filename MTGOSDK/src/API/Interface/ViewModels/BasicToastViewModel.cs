/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play;

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
  /// The main shell view currently displayed on the primary MTGO window.
  /// </summary>
  private static IToastRelatedView MainRelatedView =>
    ObjectProvider.Get<IShellViewModel>().MainRelatedView;

  /// <summary>
  /// Creates a new remote instance of the BasicToastViewModel class.
  /// </summary>
  private static BasicToastViewModel NewInstance(params dynamic[] args) =>
    new(RemoteClient.CreateInstance(
      "Shiny.Toast.ViewModels.BasicToastViewModel",
      Unbind(args)
    ));

  /// <summary>
  /// Creates a new remote instance of the BasicToastViewModel class.
  /// </summary>
  public BasicToastViewModel(
    string title,
    string text,
    IToastRelatedView? relatedView = null,
    bool showForever = false)
      : this(NewInstance(text, relatedView ?? MainRelatedView, title, showForever))
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

  /// <summary>
  /// Sets the navigate to view command for the toast.
  /// </summary>
  /// <param name="playerEvent">The player event to navigate to.</param>
  public void SetNavigateToViewCommand(Event playerEvent) =>
    @base.SetNavigateToViewCommand(Unbind(playerEvent));

  public void Dispose() => @base.Dispose();
}
