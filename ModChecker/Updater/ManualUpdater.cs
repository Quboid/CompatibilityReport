﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ModChecker.DataTypes;
using ModChecker.Util;


// Manual Updater updates the catalog with information from CSV files in de updater folder. See separate guide for details.


namespace ModChecker.Updater
{
    internal static class ManualUpdater
    {
        // Stringbuilder to gather the combined CSVs, to be saved with the new catalog
        private static StringBuilder CSVCombined;


        // Start the manual updater; will load and process CSV files, update the active catalog and save it with a new version; including change notes
        internal static void Start()
        {
            // Exit if the updater is not enabled in settings
            if (!ModSettings.UpdaterEnabled)
            {
                return;
            }

            // If the current active catalog is version 1 or 2, we're (re)building the catalog from scratch; wait with manual updates until version 3
            if (ActiveCatalog.Instance.Version < 3)
            {
                Logger.UpdaterLog($"ManualUpdater skipped.", extraLine: true);

                return;
            }

            Logger.Log("Manual Updater started. See separate logfile for details.");
            Logger.UpdaterLog("Manual Updater started.");

            // Initialize the dictionaries we need
            CatalogUpdater.Init();

            CSVCombined = new StringBuilder();

            // Read all the CSVs
            ReadCSVs();

            if (CSVCombined.Length == 0)
            {
                Logger.UpdaterLog("No CSV files found. No new catalog created.");
            }
            else
            {
                // Update the catalog with the new info and save it to a new version
                string partialPath = CatalogUpdater.Start(autoUpdater: false);

                // Save the combined CSVs, next to the catalog
                if (!string.IsNullOrEmpty(partialPath))
                {
                    Toolkit.SaveToFile(CSVCombined.ToString(), partialPath + "_ManualUpdates.txt");
                }
            }

            // Clean up
            CSVCombined = null;

            Logger.UpdaterLog("Manual Updater has shutdown.", extraLine: true, duplicateToRegularLog: true);
        }


        // Read all CSV files
        private static void ReadCSVs()
        {
            // Get all CSV filenames
            List<string> CSVfiles = Directory.GetFiles(ModSettings.updaterPath, "*.csv").ToList();

            // Exit if we have no CSVs to read
            if (!CSVfiles.Any())
            {
                return;
            }

            // Sort the list
            CSVfiles.Sort();

            bool overallSuccess = true;
            uint numberOfFiles = 0;

            // Process all CSV files
            foreach (string CSVfile in CSVfiles)
            {
                Logger.UpdaterLog($"Processing \"{ CSVfile }\".");

                // Add filename to the combined CSV file
                CSVCombined.AppendLine($"#####################################################################");
                CSVCombined.AppendLine($"#### { Toolkit.PrivacyPath(CSVfile) }");
                CSVCombined.AppendLine("");

                numberOfFiles++;

                bool singleFileSuccess = true;

                // Read a single CSV file
                using (StreamReader reader = File.OpenText(CSVfile))
                {
                    string line;

                    // Read each line and process the command
                    while ((line = reader.ReadLine()) != null)
                    {
                        // Process a line
                        if (ProcessLine(line))
                        {
                            // Add this line to the complete list
                            CSVCombined.AppendLine(line);
                        }
                        else
                        {
                            // Add the failed line with a comment to the complete list
                            CSVCombined.AppendLine("# [NOT PROCESSED] " + line);

                            singleFileSuccess = false;

                            overallSuccess = false;
                        }
                    }
                }

                // Add some space to the combined CSV file
                CSVCombined.AppendLine("");

                // Rename the processed CSV file
                string newFileName = CSVfile + (singleFileSuccess ? ".processed.txt" : ".processed_partially.txt");

                /* [Todo 0.3] Re-enable this
                if (!Toolkit.MoveFile(CSVfile, newFileName))
                {
                    Logger.UpdaterLog($"Could not rename \"{ Toolkit.PrivacyPath(CSVfile) }\". Rename or delete it manually to avoid processing it again.", Logger.error);
                }
                */

                // Log if we couldn't process all lines
                if (!singleFileSuccess)
                {
                    Logger.UpdaterLog($"\"{ newFileName }\" not completely processed.", Logger.warning);
                }
            }

            string s = numberOfFiles == 1 ? "" : "s";

            // Log number of processed files to updater log
            Logger.UpdaterLog($"{ numberOfFiles } CSV file{ s } processed{ (overallSuccess ? "" : ", with some errors" ) }.");
            
            // Log to regular log
            if (overallSuccess)
            {
                Logger.Log($"Manual Updater processed { numberOfFiles } CSV file{ s }. Check logfile for details.");
            }
            else
            {
                Logger.Log($"Manual Updater processed { numberOfFiles } CSV file{ s }, with some errors. Check logfile for details.", 
                    Logger.warning);
            }
        }


        // Process a line from a CSV file
        private static bool ProcessLine(string line)
        {
            // Skip empty lines, without returning an error
            if (string.IsNullOrEmpty(line))
            {
                return true;
            }

            // Skip comments starting with a '#', without returning an error
            if (line[0] == '#')
            {
                return true;
            }

            // Split the line
            string[] lineFragments = line.Split(',');

            // Get the action
            string action = lineFragments[0].Trim().ToLower();

            // Get the id as number (Steam ID or group ID) and as string (author custom url, exclusion category, game version string, catalog note)
            string idString = lineFragments.Length < 2 ? "" : lineFragments[1].Trim();
            ulong id = Toolkit.ConvertToUlong(idString);

            // Get the rest of the data, if present
            string extraData = "";
            ulong secondID = 0;

            if (lineFragments.Length > 2)
            {
                extraData = lineFragments[2].Trim();

                secondID = Toolkit.ConvertToUlong(extraData);
                
                // Set extraData to the 3rd element if that isn't numeric (secondID = 0), otherwise to the 4th element if available, otherwise to an empty string
                extraData = secondID == 0 ? extraData : (lineFragments.Length > 3 ? lineFragments[3].Trim() : "");
            }

            bool success;

            // Act on the action found with the additional data  [Todo 0.3] Check if all actions are updated in CatalogUpdater
            switch (action)
            {
                case "add_mod":
                    // Join the lineFragments to allow for commas in the mod name
                    success = AddMod(id, authorID: secondID, 
                        modName: lineFragments.Length < 5 ? extraData : string.Join(", ", lineFragments, 3, lineFragments.Length - 3));
                    break;

                case "remove_mod":
                    success = RemoveMod(id);
                    break;

                case "add_archiveurl":
                case "add_sourceurl":
                case "add_gameversion":
                case "add_requireddlc":
                case "add_status":
                case "remove_requireddlc":
                case "remove_status":
                    success = ChangeModItem(id, action, extraData);
                    break;

                case "add_note":
                    // Join the lineFragments to allow for commas in the note
                    success = ChangeModItem(id, action, string.Join(", ", lineFragments, 2, lineFragments.Length - 2));
                    break;

                case "remove_archiveurl":
                case "remove_sourceurl":
                case "remove_gameversion":
                case "remove_note":
                    success = ChangeModItem(id, action, "", 0);
                    break;

                case "add_requiredmod":
                case "add_successor":
                case "add_alternative":
                case "remove_requiredmod":
                case "remove_successor":
                case "remove_alternative":
                    success = ChangeModItem(id, action, listMember: secondID);
                    break;

                case "add_compatibility":
                case "remove_compatibility":
                    success = AddRemoveCompatibility(steamID1: id, steamID2: secondID, compatibility: extraData);
                    break;

                case "add_group":
                    if (lineFragments.Length < 4)
                    {
                        success = false;
                    }
                    else
                    {
                        // Get all linefragments to get all group members as strings; remove the first two elements: action and name
                        List<string> groupMembers = lineFragments.ToList();

                        groupMembers.RemoveRange(0, 2);

                        success = AddGroup(groupName: idString, groupMembers);
                    }
                    break;

                case "remove_group":
                    // Remove the group after changing it to a replacement mod as 'required mod' for all affected mods
                    success = RemoveGroup(groupID: id, replacementMod: secondID);
                    break;

                case "add_groupmember":
                case "remove_groupmember":
                    success = AddRemoveGroupMember(groupID: id, groupMember: secondID);
                    break;

                case "add_authorid":
                case "add_authorurl":
                case "add_lastseen":
                case "add_retired":
                case "remove_authorurl":
                case "remove_retired":
                    if (id == 0)
                    {
                        success = ChangeAuthorItem(authorURL: idString, action, extraData, newAuthorID: secondID);
                    }
                    else
                    {
                        success = ChangeAuthorItem(authorID: id, action, extraData);
                    }                    
                    break;

                case "remove_exclusion":
                    success = RemoveExclusion(steamID: id, subitem: secondID, categoryString: extraData);
                    break;

                case "add_cataloggameversion":
                    success = SetCatalogGameVersion(gameVersionString: idString);
                    break;

                case "add_catalognote":
                    // Join the lineFragments to allow for commas in the note
                    CatalogUpdater.SetNote(string.Join(", ", lineFragments, 1, lineFragments.Length - 1).Trim());
                    success = true;
                    break;

                case "remove_catalognote":
                    CatalogUpdater.SetNote("");
                    success = true;
                    break;

                default:
                    Logger.UpdaterLog($"Line not processed, invalid action. Line: { line }", Logger.warning);
                    success = false;
                    break;
            }

            return success;
        }


        // Add unlisted mod
        private static bool AddMod(ulong steamID, ulong authorID, string modName)
        {
            // Exit if Steam ID is not valid, exists in the active catalog or exists in the collected mods
            if (steamID <= ModSettings.highestFakeID || ActiveCatalog.Instance.ModDictionary.ContainsKey(steamID) || CatalogUpdater.CollectedModInfo.ContainsKey(steamID))
            {
                return false;
            }

            // Create a new mod
            Mod newMod = new Mod(steamID, modName, authorID, "");

            // Add unlisted status
            newMod.Statuses.Add(Enums.ModStatus.UnlistedInWorkshop);

            // Add mod to collected mods
            CatalogUpdater.CollectedModInfo.Add(steamID, newMod);

            return true;
        }


        // Remove an unlisted or removed mod from the catalog
        private static bool RemoveMod(ulong steamID)
        {
            // Exit if Steam ID is not valid, doesn't exist in the active catalog or exists in the collected mods
            if (steamID <= ModSettings.highestFakeID || !ActiveCatalog.Instance.ModDictionary.ContainsKey(steamID) || CatalogUpdater.CollectedModInfo.ContainsKey(steamID))
            {
                return false;
            }

            // Exit if it is listed as required, needed, successor or alternative mod anywhere
            // [Todo 0.3]

            // Get the mod from the active catalog
            Mod mod = ActiveCatalog.Instance.ModDictionary[steamID];

            // Exit if it does not have the unlisted or removed status
            if (!mod.Statuses.Contains(Enums.ModStatus.UnlistedInWorkshop) && !mod.Statuses.Contains(Enums.ModStatus.RemovedFromWorkshop))
            {
                return false;
            }

            // Add mod to the removals list
            if (CatalogUpdater.CollectedRemovals.ContainsKey(steamID))
            {
                // Add a new string to the existing list
                CatalogUpdater.CollectedRemovals[steamID].Add("remove_mod");
            }
            else
            {
                // Add a new entry with the mod and a new, single-entry list
                CatalogUpdater.CollectedRemovals.Add(steamID, new List<string> { "remove_mod" });
            }

            return true;
        }


        // 
        private static bool ChangeModItem(ulong steamID, string action, string itemData)
        {
            // Exit if itemData is empty
            if (string.IsNullOrEmpty(itemData))
            {
                return false;
            }

            // Do the actual change
            return ChangeModItem(steamID, action, itemData, 0);
        }


        // 
        private static bool ChangeModItem(ulong steamID, string action, ulong listMember)
        {
            // Exit if the listMember is not in the active catalog (as mod or group) or collected mod dictionary [Todo 0.3] Add collected group info
            if (listMember == 0 || (!ActiveCatalog.Instance.ModDictionary.ContainsKey(listMember) && 
                !ActiveCatalog.Instance.ModGroupDictionary.ContainsKey(listMember) && !CatalogUpdater.CollectedModInfo.ContainsKey(listMember)))
            {
                return false;
            }

            // Do the actual change
            return ChangeModItem(steamID, action, "", listMember);
        }


        // 
        private static bool ChangeModItem(ulong steamID, string action, string itemData, ulong listMember)
        {
            // Exit if the Steam ID is invalid or not in the active catalog or the collected mod dictionary
            if (steamID <= ModSettings.highestFakeID || 
                (!ActiveCatalog.Instance.ModDictionary.ContainsKey(steamID) && !CatalogUpdater.CollectedModInfo.ContainsKey(steamID)))
            {
                return false;
            }

            // Get a copy of the catalog mod from the collected mods dictionary or create a new copy
            Mod newMod= CatalogUpdater.CollectedModInfo.ContainsKey(steamID) ? CatalogUpdater.CollectedModInfo[steamID] : 
                Mod.Copy(ActiveCatalog.Instance.ModDictionary[steamID]);

            // Update the new mod [Todo 0.3] Needs action in CatalogUpdater
            if (action == "add_archiveurl" || action == "remove_archiveurl")
            {
                newMod.Update(archiveURL: itemData);
            }
            else if (action == "add_sourceurl")
            {
                newMod.Update(sourceURL: itemData);

                ActiveCatalog.Instance.AddExclusion(steamID, Enums.ExclusionCategory.SourceURL);
            }
            else if (action == "remove_sourceurl")
            {
                newMod.Update(sourceURL: "");

                ActiveCatalog.Instance.RemoveExclusion(steamID, Enums.ExclusionCategory.SourceURL);
            }
            else if (action == "add_gameversion" || action == "remove_gameversion")
            {
                // [Todo 0.3]
                return false;

                // Add or remove exclusion
                if (action[0] == 'a')
                {
                    // Add exclusion only if the mod has a different gameversion now
                    if (ActiveCatalog.Instance.ModDictionary[steamID].CompatibleGameVersionString != newMod.CompatibleGameVersionString &&
                        !string.IsNullOrEmpty(ActiveCatalog.Instance.ModDictionary[steamID].CompatibleGameVersionString) &&
                        ActiveCatalog.Instance.ModDictionary[steamID].CompatibleGameVersionString != GameVersion.Formatted(GameVersion.Unknown))
                    {
                        ActiveCatalog.Instance.AddExclusion(steamID, Enums.ExclusionCategory.GameVersion);
                    }
                }
                else
                {
                    ActiveCatalog.Instance.RemoveExclusion(steamID, Enums.ExclusionCategory.GameVersion);
                }
            }
            else if (action == "add_requireddlc" || action == "remove_requireddlc")
            {
                Enums.DLC dlc;

                // Convert the DLC string to enum [Todo 0.3] Move to Toolkit method?
                try
                {
                    dlc = (Enums.DLC)Enum.Parse(typeof(Enums.DLC), itemData, ignoreCase: true);
                }
                catch
                {
                    Logger.UpdaterLog($"DLC \"{ itemData }\" could not be converted.", Logger.debug);

                    return false;
                }

                if (action[0] == 'a')
                {
                    // Add the required DLC if the mod doesn't already have it
                    if (newMod.RequiredDLC.Contains(dlc))
                    {
                        return false;
                    }

                    newMod.RequiredDLC.Add(dlc);

                    // Add exclusion
                    ActiveCatalog.Instance.AddExclusion(steamID, Enums.ExclusionCategory.RequiredDLC, (uint)dlc);
                }
                else
                {
                    // Remove the required DLC if the mod has it
                    if (!newMod.RequiredDLC.Contains(dlc))
                    {
                        return false;
                    }

                    newMod.RequiredDLC.Remove(dlc);

                    // Remove exclusion if it exists
                    ActiveCatalog.Instance.RemoveExclusion(steamID, Enums.ExclusionCategory.RequiredDLC, (uint)dlc);
                }
            }
            else if (action == "add_status" || action == "remove_status")  // [Todo 0.3] Needs action in CatalogUpdater for additional statuses
            {
                Enums.ModStatus status;

                // Convert the status string to enum [Todo 0.3] Move to Toolkit method?
                try
                {
                    status = (Enums.ModStatus)Enum.Parse(typeof(Enums.ModStatus), itemData, ignoreCase: true);
                }
                catch
                {
                    Logger.UpdaterLog($"Status \"{ itemData }\" could not be converted.", Logger.debug);

                    return false;
                }

                if (action[0] == 'a')
                {
                    // Add the status if the mod doesn't already have it
                    if (newMod.Statuses.Contains(status))
                    {
                        return false;
                    }
                    
                    newMod.Statuses.Add(status);

                    // Add exclusion for some statuses
                    if (status == Enums.ModStatus.UnlistedInWorkshop || status == Enums.ModStatus.RemovedFromWorkshop)
                    {
                        ActiveCatalog.Instance.AddExclusion(steamID, Enums.ExclusionCategory.Status, (uint)status);
                    }
                }
                else
                {
                    // Remove the status if the mod has it
                    if (!newMod.Statuses.Contains(status))
                    {
                        return false;
                    }

                    newMod.Statuses.Remove(status);

                    // Remove exclusion if it exists; will only exist for some statuses
                    ActiveCatalog.Instance.RemoveExclusion(steamID, Enums.ExclusionCategory.Status, (uint)status);
                }
            }
            else if (action == "add_note" || action == "remove_note") // [Todo 0.3] Needs action in CatalogUpdater
            {
                // Add the new note (empty for 'remove')
                newMod.Update(note: itemData);
            }
            else if (action == "add_requiredmod")
            {
                // Add the required mod if the mod doesn't already have it
                if (newMod.RequiredMods.Contains(listMember))
                {
                    return false;
                }

                newMod.RequiredMods.Add(listMember);

                // Add exclusion
                ActiveCatalog.Instance.AddExclusion(steamID, Enums.ExclusionCategory.RequiredMod, listMember);
            }
            else if (action == "remove_requiredmod")
            {
                if (!newMod.RequiredMods.Contains(listMember))
                {
                    return false;
                }

                newMod.RequiredMods.Remove(listMember);

                // Remove exclusion if it exists
                ActiveCatalog.Instance.RemoveExclusion(steamID, Enums.ExclusionCategory.RequiredMod, listMember);
            }
            else if (action == "add_successor") // [Todo 0.3] Needs action in CatalogUpdater
            {
                if (newMod.SucceededBy.Contains(listMember))
                {
                    return false;
                }

                newMod.SucceededBy.Add(listMember);
            }
            else if (action == "remove_successor") // [Todo 0.3] Needs action in CatalogUpdater
            {
                if (!newMod.SucceededBy.Contains(listMember))
                {
                    return false;
                }

                newMod.SucceededBy.Remove(listMember);
            }
            else if (action == "add_alternative") // [Todo 0.3] Needs action in CatalogUpdater
            {
                if (newMod.Alternatives.Contains(listMember))
                {
                    return false;
                }

                newMod.Alternatives.Add(listMember);
            }
            else if (action == "remove_alternative") // [Todo 0.3] Needs action in CatalogUpdater
            {
                if (!newMod.Alternatives.Contains(listMember))
                {
                    return false;
                }

                newMod.Alternatives.Remove(listMember);
            }
            else
            {
                return false;
            }
            
            // Add the copied mod to the collected mods dictionary if the change was successful
            if (!CatalogUpdater.CollectedModInfo.ContainsKey(steamID))
            {
                CatalogUpdater.CollectedModInfo.Add(newMod.SteamID, newMod);
            }

            return true;
        }


        // Add an author item, by author profile ID
        private static bool ChangeAuthorItem(ulong authorID, string action, string itemData)
        {
            // Exit if the author ID is invalid or not in the active catalog
            if (authorID == 0 || !ActiveCatalog.Instance.AuthorIDDictionary.ContainsKey(authorID))
            {
                return false;
            }

            // Get a copy of the catalog author from the collected authors dictionary or create a new copy
            Author newAuthor = CatalogUpdater.CollectedAuthorIDs.ContainsKey(authorID) ? CatalogUpdater.CollectedAuthorIDs[authorID] : 
                Author.Copy(ActiveCatalog.Instance.AuthorIDDictionary[authorID]);
            
            // Update the author
            bool success = ChangeAuthorItem(newAuthor, action, itemData, 0);

            // Add the copied author to the collected mods dictionary if the change was successful
            if (success && !CatalogUpdater.CollectedAuthorIDs.ContainsKey(authorID))
            {
                CatalogUpdater.CollectedAuthorIDs.Add(authorID, newAuthor);
            }

            return success;
        }


        // Add an author item, by author custom URL
        private static bool ChangeAuthorItem(string authorURL, string action, string itemData, ulong newAuthorID = 0)
        {
            // Exit if the author custom URL is empty or not in the active catalog
            if (string.IsNullOrEmpty(authorURL) || !ActiveCatalog.Instance.AuthorURLDictionary.ContainsKey(authorURL))
            {
                return false;
            }

            // Get a copy of the catalog author from the collected authors dictionary or create a new copy
            Author newAuthor = CatalogUpdater.CollectedAuthorURLs.ContainsKey(authorURL) ? CatalogUpdater.CollectedAuthorURLs[authorURL] : 
                Author.Copy(ActiveCatalog.Instance.AuthorURLDictionary[authorURL]);

            // Update the author
            bool success = ChangeAuthorItem(newAuthor, action, itemData, newAuthorID);

            // Add the copied author to the collected mods dictionary if the change was successful
            if (success && !CatalogUpdater.CollectedAuthorURLs.ContainsKey(authorURL))
            {
                CatalogUpdater.CollectedAuthorURLs.Add(authorURL, newAuthor);
            }

            return success;
        }


        // Add an author item, by author type
        private static bool ChangeAuthorItem(Author newAuthor, string action, string itemData, ulong newAuthorID)
        {
            if (newAuthor == null)
            {
                return false;
            }

            // Update the author item
            if (action == "add_authorid")
            {
                // Exit if the author already has an ID or if the new ID is zero
                if (newAuthor.ProfileID != 0 || newAuthorID == 0)
                {
                    return false;
                }

                newAuthor.Update(profileID: newAuthorID);
            }
            else if (action == "add_authorurl")
            {
                // Exit if the new URL is empty
                if (string.IsNullOrEmpty(itemData))
                {
                    return false;
                }

                newAuthor.Update(customURL: itemData);
            }
            else if (action == "remove_authorurl")
            {
                // Exit if the author doesn't have a custom URL
                if (string.IsNullOrEmpty(newAuthor.CustomURL))
                {
                    return false;
                }

                newAuthor.Update(customURL: "");
            }
            else if (action == "add_lastseen")
            {
                DateTime lastSeen;

                // Check if we have a valid date that is more recent than the current author last seen date
                try
                {
                    lastSeen = DateTime.ParseExact(itemData, "yyyy-MM-dd", new CultureInfo("en-GB"));

                    if (lastSeen < newAuthor.LastSeen)
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }

                newAuthor.Update(lastSeen: lastSeen);
            }
            else if (action == "add_retired")
            {
                if (newAuthor.Retired)
                {
                    return false;
                }

                newAuthor.Update(retired: true);
            }
            else if (action == "remove_retired")
            {
                if (!newAuthor.Retired && true)    // [Todo 0.3] Doesn't work as we can't differentiate between set to false or unset and defaulting to false
                {
                    return false;
                }

                newAuthor.Update(retired: false);
            }
            else
            {
                return false;
            }            

            return true;
        }


        // 
        private static bool AddGroup(string groupName, List<string> groupMembers)
        {
            // [Todo 0.3]

            return false;
        }


        // 
        private static bool RemoveGroup(ulong groupID, ulong replacementMod)
        {
            // [Todo 0.3]

            return false;
        }


        // 
        private static bool AddRemoveGroupMember(ulong groupID, ulong groupMember)
        {
            // [Todo 0.3]

            return false;
        }


        // 
        private static bool AddRemoveCompatibility(ulong steamID1, ulong steamID2, string compatibility)
        {
            // [Todo 0.3]

            return false;
        }


        // 
        private static bool RemoveExclusion(ulong steamID, ulong subitem, string categoryString)
        {
            // Exit if the Steam ID is zero or the category is null
            if (steamID == 0 || string.IsNullOrEmpty(categoryString))
            {
                return false;
            }

            Enums.ExclusionCategory category;

            // Convert the category string to enum [Todo 0.3] Move to Toolkit method?
            try
            {
                category = (Enums.ExclusionCategory)Enum.Parse(typeof(Enums.ExclusionCategory), categoryString, ignoreCase: true);
            }
            catch
            {
                Logger.UpdaterLog($"Exclusion category \"{ categoryString }\" could not be converted.", Logger.debug);

                return false;
            }

            // Remove the exclusion; will return false if the exclusion didn't exist
            return ActiveCatalog.Instance.RemoveExclusion(steamID, category, subitem);
        }


        // 
        private static bool SetCatalogGameVersion(string gameVersionString)
        {
            // [Todo 0.3]

            return false;
        }
    }
}