﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendaryExplorerCore.Gammtek.Extensions.Collections.Generic;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Misc;
using ME3TweaksCore.Targets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ME3TweaksCore.Services.BasegameFileIdentification
{
    public class BasegameFileIdentificationService
    {
        /// <summary>
        /// Database of locally installed files. The mapping is as follows:
        /// [GameNameAsString] -> Dictionary of relative file paths -> List of records about that file. E.g. Size, hash, name.
        /// </summary>
        private static Dictionary<string, CaseInsensitiveDictionary<List<BasegameFileRecord>>> Database;

        /// <summary>
        /// If the BasegameFileIdentificationService has been initially loaded
        /// </summary>
        public static bool ServiceLoaded { get; private set; }

        /// <summary>
        /// Service name for logging
        /// </summary>
        private const string ServiceLoggingName = @"Basegame File Identification Service";

        private static void LoadLocalBasegameIdentificationService()
        {
            if (Database != null) return;
            Database = new CaseInsensitiveDictionary<CaseInsensitiveDictionary<List<BasegameFileRecord>>>();
            LoadDatabase(Database);
        }


        private static void LoadDatabase(Dictionary<string, CaseInsensitiveDictionary<List<BasegameFileRecord>>> database, JToken serverData = null)
        {
            var typeStr = @"Local";
            var file = MCoreFilesystem.GetLocalBasegameIdentificationServiceFile();
            if (File.Exists(file))
            {
                try
                {
                    var db = JsonConvert
                        .DeserializeObject<
                            Dictionary<string, CaseInsensitiveDictionary<
                                List<BasegameFileRecord>>>>(
                            File.ReadAllText(file));
                    database.ReplaceAll(db);
                    MLog.Information($@"Loaded {typeStr} {ServiceLoggingName}");
                }
                catch (Exception e)
                {
                    MLog.Error($@"Error loading {typeStr} {ServiceLoggingName}: {e.Message}");
                    var db = getBlankBGFIDB();
                    database.ReplaceAll(db);
                }
            }
            else
            {
                MLog.Information($@"Loaded blank {typeStr} {ServiceLoggingName}");
                var db = getBlankBGFIDB();
                database.ReplaceAll(db);
            }
        }

        public static void AddLocalBasegameIdentificationEntries(List<BasegameFileRecord> entries)
        {
            LoadLocalBasegameIdentificationService();

            bool updated = false;
            // Update the DB
            foreach (var entry in entries)
            {
                string gameKey = entry.game == @"0"
                    ? @"LELAUNCHER"
                    : MUtilities.GetGameFromNumber(entry.game).ToString();
                if (Database.TryGetValue(gameKey, out var gameDB))
                {
                    List<BasegameFileRecord> existingInfos;
                    if (!gameDB.TryGetValue(entry.file, out existingInfos))
                    {
                        existingInfos = new List<BasegameFileRecord>();
                        gameDB[entry.file] = existingInfos;
                    }

                    if (existingInfos.All(x => x.hash != entry.hash))
                    {
                        // new info
                        entry.file = null; // Do not serialize this
                        entry.game = null; // Do not serialize this
                        existingInfos.Add(entry);
                        updated = true;
                    }
                }
            }

            // Serialize it back to disk
            if (updated)
            {
                CommitDatabaseToDisk();
            }
            else
            {
                MLog.Information($@"Local {ServiceLoggingName} did not need updating");

            }
        }

        private static void CommitDatabaseToDisk()
        {

#if DEBUG
            var outText = JsonConvert.SerializeObject(Database, Formatting.Indented,
                new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore });
#else
                var outText =
 JsonConvert.SerializeObject(Database, new JsonSerializerSettings() { NullValueHandling =
 NullValueHandling.Ignore });
#endif
            try
            {
                File.WriteAllText(MCoreFilesystem.GetLocalBasegameIdentificationServiceFile(), outText);
                MLog.Information($@"Updated local {ServiceLoggingName}");

            }
            catch (Exception e)
            {
                // bwomp bwomp
                MLog.Error($@"Error saving local BGFIS: {e.Message}");
            }
        }

        /// <summary>
        /// Looks up information about a basegame file using the {ServiceLoggingName}
        /// </summary>
        /// <param name="target"></param>
        /// <param name="fullfilepath"></param>
        /// <returns></returns>
        public static BasegameFileRecord GetBasegameFileSource(GameTarget target, string fullfilepath, string md5 = null)
        {
            LoadLocalBasegameIdentificationService();
            if (Database.TryGetValue(target.Game.ToString(), out var infosForGameL))
            {
                var relativeFilename = fullfilepath.Substring(target.TargetPath.Length + 1).ToUpper();

                if (infosForGameL.TryGetValue(relativeFilename, out var items))
                {
                    md5 ??= MUtilities.CalculateHash(fullfilepath);
                    var match = items.FirstOrDefault(x => x.hash == md5);
                    if (match != null)
                    {
                        return match;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns a blank Basegame Identification Database
        /// </summary>
        /// <returns></returns>
        private static Dictionary<string, CaseInsensitiveDictionary<List<BasegameFileRecord>>> getBlankBGFIDB()
        {
            return new Dictionary<string, CaseInsensitiveDictionary<List<BasegameFileRecord>>>
            {
                [@"ME1"] = new CaseInsensitiveDictionary<List<BasegameFileRecord>>(),
                [@"ME2"] = new CaseInsensitiveDictionary<List<BasegameFileRecord>>(),
                [@"ME3"] = new CaseInsensitiveDictionary<List<BasegameFileRecord>>(),
                [@"LE1"] = new CaseInsensitiveDictionary<List<BasegameFileRecord>>(),
                [@"LE2"] = new CaseInsensitiveDictionary<List<BasegameFileRecord>>(),
                [@"LE3"] = new CaseInsensitiveDictionary<List<BasegameFileRecord>>(),
                [@"LELauncher"] = new CaseInsensitiveDictionary<List<BasegameFileRecord>>(),
            };
        }

        // This service doesn't take any data but uses the service loader model.
        public static bool LoadService(JToken data)
        {
            LoadLocalBasegameIdentificationService();
            ServiceLoaded = true;
            return true;
        }

        private static object syncObj = new object();

        /// <summary>
        /// Purges all entries for the specified game and commits the file back to disk.
        /// </summary>
        /// <param name="game">The game to purge entries for</param>
        public static void PurgeEntriesForGame(MEGame game)
        {
            lock (syncObj)
            {
                if (Database.TryGetValue(game.ToString(), out var infosForGameL))
                {
                    MLog.Information($@"Clearing basegame filedatabase entries for {game}");
                    infosForGameL.Clear();
                    CommitDatabaseToDisk();
                }
            }
        }

        /// <summary>
        /// Returns all entries for a given game
        /// </summary>
        /// <param name="game">The game to get entries for</param>
        /// <returns>Dictionary mapping for the game</returns>
        public static CaseInsensitiveDictionary<List<BasegameFileRecord>> GetEntriesForGame(MEGame game)
        {
            if (Database.TryGetValue(game.ToString(), out var gameEntries))
            {
                return gameEntries;
            }

            return new CaseInsensitiveDictionary<List<BasegameFileRecord>>(0); // Return nothing.
        }
    }
}