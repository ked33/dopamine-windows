using Digimezzo.Foundation.Core.Settings;
using Dopamine.Core.Logging;
using Dopamine.Core.Settings;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;

// Runs in a separate process with portable settings in a temporary directory.
// Uses the real Foundation logger and settings writer, not mocked file output.
internal static class LoggingRegression
{
    private static int Main(string[] args)
    {
        try
        {
            string mode = args.Single();
            string applicationFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LoggingRegression");
            string logFolder = Path.Combine(applicationFolder, "Log");
            string logFile = Path.Combine(logFolder, "LoggingRegression.log");

            if (mode == "disabled")
            {
                Require(!LoggingSettings.IsEnabled(), "Startup must restore the disabled preference.");
                WriteAll("disabled");
                Thread.Sleep(400);
                Require(!Directory.Exists(logFolder), "Disabled logging created a log directory or file.");
            }
            else if (mode == "toggle")
            {
                Require(!LoggingSettings.IsEnabled(), "The toggle test must start disabled.");
                LoggingSettings.SetEnabled(true);
                Require(LoggingSettings.IsEnabled(), "Enabling must take effect immediately.");
                WriteAll("enabled");
                Require(SpinWait.SpinUntil(() => ReadLog(logFile).Count(c => c == '\n') >= 6, 5000),
                    "Enabled logging did not flush all six entry points.");
                string enabledLog = ReadLog(logFile);
                foreach (string level in new[] { "Info", "Warning", "Error" })
                {
                    Require(enabledLog.Contains("|" + level + "|LoggingRegression.WriteAll|"),
                        "Missing severity or incorrect caller file/member metadata: " + level);
                }

                LoggingSettings.SetEnabled(false);
                Require(!LoggingSettings.IsEnabled(), "Disabling must take effect immediately.");
                AssertPersistedDisabled();
                // Later saves by another setting must preserve the logging preference.
                SettingsClient.Set("Appearance", "OtherSetting", true);
                SettingsClient.Write();
                AssertPersistedDisabled();
                WriteAll("disabled-after-toggle");
                Thread.Sleep(400);
                Require(ReadLog(logFile) == enabledLog, "Disabled logging appended new entries.");
            }
            else if (mode == "restart")
            {
                Require(!LoggingSettings.IsEnabled(), "A fresh process lost the disabled preference.");
                string previousLog = ReadLog(logFile);
                Require(previousLog.Contains("enabled-info"), "The restart test must retain the existing log.");
                WriteAll("disabled-after-restart");
                Thread.Sleep(400);
                Require(ReadLog(logFile) == previousLog, "Disabled startup changed an existing log.");
            }
            else if (mode == "settings-unavailable")
            {
                // The production gate must not propagate settings-initialization failures.
                // This sandbox intentionally has no BaseSettings.xml.
                WriteAll("unavailable");
                Thread.Sleep(400);
                Require(!Directory.Exists(logFolder), "Unavailable settings created a log directory.");
            }
            else
            {
                throw new ArgumentException("Unknown test mode: " + mode);
            }

            Console.WriteLine("PASS " + mode);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void WriteAll(string prefix)
    {
        AppLog.Info("{0}-info", prefix);
        AppLog.Warning("{0}-warning", prefix);
        AppLog.Error("{0}-error", prefix);
        AppLog.InfoAlways("{0}-info-alias", prefix);
        AppLog.WarningAlways("{0}-warning-alias", prefix);
        AppLog.ErrorAlways("{0}-error-alias", prefix);
    }

    private static void AssertPersistedDisabled()
    {
        var document = XDocument.Load(Path.Combine(SettingsClient.ApplicationFolder(), "Settings.xml"));
        var setting = document.Root.Elements("Namespace")
            .Single(n => (string)n.Attribute("Name") == "Appearance")
            .Elements("Setting").Single(s => (string)s.Attribute("Name") == "EnableLogging");
        Require(!bool.Parse(setting.Element("Value").Value), "The disabled preference was not persisted.");
    }

    private static string ReadLog(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
