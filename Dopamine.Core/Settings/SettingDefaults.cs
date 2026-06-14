using Digimezzo.Foundation.Core.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Dopamine.Core.Settings
{
    public static class SettingDefaults
    {
        private const string SettingsFileName = "Settings.xml";

        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, object> RuntimeValues = new Dictionary<string, object>();
        private static readonly HashSet<string> ExplicitRuntimeValues = new HashSet<string>();
        private static readonly HashSet<string> AttemptedXmlWrites = new HashSet<string>();

        public static T GetOrAdd<T>(string settingsNamespace, string settingName, T defaultValue, bool requiresRestart = false)
        {
            string key = GetKey(settingsNamespace, settingName);
            T runtimeValue;

            if (TryGetRuntimeValue(key, true, out runtimeValue))
            {
                return runtimeValue;
            }

            try
            {
                T persistedValue = SettingsClient.Get<T>(settingsNamespace, settingName);
                SetRuntimeValue(key, persistedValue, false);
                return persistedValue;
            }
            catch (Exception)
            {
                T xmlValue;

                if (TryReadSettingFromSettingsXml(settingsNamespace, settingName, out xmlValue))
                {
                    SetRuntimeValue(key, xmlValue, false);
                    return xmlValue;
                }

                if (TryGetRuntimeValue(key, false, out runtimeValue))
                {
                    return runtimeValue;
                }

                SetRuntimeValue(key, defaultValue, false);
                TryWriteSetting(settingsNamespace, settingName, defaultValue, requiresRestart, true);
                return defaultValue;
            }
        }

        public static void SetSafe<T>(string settingsNamespace, string settingName, T value, bool requiresRestart = false)
        {
            string key = GetKey(settingsNamespace, settingName);
            SetRuntimeValue(key, value, true);

            try
            {
                SettingsClient.Set<T>(settingsNamespace, settingName, value, requiresRestart);
                return;
            }
            catch (Exception)
            {
                TryWriteSetting(settingsNamespace, settingName, value, requiresRestart, false);
            }
        }

        private static void TryWriteSetting<T>(string settingsNamespace, string settingName, T value, bool requiresRestart, bool skipWhenAlreadyAttempted)
        {
            string key = GetKey(settingsNamespace, settingName);

            if (skipWhenAlreadyAttempted)
            {
                lock (SyncRoot)
                {
                    if (AttemptedXmlWrites.Contains(key))
                    {
                        return;
                    }

                    AttemptedXmlWrites.Add(key);
                }
            }

            try
            {
                SettingsClient.Set<T>(settingsNamespace, settingName, value, requiresRestart);
                TryWriteSettingsClient();
                return;
            }
            catch (Exception)
            {
            }

            try
            {
                WriteSettingToSettingsXml(settingsNamespace, settingName, value);
            }
            catch (Exception)
            {
            }
        }

        private static bool TryReadSettingFromSettingsXml<T>(string settingsNamespace, string settingName, out T value)
        {
            value = default(T);

            try
            {
                lock (SyncRoot)
                {
                    string settingsFilePath = Path.Combine(SettingsClient.ApplicationFolder(), SettingsFileName);

                    if (!File.Exists(settingsFilePath))
                    {
                        return false;
                    }

                    XDocument document = XDocument.Load(settingsFilePath, LoadOptions.PreserveWhitespace);

                    if (document.Root == null || document.Root.Name != "Settings")
                    {
                        return false;
                    }

                    XElement valueElement = document.Root
                        .Elements("Namespace")
                        .Where((element) => string.Equals((string)element.Attribute("Name"), settingsNamespace, StringComparison.OrdinalIgnoreCase))
                        .Elements("Setting")
                        .Where((element) => string.Equals((string)element.Attribute("Name"), settingName, StringComparison.OrdinalIgnoreCase))
                        .Elements("Value")
                        .FirstOrDefault();

                    if (valueElement == null)
                    {
                        return false;
                    }

                    value = ConvertValue<T>(valueElement.Value);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void WriteSettingToSettingsXml<T>(string settingsNamespace, string settingName, T value)
        {
            lock (SyncRoot)
            {
                string settingsFilePath = Path.Combine(SettingsClient.ApplicationFolder(), SettingsFileName);

                if (!File.Exists(settingsFilePath))
                {
                    return;
                }

                XDocument document = XDocument.Load(settingsFilePath, LoadOptions.PreserveWhitespace);

                if (document.Root == null || document.Root.Name != "Settings")
                {
                    return;
                }

                XElement namespaceElement = document.Root
                    .Elements("Namespace")
                    .FirstOrDefault((element) => string.Equals((string)element.Attribute("Name"), settingsNamespace, StringComparison.OrdinalIgnoreCase));

                if (namespaceElement == null)
                {
                    namespaceElement = new XElement("Namespace", new XAttribute("Name", settingsNamespace));
                    document.Root.Add(namespaceElement);
                }

                XElement settingElement = namespaceElement
                    .Elements("Setting")
                    .FirstOrDefault((element) => string.Equals((string)element.Attribute("Name"), settingName, StringComparison.OrdinalIgnoreCase));

                if (settingElement == null)
                {
                    settingElement = new XElement("Setting", new XAttribute("Name", settingName));
                    namespaceElement.Add(settingElement);
                }

                XElement valueElement = settingElement.Element("Value");

                if (valueElement == null)
                {
                    valueElement = new XElement("Value");
                    settingElement.Add(valueElement);
                }

                valueElement.Value = FormatValue(value);
                document.Save(settingsFilePath);
            }
        }

        private static void TryWriteSettingsClient()
        {
            try
            {
                SettingsClient.Write();
            }
            catch (Exception)
            {
            }
        }

        private static bool TryGetRuntimeValue<T>(string key, bool explicitOnly, out T value)
        {
            lock (SyncRoot)
            {
                object runtimeValue;

                if ((!explicitOnly || ExplicitRuntimeValues.Contains(key)) &&
                    RuntimeValues.TryGetValue(key, out runtimeValue) &&
                    runtimeValue is T)
                {
                    value = (T)runtimeValue;
                    return true;
                }
            }

            value = default(T);
            return false;
        }

        private static void SetRuntimeValue<T>(string key, T value, bool isExplicit)
        {
            lock (SyncRoot)
            {
                RuntimeValues[key] = value;

                if (isExplicit)
                {
                    ExplicitRuntimeValues.Add(key);
                }
                else
                {
                    ExplicitRuntimeValues.Remove(key);
                }
            }
        }

        private static string GetKey(string settingsNamespace, string settingName)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}", settingsNamespace, settingName);
        }

        private static T ConvertValue<T>(string value)
        {
            Type targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

            if (targetType == typeof(string))
            {
                return (T)(object)value;
            }

            if (targetType.IsEnum)
            {
                return (T)Enum.Parse(targetType, value, true);
            }

            return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        private static string FormatValue<T>(T value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            IFormattable formattable = value as IFormattable;

            if (formattable != null)
            {
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            }

            return value.ToString();
        }
    }
}
