using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;

namespace Si_HeavyTeleporter
{
    /// <summary>
    /// The config lives in UserData/cfg/HeavyTeleporter/ (the server's own cfg/ folder, next to
    /// gametype/ and maps/). A config file still sitting loose in UserData/ from an
    /// older version is moved there once.
    /// </summary>
    internal static class ConfigPaths
    {
        public static string Resolve(string fileName)
        {
            string userData = MelonEnvironment.UserDataDirectory;
            string dir = Path.Combine(userData, "cfg", "HeavyTeleporter");
            string path = Path.Combine(dir, fileName);
            string legacy = Path.Combine(userData, fileName);
            try
            {
                Directory.CreateDirectory(dir);
                if (!File.Exists(path) && File.Exists(legacy))
                {
                    File.Move(legacy, path);
                    MelonLogger.Msg("HeavyTeleporter: moved config " + legacy + " -> " + path);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("HeavyTeleporter: could not move " + legacy + " -> " + path + ": " + ex.Message);
            }
            return path;
        }
    }
}
