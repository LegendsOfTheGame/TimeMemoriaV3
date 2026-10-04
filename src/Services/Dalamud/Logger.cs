namespace TimeMemoria.Services;

public interface ILogger
{
  void Chat(string uncolored = "", string pre = "", string italic = "", string post = "", string name = "", XivChatType type = XivChatType.Debug, bool addPrefix = true, ushort preColor = 2, ushort italicColor = 2, ushort postColor = 2);

  void Error(string text,
      [CallerFilePath] string callerPath = "",
      [CallerMemberName] string callerName = "",
      [CallerLineNumber] int lineNumber = -1);

  void Error(Exception ex,
      [CallerFilePath] string callerPath = "",
      [CallerMemberName] string callerName = "",
      [CallerLineNumber] int lineNumber = -1);

  void Debug(string text,
      [CallerFilePath] string callerPath = "",
      [CallerMemberName] string callerName = "",
      [CallerLineNumber] int lineNumber = -1);

  void DebugObj<T>(T obj,
      [CallerFilePath] string callerPath = "",
      [CallerMemberName] string callerName = "",
      [CallerLineNumber] int lineNumber = -1);

  Task ServiceLifecycle(string? status = null,
      [CallerFilePath] string callerPath = "",
      [CallerMemberName] string callerName = "",
      [CallerLineNumber] int lineNumber = -1);
}

/// <summary>
/// No toast or notification methods, on purpose. The plugin never shows either
/// (no toasts, overlays or alerts is part of its scope), so they were removed
/// rather than left unused.
/// </summary>
public class Logger(IPluginLog _pluginLog, IChatGui _chatGui) : ILogger
{
  public void Chat(string uncolored = "", string pre = "", string italic = "", string post = "", string name = "", XivChatType type = XivChatType.Debug, bool addPrefix = true, ushort preColor = 2, ushort italicColor = 2, ushort postColor = 2)
  {
    XivChatEntry chatMessage = new()
    {
      Type = type,
      Name = new SeStringBuilder().AddText(name).Build(),
      Message = new SeStringBuilder()
        .AddUiForeground(addPrefix ? "[TimeMemoria] " : "", 35)
        .AddText(uncolored)
        .AddUiForeground(pre, preColor)
        .AddItalicsOn()
        .AddUiForeground(italic, italicColor)
        .AddItalicsOff()
        .AddUiForeground(post, postColor)
        .Build(),
    };
    _chatGui.Print(chatMessage);
    Debug($"Printed chatMessage::'{chatMessage.Message}'");
  }

  private string FormatCallsite(string callerPath = "", string callerName = "", int lineNumber = -1) =>
    $"[{Path.GetFileName(callerPath)}:{callerName}:{lineNumber}]";

  public void Error(string text, [CallerFilePath] string callerPath = "", [CallerMemberName] string callerName = "", [CallerLineNumber] int lineNumber = -1)
  {
    string logEntry = $"{FormatCallsite(callerPath, callerName, lineNumber)} {text}";
    _pluginLog.Error(logEntry);
  }

  public void Error(Exception ex, [CallerFilePath] string callerPath = "", [CallerMemberName] string callerName = "", [CallerLineNumber] int lineNumber = -1)
  {
    string logEntry = $"{FormatCallsite(callerPath, callerName, lineNumber)} Exception: {ex}";
    _pluginLog.Error(logEntry);
  }

  public void Debug(string text, [CallerFilePath] string callerPath = "", [CallerMemberName] string callerName = "", [CallerLineNumber] int lineNumber = -1)
  {
    string logEntry = $"{FormatCallsite(callerPath, callerName, lineNumber)} {text}";
    _pluginLog.Debug(logEntry);
  }

  public void DebugObj<T>(T obj, [CallerFilePath] string callerPath = "", [CallerMemberName] string callerName = "", [CallerLineNumber] int lineNumber = -1)
  {
    if (obj == null)
    {
      Debug("null", callerPath, callerName, lineNumber);
      return;
    }

    Type type = typeof(T);
    StringBuilder sb = new();
    sb.AppendLine($"Type: {type.Name}");

    PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
    foreach (PropertyInfo prop in properties)
    {
      object? value = prop.GetValue(obj);
      sb.AppendLine($"  {prop.Name}: {value ?? null}");
    }

    FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
    foreach (FieldInfo field in fields)
    {
      object? value = field.GetValue(obj);
      sb.AppendLine($"  {field.Name}: {value ?? null}");
    }

    if (properties.Length == 0 && fields.Length == 0)
      sb.AppendLine("  No public properties or fields found.");

    Debug(sb.ToString(), callerPath, callerName, lineNumber);
  }

  public Task ServiceLifecycle(string? status = null, [CallerFilePath] string callerPath = "", [CallerMemberName] string callerName = "", [CallerLineNumber] int lineNumber = -1)
  {
    string lifecycleStage = status ??
      (callerName.Contains("Start")
      ? "started" : callerName.Contains("Stop")
      ? "stopped" : "changed");

    string className = new StackTrace()
      .GetFrame(1)
      ?.GetMethod()
      ?.DeclaringType
      ?.Name ?? "UnknownClass";

    Debug($"{className} {lifecycleStage}", callerPath, callerName, lineNumber);

    return Task.CompletedTask;
  }
}
