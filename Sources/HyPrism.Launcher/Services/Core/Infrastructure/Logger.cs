using Serilog;

namespace HyPrism.Services.Core.Infrastructure;

/// <summary>
/// Provides centralized logging functionality with console output and in-memory buffer.
/// Wraps Serilog for file logging while providing colored console output and recent log retrieval.
/// </summary>
public static class Logger
{
    private static readonly object _lock = new();
    private static readonly Queue<string> _logBuffer = new();
    private const int MaxLogEntries = 100;
    
    /// <summary>
    /// The original stdout TextWriter, captured before Console.Out is replaced by
    /// ElectronLogInterceptor. Logger writes directly to this stream so output is
    /// visible even when Console.IsOutputRedirected is true.
    /// Call <see cref="CaptureOriginalConsole"/> early in Program.Main, before
    /// Console.SetOut is invoked.
    /// </summary>
    private static TextWriter _originalOut = Console.Out;
    
    /// <summary>
    /// Saves a reference to the current Console.Out so that Logger can continue
    /// writing colored output after the stream is replaced.
    /// </summary>
    public static void CaptureOriginalConsole()
    {
        _originalOut = Console.Out;
    }
    
    /// <summary>
    /// Logs an informational message.
    /// </summary>
    /// <param name="category">The log category or source context (e.g., "Download", "Config").</param>
    /// <param name="message">The message to log.</param>
    /// <param name="logToConsole">Whether to also output to console. Defaults to <c>true</c>.</param>
    public static void Info(string category, string message, bool logToConsole = true)
    {
        Log.ForContext("SourceContext", category).Information(message);
        if (logToConsole)
        {
            WriteToConsole("INF", category, message, ConsoleColor.Gray);
            AddToBuffer("INF", category, message);
        }
    }
    
    /// <summary>
    /// Logs a success message (displayed in green in console).
    /// </summary>
    /// <param name="category">The log category or source context.</param>
    /// <param name="message">The success message to log.</param>
    /// <param name="logToConsole">Whether to also output to console. Defaults to <c>true</c>.</param>
    public static void Success(string category, string message, bool logToConsole = true)
    {
        Log.ForContext("SourceContext", category).Information($"SUCCESS: {message}");
        if (logToConsole)
        {
            WriteToConsole("SUC", category, message, ConsoleColor.Green);
            AddToBuffer("SUC", category, message);
        }
    }
    
    /// <summary>
    /// Logs a warning message (displayed in yellow in console).
    /// </summary>
    /// <param name="category">The log category or source context.</param>
    /// <param name="message">The warning message to log.</param>
    /// <param name="logToConsole">Whether to also output to console. Defaults to <c>true</c>.</param>
    public static void Warning(string category, string message, bool logToConsole = true)
    {
        Log.ForContext("SourceContext", category).Warning(message);
        if (logToConsole)
        {
            WriteToConsole("WRN", category, message, ConsoleColor.Yellow);
            AddToBuffer("WRN", category, message);
        }
    }
    
    /// <summary>
    /// Logs an error message (displayed in red in console).
    /// </summary>
    /// <param name="category">The log category or source context.</param>
    /// <param name="message">The error message to log.</param>
    /// <param name="logToConsole">Whether to also output to console. Defaults to <c>true</c>.</param>
    public static void Error(string category, string message, bool logToConsole = true)
    {
        Log.ForContext("SourceContext", category).Error(message);
        if (logToConsole)
        {
            WriteToConsole("ERR", category, message, ConsoleColor.Red);
            AddToBuffer("ERR", category, message);
        }
    }
    
    /// <summary>
    /// Logs a debug message. Only outputs in DEBUG builds.
    /// </summary>
    /// <param name="category">The log category or source context.</param>
    /// <param name="message">The debug message to log.</param>
    public static void Debug(string category, string message)
    {
#if DEBUG
        Log.ForContext("SourceContext", category).Debug(message);
        WriteToConsole("DBG", category, message, ConsoleColor.DarkGray);
        AddToBuffer("DBG", category, message);
#endif
    }
    
    /// <summary>
    /// Retrieves the most recent log entries from the in-memory buffer.
    /// </summary>
    /// <param name="count">The maximum number of entries to retrieve. Defaults to 10.</param>
    /// <returns>A list of formatted log entries, newest last.</returns>
    public static List<string> GetRecentLogs(int count = 10)
    {
        lock (_lock)
        {
            var entries = _logBuffer.ToArray();
            var start = Math.Max(0, entries.Length - count);
            var result = new List<string>();
            for (int i = start; i < entries.Length; i++)
            {
                result.Add(entries[i]);
            }
            return result;
        }
    }
    
    /// <summary>
    /// Writes a formatted log entry to the console with color coding.
    /// </summary>
    /// <param name="level">The log level abbreviation (e.g., "INF", "ERR").</param>
    /// <param name="category">The log category.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="color">The console color for the level indicator.</param>
    private static void WriteToConsole(string level, string category, string message, ConsoleColor color)
    {
        lock (_lock)
        {
            try 
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                
                _originalOut.Write($"{timestamp} ");
                
                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = color;
                _originalOut.Write(level);
                Console.ForegroundColor = originalColor;
                
                _originalOut.WriteLine($" {category}: {message}");
            }
            catch { /* Ignore */ }
        }
    }

    /// <summary>
    /// Adds a log entry to the in-memory circular buffer.
    /// </summary>
    /// <param name="level">The log level abbreviation.</param>
    /// <param name="category">The log category.</param>
    /// <param name="message">The message to buffer.</param>
    private static void AddToBuffer(string level, string category, string message)
    {
        lock (_lock)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"{timestamp} | {level} | {category} | {message}";
            
            _logBuffer.Enqueue(logEntry);
            while (_logBuffer.Count > MaxLogEntries)
            {
                _logBuffer.Dequeue();
            }
        }
    }

    /// <summary>
    /// Displays a progress bar in the console for long-running operations.
    /// Uses carriage return to update in place.
    /// </summary>
    /// <param name="category">The operation category label.</param>
    /// <param name="percent">The progress percentage (0-100).</param>
    /// <param name="message">The status message to display.</param>
    public static void Progress(string category, int percent, string message)
    {
        lock (_lock)
        {
            try {
                _originalOut.Write($"\r[{category}] {message,-40} [{ProgressBar(percent, 20)}] {percent,3}%");
                if (percent >= 100)
                {
                    _originalOut.WriteLine();
                }
            }
            catch { /* Ignore */ }
        }
    }

    
    /// <summary>
    /// Generates an ASCII progress bar string.
    /// </summary>
    /// <param name="percent">The progress percentage (0-100).</param>
    /// <param name="width">The width of the progress bar in characters.</param>
    /// <returns>A string representing the progress bar (e.g., "====------").</returns>
    private static string ProgressBar(int percent, int width)
    {
        int filled = (int)(percent / 100.0 * width);
        int empty = width - filled;
        return new string('=', filled) + new string('-', empty);
    }
}
