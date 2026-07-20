using Digimezzo.Foundation.Core.Settings;
using Dopamine.Core.Base;
using Dopamine.Core.Logging;
using SQLite;
using System;
using System.IO;

namespace Dopamine.Data
{
    public class SQLiteConnectionFactory : ISQLiteConnectionFactory
    {
        private static readonly object pragmaLock = new object();
        private static bool databasePragmasApplied;

        public string DatabaseFile => Path.Combine(SettingsClient.ApplicationFolder(), ProductInformation.ApplicationName + ".db");

        public SQLiteConnection GetConnection()
        {
            var connection = new SQLiteConnection(this.DatabaseFile)
            {
                BusyTimeout = TimeSpan.FromSeconds(10)
            };

            this.ConfigureConnection(connection);
            return connection;
        }

        private void ConfigureConnection(SQLiteConnection connection)
        {
            try
            {
                // Per-connection pragmas (safe to re-apply).
                connection.Execute("PRAGMA temp_store=MEMORY;");
                connection.Execute("PRAGMA cache_size=-8000;"); // ~8 MB page cache
                connection.Execute("PRAGMA foreign_keys=ON;");

                // Database-wide pragmas once per process (WAL survives reconnects).
                if (!databasePragmasApplied)
                {
                    lock (pragmaLock)
                    {
                        if (!databasePragmasApplied)
                        {
                            string journalMode = connection.ExecuteScalar<string>("PRAGMA journal_mode=WAL;");
                            connection.Execute("PRAGMA synchronous=NORMAL;");
                            // Best-effort; older SQLite builds may ignore mmap.
                            try
                            {
                                connection.Execute("PRAGMA mmap_size=268435456;");
                            }
                            catch
                            {
                            }

                            databasePragmasApplied = true;
                            AppLog.Info("SQLite configured. JournalMode={0}, Synchronous=NORMAL", journalMode);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning("Could not fully configure SQLite connection. Exception: {0}", ex.Message);
            }
        }
    }
}
