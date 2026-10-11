namespace Microsoft.Extensions.Logging;
public interface ILogger { }
public static class LoggingExtensions
{
    public static void LogWarning(this ILogger logger, Exception exception, string message) { }
}
