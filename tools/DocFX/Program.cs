/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Threading.Tasks;

using DocProcessor.Processors;


if (args.Length == 0)
{
  Console.WriteLine("Usage: DocProcessor <command> [args]");
  Console.WriteLine("Commands:");
  Console.WriteLine("  reorder -SourceRoot <path> -YamlDirectory <path>");
  Console.WriteLine("  remove-inherited -YamlDirectory <path>");
  Console.WriteLine("  reclassify-events -YamlDirectory <path>");
  Console.WriteLine("  organize-toc -SourceRoot <path> -TocPath <path>");
  return 1;
}

string command = args[0];
try
{
  switch (command)
  {
    case "reorder":
      return await MemberReorderer.Execute(args);
    case "remove-inherited":
      return await InheritedMemberRemover.Execute(args);
    case "reclassify-events":
      return await EventReclassifier.Execute(args);
    case "organize-toc":
      return await TocOrganizer.Execute(args);
    default:
      Console.WriteLine($"Unknown command: {command}");
      return 1;
  }
}
catch (Exception ex)
{
  Console.Error.WriteLine($"Error executing {command}: {ex.Message}");
  Console.Error.WriteLine(ex.StackTrace);
  return 1;
}
