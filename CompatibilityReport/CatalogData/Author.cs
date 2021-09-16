﻿using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace CompatibilityReport.CatalogData
{
    [Serializable] 
    public class Author
    {
        // Steam ID and Custom URL as seen on the Steam Workshop. Steam is inconsistent with them on the Workshop, so the Updater might get an ID even if an URL exists.
        // The ID always exists and cannot be changed. The Custom URL is optional and can be assigned, changed or removed at any time by the Steam user.
        // Converting from Custom URL to Steam ID can be done manually on some websites, or through the Steam API.
        public ulong SteamID { get; private set; }
        public string CustomUrl { get; private set; }

        public string Name { get; private set; }

        // Last seen is set to the most recent mod update and retirement is calculated from LastSeen. Both can be overruled through the FileImporter.
        public DateTime LastSeen { get; private set; }
        public bool Retired { get; private set; }

        // This exclusion is set by the FileImporter, to prevent a retirement state from being reset by the WebCrawler.
        public bool ExclusionForRetired { get; private set; }

        // Change notes contain a list of changes by the Updater.
        [XmlArrayItem("ChangeNote")] public List<string> ChangeNotes { get; private set; } = new List<string>();

        // Used by the Updater to indicate if this author was added during this session.
        [XmlIgnore] public bool AddedThisSession { get; private set; }


        /// <summary>Default constructor for deserialization.</summary>
        private Author()
        {
            // Nothing to do here.
        }


        /// <summary>Constructor for author creation.</summary>
        public Author(ulong steamID, string customUrl)
        {
            SteamID = steamID;
            CustomUrl = customUrl ?? "";

            AddedThisSession = true;
        }


        /// <summary>Updates one or more author properties.</summary>
        /// <remarks>The Steam ID can only be changed if it was zero before.</remarks>
        public void Update(ulong steamID = 0, string customUrl = null, string name = null, DateTime lastSeen = default, 
            bool? retired = null, bool? exclusionForRetired = null)
        {
            SteamID = SteamID == 0 ? steamID : SteamID;
            CustomUrl = customUrl ?? CustomUrl;

            Name = name ?? Name;

            LastSeen = lastSeen == default ? LastSeen : lastSeen;
            Retired = retired ?? Retired;
            ExclusionForRetired = exclusionForRetired ?? ExclusionForRetired;
        }


        /// <summary>Adds a change note to the author.</summary>
        public void AddChangeNote(string changeNote)
        {
            ChangeNotes.Add(changeNote);
        }


        /// <summary>Converts the author to a string containing the Steam ID or Custom URL, and the name.</summary>
        /// <returns>A string representing the author.</returns>
        public new string ToString()
        {
            return $"[{ (SteamID != 0 ? SteamID.ToString() : CustomUrl) }] { Name }";
        }
    }
}
