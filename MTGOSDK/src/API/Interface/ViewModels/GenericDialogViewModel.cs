/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games;
using MTGOSDK.Core;
using MTGOSDK.Core.Reflection;

using Shiny.Core.Interfaces;


namespace MTGOSDK.API.Interface.ViewModels;

public sealed class GenericDialogViewModel(dynamic genericDialogViewModel)
    : DLRWrapper<IBasicDialogViewModelBase>, IDisposable
{
  /// <summary>
  /// Stores an internal reference to the GenericDialogViewModel object.
  /// </summary>
  internal override dynamic obj => genericDialogViewModel;

  /// <summary>
  /// Creates a new remote instance of the GenericDialogViewModel class.
  /// </summary>
  internal static GenericDialogViewModel NewInstance() =>
    new(RemoteClient.CreateInstance("Shiny.ViewModels.GenericDialogViewModel"));

  public GenericDialogViewModel(
    string title,
    string text,
    string? okButton="Ok",
    string? cancelButton="Cancel") : this(NewInstance())
  {
    this.Title = title;
    this.Text = text;
    this.OkButton = okButton;
    this.CancelButton = cancelButton;
  }

  //
  // GenericDialogViewModel wrapper properties
  //

  /// <summary>
  /// The title of the dialog.
  /// </summary>
  public string Title
  {
    get => @base.m_title;
    set => @base.m_title = value;
  }

  /// <summary>
  /// The message text of the dialog.
  /// </summary>
  public string Text
  {
    get => @base.m_text;
    set => @base.m_text = value;
  }

  /// <summary>
  /// The text to display on the OK button.
  /// </summary>
  /// <remarks>
  /// If this property is set to null, the OK button will not be displayed.
  /// </remarks>
  public string? OkButton
  {
    get => @base.m_okayButtonLabel;
    set
    {
      @base.m_showOkButton = value != null;
      @base.m_okayButtonLabel = value;
    }
  }

  /// <summary>
  /// The text to display on the Cancel button.
  /// </summary>
  /// <remarks>
  /// If this property is set to null, the Cancel button will not be displayed.
  /// </remarks>
  public string? CancelButton
  {
    get => @base.m_cancelButtonLabel;
    set
    {
      @base.m_showCancelButton = value != null;
      @base.m_cancelButtonLabel = value;
    }
  }

  //
  // GenericDialogViewModel wrapper methods
  //

  public void Dispose() => @base.Dispose();
}
