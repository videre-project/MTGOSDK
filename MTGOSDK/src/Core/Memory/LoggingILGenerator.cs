/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;
using System.Reflection.Emit;
using System.Text;

using MTGOSDK.Core.Logging;


namespace MTGOSDK.Core.Memory;

/// <summary>
/// Wrapper around ILGenerator that logs all emitted IL instructions
/// for debugging purposes.
/// </summary>
internal sealed class LoggingILGenerator
{
  private readonly ILGenerator _il;
  private readonly StringBuilder _log = new();
  private int _instructionCount = 0;

  public LoggingILGenerator(ILGenerator il)
  {
    _il = il;
  }

  /// <summary>
  /// Gets the accumulated IL log as a string.
  /// </summary>
  public string GetLog() => _log.ToString();

  /// <summary>
  /// Writes all logged IL instructions to the logger.
  /// </summary>
  public void FlushToLog()
  {
    Log.Debug("[ObjectPinner] Generated IL instructions:\n" + _log.ToString());
  }

  public LocalBuilder DeclareLocal(Type localType, bool pinned = false)
  {
    var local = _il.DeclareLocal(localType, pinned);
    _log.AppendLine($"  .locals [{local.LocalIndex}] {(pinned ? "pinned " : "")}{localType.Name}");
    return local;
  }

  public Label DefineLabel()
  {
    return _il.DefineLabel();
  }

  public void MarkLabel(Label loc)
  {
    _il.MarkLabel(loc);
    _log.AppendLine($"IL_{loc.GetHashCode():X4}:");
  }

  public void Emit(OpCode opcode)
  {
    _il.Emit(opcode);
    LogInstruction(opcode);
  }

  public void Emit(OpCode opcode, byte arg)
  {
    _il.Emit(opcode, arg);
    LogInstruction(opcode, arg.ToString());
  }

  public void Emit(OpCode opcode, int arg)
  {
    _il.Emit(opcode, arg);
    LogInstruction(opcode, arg.ToString());
  }

  public void Emit(OpCode opcode, Label label)
  {
    _il.Emit(opcode, label);
    LogInstruction(opcode, $"IL_{label.GetHashCode():X4}");
  }

  public void Emit(OpCode opcode, Label[] labels)
  {
    _il.Emit(opcode, labels);
    var labelStrs = string.Join(", ", labels.Select(l => $"IL_{l.GetHashCode():X4}"));
    LogInstruction(opcode, $"({labelStrs})");
  }

  public void Emit(OpCode opcode, LocalBuilder local)
  {
    _il.Emit(opcode, local);
    LogInstruction(opcode, $"V_{local.LocalIndex}");
  }

  public void Emit(OpCode opcode, MethodInfo method)
  {
    _il.Emit(opcode, method);
    LogInstruction(opcode, $"{method.DeclaringType?.Name}.{method.Name}");
  }

  public void Emit(OpCode opcode, FieldInfo field)
  {
    _il.Emit(opcode, field);
    LogInstruction(opcode, $"{field.DeclaringType?.Name}.{field.Name}");
  }

  public void Emit(OpCode opcode, Type type)
  {
    _il.Emit(opcode, type);
    LogInstruction(opcode, type.Name);
  }

  public void Emit(OpCode opcode, string str)
  {
    _il.Emit(opcode, str);
    LogInstruction(opcode, $"\"{str}\"");
  }

  private void LogInstruction(OpCode opcode, string? operand = null)
  {
    _instructionCount++;
    var line = $"  {_instructionCount:D4}: {opcode.Name}";
    if (operand != null)
    {
      line += $" {operand}";
    }
    _log.AppendLine(line);
  }
}
