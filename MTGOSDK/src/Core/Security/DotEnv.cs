/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.IO;
using System.Security;
using System.Text;
using System.Runtime.CompilerServices;


namespace MTGOSDK.Core.Security;

/// <summary>
/// A wrapper for environment variables for insecure credential storage.
/// </summary>
public static class DotEnv
{
  /// <summary>
  /// The internal dictionary of variables.
  /// </summary>
  private static readonly Dictionary<string, SecureVariable> s_variables = new();

  /// <summary>
  /// Gets the value of the specified variable.
  /// </summary>
  /// <param name="key">The name of the variable to get.</param>
  /// <returns>The value of the variable, if it exists.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown if the variable does not exist.
  /// </exception>
  public static dynamic Get(string key) => s_variables[key];

  /// <summary>
  /// Loads the .env file from the current directory or a given filepath.
  /// </summary>
  /// <param name="filepath">The path to the .env file (optional).</param>
  /// <exception cref="FileNotFoundException">
  /// Thrown if the .env file does not exist or cannot be found.
  /// </exception>
  public static void LoadFile([CallerFilePath] string filepath = null)
  {
    // If the caller path begins with '/_/', it is a relative file path.
    if (filepath.StartsWith("/_/"))
      filepath = Path.Combine(Directory.GetCurrentDirectory(), filepath[3..]);

    // Recursively search for the .env file within each parent directory.
    int maxSearchDepth = 25;
    while (!(File.Exists(filepath) && Path.GetFileName(filepath) == ".env"))
    {
      // If the filepath does not point to an .env file (caller path),
      // search for the .env file in the current directory.
      if (Path.GetFileName(filepath) != ".env")
        filepath = Path.Combine(Path.GetDirectoryName(filepath), @".env");
      // Otherwise, keep searching for the .env file in the parent directory.
      else
        filepath = Path.Combine(Path.GetDirectoryName(filepath), @"..\.env");

      if (Path.GetDirectoryName(filepath) == Path.GetPathRoot(filepath) ||
          maxSearchDepth-- <= 0)
        throw new FileNotFoundException("Could not find .env file.");
    }

    using (StreamReader reader = new(filepath))
    {
      // Temporary buffers for each key and value pair.
      StringBuilder key = new();
      SecureString value = new();

      char c;
      bool inKey = true;
      while(reader.Peek() >= 0)
      {
        c = (char)reader.Read();

        // Skip leading whitespace.
        if (inKey && (c == ' ' && key.Length == 0))
          continue;
        if (!inKey && (c == ' ' && value.Length == 0))
          continue;

        // Skip and reset cursor on newlines.
        if ((c == '\n' || c == '\r') && key.Length > 0)
        {
          string keyStr = key.ToString().Trim();
          if (keyStr.Length > 0)
            s_variables.Add(keyStr, new SecureVariable(value));
          key.Clear();
          value = new();

          c = default;
          inKey = true;
          continue;
        }

        // Handle delimiters.
        if (inKey && (c == '=' || c == ':'))
        {
          if (key[key.Length - 1] == ' ')
            key.Remove(key.Length - 1, 1);

          inKey = false;
          continue;
        }

        // Build the key and value parts.
        if (inKey)
          key.Append(c);
        else
          value.AppendChar(c);
      }
    }
  }
}
