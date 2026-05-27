/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Globalization;
using System.Text.Json;

using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;
using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.Core.Reflection.Serialization;

internal static class RemoteBatchCollection
{
  public static bool TryFetch(
    object? collection,
    IEnumerable<string> paths,
    out BatchCollectionSnapshot snapshot,
    int maxItems = 0)
  {
    snapshot = null!;

    if (collection is not DynamicRemoteObject dro)
    {
      return false;
    }

    var pathList = paths as string[] ?? paths.ToArray();
    if (pathList.Length == 0)
    {
      return false;
    }

    BatchCollectionResponse response;
    try
    {
      response = RemoteClient.@client.Communicator.GetBatchCollectionMembers(
        dro.__ro.RemoteToken,
        dro.__type?.FullName ?? "Unknown",
        string.Join("|", pathList),
        maxItems);
    }
    catch
    {
      return false;
    }

    if (response?.Schema == null ||
        response.Columns == null ||
        response.Count <= 0)
    {
      return false;
    }

    snapshot = new(response);
    return true;
  }
}

internal sealed class BatchCollectionSnapshot
{
  private readonly BatchCollectionResponse m_response;
  private readonly Dictionary<string, int> m_columns;

  public int Count => m_response.Count;

  public BatchCollectionSnapshot(BatchCollectionResponse response)
  {
    m_response = response;
    m_columns = new(StringComparer.Ordinal);
    for (int i = 0; i < response.Schema.Length; i++)
    {
      m_columns[response.Schema[i]] = i;
    }
  }

  public bool HasColumn(string path) => m_columns.ContainsKey(path);

  public bool HasColumns(IEnumerable<string> paths) =>
    paths.All(HasColumn);

  public bool ColumnHasAnyValue(string path) =>
    TryGetColumn(path, out int column) &&
    m_response.Columns[column].Any(value => value != null);

  public int GetInt(int row, string path, int fallback = 0)
  {
    object? value = Decode(row, path);
    return value switch
    {
      int intValue => intValue,
      long longValue => (int)longValue,
      string stringValue when int.TryParse(
        stringValue,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out int parsed) => parsed,
      _ => fallback
    };
  }

  public string GetString(
    int row,
    string path,
    string fallback = "") =>
    Decode(row, path)?.ToString() ?? fallback;

  public IReadOnlyList<int> GetIntArray(int row, string path)
  {
    if (!TryGetColumn(path, out int column))
    {
      return [];
    }

    string? json = m_response.Columns[column][row];
    if (string.IsNullOrWhiteSpace(json))
    {
      return [];
    }

    try
    {
      using var document = JsonDocument.Parse(json);
      if (document.RootElement.ValueKind != JsonValueKind.Array)
      {
        return [];
      }

      var values = new List<int>();
      foreach (var element in document.RootElement.EnumerateArray())
      {
        values.Add(ReadJsonInt(element));
      }

      return values;
    }
    catch
    {
      return [];
    }
  }

  public IReadOnlyList<string> GetStringArray(int row, string path)
  {
    if (!TryGetColumn(path, out int column))
    {
      return [];
    }

    string? json = m_response.Columns[column][row];
    if (string.IsNullOrWhiteSpace(json))
    {
      return [];
    }

    try
    {
      using var document = JsonDocument.Parse(json);
      if (document.RootElement.ValueKind != JsonValueKind.Array)
      {
        return [];
      }

      var values = new List<string>();
      foreach (var element in document.RootElement.EnumerateArray())
      {
        string value = element.ValueKind switch
        {
          JsonValueKind.String => element.GetString() ?? "",
          JsonValueKind.Number => element.GetRawText(),
          JsonValueKind.True => bool.TrueString,
          JsonValueKind.False => bool.FalseString,
          _ => ""
        };

        if (!string.IsNullOrEmpty(value))
        {
          values.Add(value);
        }
      }

      return values;
    }
    catch
    {
      return [];
    }
  }

  public static int GetAt(
    IReadOnlyList<int> values,
    int index,
    int fallback = 0) =>
    index < values.Count ? values[index] : fallback;

  private object? Decode(int row, string path)
  {
    if (!TryGetColumn(path, out int column))
    {
      return null;
    }

    string? encoded = m_response.Columns[column][row];
    if (encoded == null)
    {
      return null;
    }

    try
    {
      return PrimitivesEncoder.Decode(encoded, m_response.SchemaTypes[column]);
    }
    catch
    {
      return encoded.Trim('"');
    }
  }

  private bool TryGetColumn(string path, out int column) =>
    m_columns.TryGetValue(path, out column);

  private static int ReadJsonInt(JsonElement element)
  {
    return element.ValueKind switch
    {
      JsonValueKind.Number when element.TryGetInt32(out int value) => value,
      JsonValueKind.String when int.TryParse(
        element.GetString(),
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out int value) => value,
      JsonValueKind.True => 1,
      _ => 0
    };
  }
}
