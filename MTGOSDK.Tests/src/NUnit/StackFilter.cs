/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;


namespace MTGOSDK.NUnit;

public class StackFilter(IEnumerable<string> filterPatterns)
{
  private readonly IEnumerable<Regex> _regexPatterns =
    filterPatterns.Select(p => new Regex(p, RegexOptions.IgnoreCase));

  public string? Filter(string? rawTrace)
  {
    if (rawTrace is null) return null;

    StringReader sr = new(rawTrace);
    StringWriter sw = new();

    // Filter out all lines that match any of our filter patterns.
    string? line;
    while ((line = sr.ReadLine()) != null)
    {
      if (!_regexPatterns.Any(regex => regex.IsMatch(line)))
      {
        sw.WriteLine(line);
      }
    }

    return sw.ToString();
  }
}
