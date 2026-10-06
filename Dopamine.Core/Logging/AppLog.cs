using Digimezzo.Foundation.Core.Logging;
using Dopamine.Core.Settings;
using System;
using System.Runtime.CompilerServices;

namespace Dopamine.Core.Logging
{
    public static class AppLog
    {
        public static void Info(
            string message,
            object param1 = null,
            object param2 = null,
            object param3 = null,
            object param4 = null,
            object param5 = null,
            object param6 = null,
            object param7 = null,
            object param8 = null,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            Write(() => LogClient.Info(message, param1, param2, param3, param4, param5, param6, param7, param8, filePath, memberName, lineNumber));
        }

        public static void Warning(
            string message,
            object param1 = null,
            object param2 = null,
            object param3 = null,
            object param4 = null,
            object param5 = null,
            object param6 = null,
            object param7 = null,
            object param8 = null,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            Write(() => LogClient.Warning(message, param1, param2, param3, param4, param5, param6, param7, param8, filePath, memberName, lineNumber));
        }

        public static void Error(
            string message,
            object param1 = null,
            object param2 = null,
            object param3 = null,
            object param4 = null,
            object param5 = null,
            object param6 = null,
            object param7 = null,
            object param8 = null,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            Write(() => LogClient.Error(message, param1, param2, param3, param4, param5, param6, param7, param8, filePath, memberName, lineNumber));
        }

        // Compatibility aliases for callers compiled against earlier Dopamine.Core versions.
        // "Always" must never bypass the user's logging preference.
        public static void WarningAlways(
            string message,
            object param1 = null,
            object param2 = null,
            object param3 = null,
            object param4 = null,
            object param5 = null,
            object param6 = null,
            object param7 = null,
            object param8 = null,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            Warning(message, param1, param2, param3, param4, param5, param6, param7, param8, memberName, filePath, lineNumber);
        }

        public static void ErrorAlways(
            string message,
            object param1 = null,
            object param2 = null,
            object param3 = null,
            object param4 = null,
            object param5 = null,
            object param6 = null,
            object param7 = null,
            object param8 = null,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            Error(message, param1, param2, param3, param4, param5, param6, param7, param8, memberName, filePath, lineNumber);
        }

        public static void InfoAlways(
            string message,
            object param1 = null,
            object param2 = null,
            object param3 = null,
            object param4 = null,
            object param5 = null,
            object param6 = null,
            object param7 = null,
            object param8 = null,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            Info(message, param1, param2, param3, param4, param5, param6, param7, param8, memberName, filePath, lineNumber);
        }

        private static void Write(Action writeAction)
        {
            try
            {
                // Keep the gate in one place, before the file logger is initialized or queued.
                // This also applies to diagnostics and unhandled-exception logging.
                if (!LoggingSettings.IsEnabled())
                {
                    return;
                }

                writeAction();
            }
            catch (Exception)
            {
            }
        }
    }
}
