using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Web;

namespace Chalmers.ILL.Members
{
    // Replaces the Umbraco member database (cmsMember tables in Umbraco.sdf) with a small,
    // manually maintained JSON file. There are only a handful of accounts, so a file is enough
    // and removes the last live database dependency on Umbraco.
    public static class MemberFileStore
    {
        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(MemberFileStore));

        // Guards Load/Save against the lost-update race in FileMembershipProvider.ChangePassword
        // and MemberAdminService (both do Load -> mutate -> Save). A single in-process lock is
        // enough because the app is deployed as a single instance (see Fastställda designbeslut).
        private static readonly object _lock = new object();

        public static List<MemberAccount> Load() => Load(ResolvePath());

        public static List<MemberAccount> Load(string path)
        {
            if (!File.Exists(path))
                return new List<MemberAccount>();

            lock (_lock)
            {
                try
                {
                    var json = File.ReadAllText(path);
                    return JsonConvert.DeserializeObject<List<MemberAccount>>(json) ?? new List<MemberAccount>();
                }
                catch (Exception e)
                {
                    _log.Error("Failed to load member accounts from " + path + ".", e);
                    return new List<MemberAccount>();
                }
            }
        }

        public static void Save(List<MemberAccount> accounts) => Save(accounts, ResolvePath());

        public static void Save(List<MemberAccount> accounts, string path)
        {
            var json = JsonConvert.SerializeObject(accounts, Formatting.Indented);

            // Write to a temp file on the same volume, then swap it in with a rename, so a crash
            // mid-write can't leave members.json truncated (File.WriteAllText truncates first).
            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            lock (_lock)
            {
                File.WriteAllText(tempPath, json);
                try
                {
                    if (File.Exists(path))
                        File.Replace(tempPath, path, null);
                    else
                        File.Move(tempPath, path);
                }
                catch
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                    throw;
                }
            }
        }

        private static string ResolvePath()
        {
            var appRoot = HttpRuntime.AppDomainAppPath;
            if (string.IsNullOrEmpty(appRoot))
                appRoot = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(appRoot, "Config", "members.json");
        }
    }
}
