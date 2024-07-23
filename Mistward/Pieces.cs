using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using Logger = Jotunn.Logger;
using UnityEngine;
using Jotunn.Configs;
using Jotunn;

namespace Mistward
{
    class ValheimPieces
    {
        public ValheimPieces(AssetBundle EmbeddedResourceBundle, Config config)
        {
            if (Config.EnableDebugMode.Value) { Logger.LogInfo("Loading Pieces."); }
            LoadMistward(EmbeddedResourceBundle, config);
        }

        private void LoadMistward(AssetBundle EmbeddedResourceBundle, Config config)
        {
            // Alter of Challenge
            new ValheimPiece(
                EmbeddedResourceBundle,
                config,
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

        class ValheimPiece
        {
            private static ParticleSystemForceField mistward_pushfield;
            private static string prefabname;
            String[] allowed_catagories = { "Furniture", "Building", "Crafting", "Misc" };
            String[] crafting_stations = { "forge", "piece_workbench", "blackforge", "piece_artisanstation", "piece_stonecutter" };

            /// <summary>
            /// 
            /// </summary>
            /// <param name="EmbeddedResourceBundle"> The embedded assets</param>
            /// <param name="cfg"> config file to add things to</param>
            /// <param name="metadata">Key(string)-Value(string) dictionary of item metadata eg: "name" = "Green Metal Arrow"</param>
            /// <param name="itemdata">Key(string)-Value(Tuple) dictionary of item metadata with config metadata eg: "blunt" = < 15(value), 0(min), 200(max), true(cfg_enable_flag) > </param>
            /// <param name="itemtoggles">Key(string)-Value(bool) dictionary of true/false config toggles for this item.</param>
            /// <param name="recipedata">Key(string)-Value(Tuple) dictionary of recipe requirements (limit 4) eg: "SerpentScale" = < 3(creation requirement), 2(levelup requirement)> </param>
            public ValheimPiece(
                AssetBundle EmbeddedResourceBundle,
                Config config,
                Dictionary<String, String> metadata,
                Dictionary<String, bool> piecetoggle,
                Dictionary<String, Tuple<int, bool>> recipedata
                )
            {
                // Validate inputs are valid
                if (!allowed_catagories.Contains(metadata["catagory"])) { throw new ArgumentException($"Catagory {metadata["catagory"]} must be an allowed catagory: {allowed_catagories}"); }
                if (!metadata.ContainsKey("name")) { throw new ArgumentException($"Item must have a name"); }
                if (!metadata.ContainsKey("prefab")) { throw new ArgumentException($"Item must have a prefab"); }
                if (!metadata.ContainsKey("sprite")) { throw new ArgumentException($"Item must have a sprite"); }
                if (!metadata.ContainsKey("requiredBench")) { throw new ArgumentException($"Item must have a requiredBench"); }
                if (!piecetoggle.ContainsKey("enabled")) { piecetoggle.Add("enabled", true); }
                // needed metadata - item name without spaces
                metadata["short_item_name"] = string.Join("", metadata["name"].Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
                prefabname = metadata["prefab"];

                // create config
                if (Config.EnableDebugMode.Value) { Logger.LogInfo($"Creating Configuration Values for {metadata["name"]}"); }
                CreateAndLoadConfigValues(config, metadata, piecetoggle, recipedata);

                

                // If the item is not enabled we do not load it
                if (piecetoggle["enabled"] != false)
                {
                    // load assets
                    if (Config.EnableDebugMode.Value)
                    {
                        Logger.LogInfo($"Loading bundled assets for {metadata["name"]}");
                        Logger.LogInfo($"Assets/Custom/Pieces/{metadata["catagory"]}/{metadata["prefab"]}.prefab & Assets/Custom/Icons/piece_icons/{metadata["sprite"]}.png");
                    }
                    GameObject prefab = EmbeddedResourceBundle.LoadAsset<GameObject>($"Assets/Custom/Pieces/{metadata["catagory"]}/{metadata["prefab"]}.prefab");
                    Sprite sprite = EmbeddedResourceBundle.LoadAsset<Sprite>($"Assets/Custom/Icons/piece_icons/{metadata["sprite"]}.png");

                    // These are custom unity componet scripts which have never seen the light of unity. So they arn't baked into the assets
                    // and must be added later. This allows these scripts to do things like be modified by config values, or reference Jotunn
                    mistward_pushfield = prefab.FindDeepChild("Particle_System_Force_Field").GetComponent<ParticleSystemForceField>();
                    mistward_pushfield.endRange = Config.MistwardRange.Value;
                    Config.MistwardRange.SettingChanged += MistwardRangeChange;

                    // Add the recipe with helper
                    if (Config.EnableDebugMode.Value) { Logger.LogInfo($"Loading {metadata["name"]} updated Recipe."); }
                    RequirementConfig[] recipe = new RequirementConfig[recipedata.Count];
                    int recipe_index = 0;
                    foreach (KeyValuePair<string, Tuple<int, bool>> entry in recipedata)
                    {
                        recipe[recipe_index] = new RequirementConfig { Item = entry.Key, Amount = entry.Value.Item1, Recover = entry.Value.Item2 };
                        recipe_index++;
                    }
                    if (Config.EnableDebugMode.Value) { Logger.LogInfo($"Building Piececonfig for {metadata["name"]}."); }
                    PieceConfig piececfg = new PieceConfig()
                    {
                        CraftingStation = $"{metadata["requiredBench"]}",
                        PieceTable = PieceTables.Hammer,
                        Category = metadata["catagory"],
                        Icon = sprite,
                        Requirements = recipe
                    };
                    PieceManager.Instance.AddPiece(new CustomPiece(prefab, fixReference: true, piececfg));
                    if (Config.EnableDebugMode.Value) { Logger.LogInfo($"Piece {metadata["name"]} Added!"); }
                }
                else
                {
                    if (Config.EnableDebugMode.Value) { Logger.LogInfo($"{metadata["name"]} is not enabled, and was not loaded."); }
                }
            }

            private void MistwardRangeChange(object sender, EventArgs e)
            {
                // Update the original
                mistward_pushfield.endRange = Config.MistwardRange.Value;

                // Update all that are visible/loaded
                // Get and update all of the in-scene game objects
                IEnumerable<GameObject> objects = Resources.FindObjectsOfTypeAll<GameObject>().Where(obj => obj.name == prefabname);
                if (Config.EnableDebugMode.Value) { Logger.LogInfo($"Found in scene objects: {objects.Count()}"); }
                foreach (GameObject go in objects)
                {
                    if (Config.EnableDebugMode.Value) { Logger.LogInfo($"Found {go.name}"); }
                    ParticleSystemForceField force_system = null;
                    go.FindDeepChild("Particle_System_Force_Field").TryGetComponent<ParticleSystemForceField>(out force_system);
                    if (force_system != null)
                    {
                        if (Config.EnableDebugMode.Value) { Logger.LogInfo($"{go.name} updating forcefield {Config.MistwardRange.Value}"); }
                        // For some reason the child reference is not updating the in scene one
                        force_system.endRange = Config.MistwardRange.Value;

                    }
                }
            }

            /// <summary>
            ///  Creates configuration values with automated segmentation on type
            /// </summary>
            /// <param name="config"></param>
            /// <param name="metadata"></param>
            /// <param name="itemdata"></param>
            /// <param name="itemtoggles"></param>
            private void CreateAndLoadConfigValues(Config config, Dictionary<String, String> metadata, Dictionary<String, bool> piecetoggle, Dictionary<String, Tuple<int, bool>> recipedata)
            {
                piecetoggle["enabled"] = config.BindServerConfig($"{metadata["catagory"]} - {metadata["name"]}", $"{metadata["short_item_name"]}-Enable", piecetoggle["enabled"], $"Enable/Disable the {metadata["name"]}.").Value;

                // Item bolean flag configs
                foreach (KeyValuePair<string, bool> entry in piecetoggle)
                {
                    if (entry.Key == "enabled") { continue; }
                    piecetoggle[entry.Key] = config.BindServerConfig($"{metadata["catagory"]} - {metadata["name"]}", $"{metadata["short_item_name"]}-{entry.Key}", entry.Value, $"{entry.Key} enable(true)/disable(false).", true).Value;
                }
                // Recipe Configs
                String recipe_cfg = "";
                foreach (KeyValuePair<string, Tuple<int, bool>> entry in recipedata)
                {
                    if (recipe_cfg.Length > 0) { recipe_cfg += "|"; }
                    recipe_cfg += $"{entry.Key},{entry.Value.Item1},{entry.Value.Item2}";
                }
                String RawRecipe;
                RawRecipe = config.BindServerConfig($"{metadata["catagory"]} - {metadata["name"]}", $"{metadata["short_item_name"]}-recipe", recipe_cfg, $"Recipe to craft, Find item ids: https://valheim.fandom.com/wiki/Item_IDs, at most 4 costs. Format: resouce_id,craft_cost-recover_flag eg: Wood,8,false|Iron,12,true", true).Value;
                if (Config.EnableDebugMode.Value) { Logger.LogInfo($"recieved rawrecipe data: '{RawRecipe}'"); }
                String[] RawRecipeEntries = RawRecipe.Split('|');
                Dictionary<String, Tuple<int, bool>> updated_recipe = new Dictionary<String, Tuple<int, bool>>();
                // we only clear out the default recipe if there is recipe data provided, otherwise we will continue to use the default recipe
                // TODO: Add a sanity check to ensure that recipe formatting is correct
                if (Config.EnableDebugMode.Value) { Logger.LogInfo($"recipe entries: {RawRecipeEntries.Length} : {RawRecipeEntries}"); }
                if (RawRecipeEntries.Length >= 1)
                {
                    foreach (String recipe_entry in RawRecipeEntries)
                    {
                        String[] recipe_segments = recipe_entry.Split(',');
                        bool recovery = true;
                        if (recipe_segments.Length > 2) { recovery = bool.Parse(recipe_segments[2]); }
                        if (Config.EnableDebugMode.Value) { Logger.LogInfo($"Setting recipe requirement: {recipe_segments[0]}={recipe_segments[1]} recover={recovery}"); }
                        // Add a sanity check to ensure the prefab we are trying to use exists
                        // This is likely going to need to be late-loaded configuration where we always use the default on modload and then switch the configuration values defined by the user
                        // closer the game init, this will allow setting prefabs/crafting stations that are outside of the scope of thsi mod. Will need more sanity checks.

                        updated_recipe.Add(recipe_segments[0], Tuple.Create(Int32.Parse(recipe_segments[1]), recovery));
                    }
                    recipedata.Clear();
                    foreach (KeyValuePair<string, Tuple<int, bool>> entry in updated_recipe)
                    {
                        if (Config.EnableDebugMode.Value) { Logger.LogInfo($"Updated recipe: resouce: {entry.Key} build: {entry.Value.Item1} recovery: {entry.Value.Item2}"); }
                        recipedata.Add(entry.Key, entry.Value);
                    }
                }
                else
                {
                    Logger.LogWarning($"Configuration '{metadata["catagory"]} - {metadata["name"]} - {metadata["short_item_name"]}-recipe' was invalid and will be ignored, the default will be used.");
                }
            }

        }
    }
}
