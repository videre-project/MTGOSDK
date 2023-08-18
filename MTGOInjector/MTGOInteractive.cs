/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: BSD-3-Clause
**/

namespace MTGOInjector;

public class MTGOInteractive : BaseClient
{
  private readonly dynamic _dialogService;

  public MTGOInteractive()
  {
    _dialogService = GetInstances("Shiny.Core.DialogManagement.DialogService").Single();
  }

  public bool DialogWindow(string title,
                           string text,
                           string? okButton="Ok",
                           string? cancelButton="Cancel")
  {
    dynamic viewModel = CreateInstance("Shiny.ViewModels.GenericDialogViewModel");
    viewModel.m_title = title;
    viewModel.m_text = text;
    viewModel.m_showOkButton = okButton != null;
    if (okButton != null)
    {
      viewModel.m_okayButtonLabel = okButton;
    }

    viewModel.m_showCancelButton = okButton != null;
    if (cancelButton != null)
    {
      viewModel.m_cancelButtonLabel = cancelButton;
    }

    bool result = _dialogService.ShowModal<dynamic>(viewModel, -1);
    _dialogService.TryDisposeViewModel(viewModel);

    return result;
  }
}
