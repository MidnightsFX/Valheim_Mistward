﻿using BepInEx;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using Mistward.common;
using System.Collections.Generic;
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Mistward
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    //[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class Mistward : BaseUnityPlugin
    {
        public const string PluginGUID = "MidnightsFX.Mistward";
        public const string PluginName = "Mistward";
        public const string PluginVersion = "0.7.1";

        internal static AssetBundle EmbeddedResourceBundle;
        public Config cfg;

        private void Awake()
        {
            cfg = new Config(Config);
            EmbeddedResourceBundle = AssetUtils.LoadAssetBundleFromResources("Mistward.AssetsEmbedded.mistward", typeof(Mistward).Assembly);
            AddLocalizations();
            // Mistward
            new JotunnPiece(
                new Dictionary<string, string>() {
                    { "name", "Mistward" },
                    { "catagory", "Misc" },
                    { "prefab", "MFX_Mistward" },
                    { "sprite", "mistward_icon" },
                    { "requiredBench", "piece_stonecutter" }
                },
                new Dictionary<string, bool>() { },
                new Dictionary<string, Tuple<int, bool>>()
                {
                    { "BlackMarble", Tuple.Create(30, true) },
                    { "Copper", Tuple.Create(15, true) },
                    { "Sap", Tuple.Create(10, true) },
                    { "BlackCore", Tuple.Create(1, true) },
                }
            );
        }

        /// <summary>
        /// This reads an embedded file resouce name, these are all resouces packed into the DLL
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        internal static string ReadEmbeddedResourceFile(string filename)
        {
            using (var stream = typeof(Mistward).Assembly.GetManifestResourceStream(filename))
            {
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        // This loads all localizations within the localization directory.
        // Localizations should be plain JSON objects with each of the two required entries being seperate eg:
        // "item_sword": "sword-name-here",
        // "item_sword_description": "sword-description-here",
        // the localization file itself should be a casematched language as defined by one of the "folder" language names from here:
        // https://valheim-modding.github.io/Jotunn/data/localization/language-list.html
        private void AddLocalizations()
        {
            // Use this class to add your own localization to the game
            // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
            CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();
            // load all localization files within the localizations directory
            Jotunn.Logger.LogDebug("Loading Localizations.");
            foreach (string embeddedResouce in typeof(Mistward).Assembly.GetManifestResourceNames())
            {
                if (!embeddedResouce.Contains("Localizations")) { continue; }
                // Read the localization file
                string localization = ReadEmbeddedResourceFile(embeddedResouce);
                // since I use comments in the localization that are not valid JSON those need to be stripped
                string cleaned_localization = Regex.Replace(localization, @"\/\/.*", "");
                // Just the localization name
                var localization_name = embeddedResouce.Split('.');
                Jotunn.Logger.LogDebug($"Adding localization: {localization_name[2]}");
                // Logging some characters seem to cause issues sometimes
                // if (VFConfig.EnableDebugMode.Value == true) { Logger.LogInfo($"Localization Text: {cleaned_localization}"); }
                //Localization.AddTranslation(localization_name[2], localization);
                Localization.AddJsonFile(localization_name[2], cleaned_localization);
            }
        }
    }
}