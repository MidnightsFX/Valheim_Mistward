using BepInEx.Configuration;
using HarmonyLib;
using Jotunn;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
using Logger = Jotunn.Logger;
using UnityEngine;
using static Piece;

namespace Mistward.common
{
    public class JotunnPiece
    {
        Dictionary<String, String> PieceMetadata;
        Dictionary<String, Tuple<int, bool>> RecipeData;
        Dictionary<String, bool> PieceToggles;

        Dictionary<String, Tuple<int, bool>> UpdatedRecipeData = new Dictionary<string, Tuple<int, bool>>() { };

        GameObject ScenePrefab;

        GameObject PiecePrefab;
        Sprite PieceSprite;

        ConfigEntry<Boolean> EnabledConfig;
        ConfigEntry<String> RecipeConfig;
        ConfigEntry<String> BuiltAt;
        ConfigEntry<String> BuildCategory;

        private static ParticleSystemForceField mistward_pushfield;

        public JotunnPiece(Dictionary<String, String> metadata, Dictionary<string, bool> pieceToggles, Dictionary<String, Tuple<int, bool>> recipedata)
        {
            PieceMetadata = metadata;
            PieceToggles = pieceToggles;
            RecipeData = recipedata;

            // Add the internal short name
            PieceMetadata["short_item_name"] = string.Join("", metadata["name"].Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));

            // Add universal defaults
            if (!PieceToggles.ContainsKey("enabled")) { PieceToggles.Add("enabled", true); }

            // Set asset references
            PiecePrefab = Mistward.EmbeddedResourceBundle.LoadAsset<GameObject>($"Assets/Custom/Pieces/{PieceMetadata["catagory"]}/{PieceMetadata["prefab"]}.prefab");
            PieceSprite = Mistward.EmbeddedResourceBundle.LoadAsset<Sprite>($"Assets/Custom/Icons/{PieceMetadata["sprite"]}.png");

            InitItemConfigs();
            InitialPieceSetup();

            //Mistward specific
            mistward_pushfield = PiecePrefab.FindDeepChild("Particle_System_Force_Field").GetComponent<ParticleSystemForceField>();
            mistward_pushfield.endRange = Config.MistwardRange.Value;
            Config.MistwardRange.SettingChanged += MistwardRangeChange;

            // Find and register this prefab in the scene, for in-place updates.
            PrefabManager.OnPrefabsRegistered += SetSceneParentPrefab;
        }

        private void MistwardRangeChange(object sender, EventArgs e)
        {
            // Update the original
            mistward_pushfield.endRange = Config.MistwardRange.Value;

            // Update all that are visible/loaded
            // Get and update all of the in-scene game objects
            IEnumerable<GameObject> objects = Resources.FindObjectsOfTypeAll<GameObject>().Where(obj => obj.name.StartsWith(PieceMetadata["prefab"]));
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

        private void InitialPieceSetup()
        {
            CreateAndUpdateRecipe();
            BuildCategory = Config.BindServerConfig($"{PieceMetadata["name"]}", $"{PieceMetadata["short_item_name"]}-category", nameof(PieceCategories.Misc), new ConfigDescription("Build piece Category.", PieceCategories.GetAcceptableValueList()));
            BuildCategory.SettingChanged += CraftingCategory_SettingChanged;
            RequirementConfig[] recipe = new RequirementConfig[UpdatedRecipeData.Count];
            int recipe_index = 0;
            foreach (KeyValuePair<string, Tuple<int, bool>> entry in UpdatedRecipeData)
            {
                recipe[recipe_index] = new RequirementConfig { Item = entry.Key, Amount = entry.Value.Item1, Recover = entry.Value.Item2 };
                recipe_index++;
            }

            PieceConfig piececfg = new PieceConfig()
            {
                CraftingStation = $"{PieceMetadata["requiredBench"]}",
                PieceTable = PieceTables.Hammer,
                Category = BuildCategory.Value,
                Icon = PieceSprite,
                Requirements = recipe
            };

            PieceManager.Instance.AddPiece(new CustomPiece(PiecePrefab, fixReference: true, piececfg));
        }

        private void SetSceneParentPrefab()
        {
            IEnumerable<GameObject> scene_parents = Resources.FindObjectsOfTypeAll<GameObject>().Where(obj => obj.name == PieceMetadata["prefab"]);
            if (Config.EnableDebugMode.Value) { Logger.LogInfo($"Found {PieceMetadata["prefab"]} scene parent objects: {scene_parents.Count()}"); }
            ScenePrefab = scene_parents.First();
        }

        private void CreateAndUpdateRecipe()
        {
            // default recipe config
            String recipe_cfg_default = "";
            foreach (KeyValuePair<string, Tuple<int, bool>> entry in RecipeData)
            {
                if (recipe_cfg_default.Length > 0) { recipe_cfg_default += "|"; }
                recipe_cfg_default += $"{entry.Key},{entry.Value.Item1},{entry.Value.Item2}";
            }
            RecipeConfig = Config.BindServerConfig($"{PieceMetadata["name"]}", $"{PieceMetadata["short_item_name"]}-recipe", recipe_cfg_default, $"Recipe to craft and upgrade costs. Find item ids: https://valheim.fandom.com/wiki/Item_IDs, at most 4 costs. Format: resouce_id,craft_cost,upgrade_cost eg: Wood,8,2|Iron,12,4|LeatherScraps,4,0", true);
            if (PieceRecipeConfigUpdater(RecipeConfig.Value, true) == false)
            {
                Logger.LogWarning($"{PieceMetadata["name"]} has an invalid recipe. The default will be used instead.");
                PieceRecipeConfigUpdater(recipe_cfg_default, true);
            }
            RecipeConfig.SettingChanged += BuildRecipeChanged_SettingChanged;
        }

        private void InitItemConfigs()
        {
            // Populate defaults if they don't exist
            EnabledConfig = Config.BindServerConfig($"{PieceMetadata["name"]}", $"{PieceMetadata["short_item_name"]}-enabled", PieceToggles["enabled"], $"Enable/Disable the {PieceMetadata["name"]}.");
            PieceToggles["enabled"] = EnabledConfig.Value;
            EnabledConfig.SettingChanged += BuildRecipeChanged_SettingChanged;

            // Set where the recipe can be crafted
            BuiltAt = Config.BindServerConfig($"{PieceMetadata["name"]}", $"{PieceMetadata["short_item_name"]}-requiredBench", PieceMetadata["requiredBench"], $"The table required to allow building this piece, eg: 'forge', 'piece_workbench', 'blackforge', 'piece_artisanstation'.");
            PieceMetadata["requiredBench"] = BuiltAt.Value;
            BuiltAt.SettingChanged += RequiredBench_SettingChanged;
        }

        private void BuildRecipeChanged_SettingChanged(object sender, EventArgs e)
        {
            if (sender.GetType() == typeof(ConfigEntry<string>))
            {
                ConfigEntry<string> sendEntry = (ConfigEntry<string>)sender;
                if (Config.EnableDebugMode.Value == true) { Logger.LogInfo($"Recieved new piece config {sendEntry.Value}"); }
                // return if its an invalid change
                if (PieceRecipeConfigUpdater(sendEntry.Value) == false) { return; }
            }

            RequirementConfig[] recipe = new RequirementConfig[UpdatedRecipeData.Count];
            int recipe_index = 0;
            if (Config.EnableDebugMode.Value == true) { Logger.LogInfo("Validating and building requirementsConfig"); }
            foreach (KeyValuePair<string, Tuple<int, bool>> entry in UpdatedRecipeData)
            {
                if (PrefabManager.Instance.GetPrefab(entry.Key) == null)
                {
                    if (Config.EnableDebugMode.Value == true) { Logger.LogInfo($"{entry.Key} is not a valid prefab, skipping recipe update."); }
                    return;
                }
                if (Config.EnableDebugMode.Value == true) { Logger.LogInfo($"Checking entry {entry.Key} c:{entry.Value.Item1} r:{entry.Value.Item2}"); }
                recipe[recipe_index] = new RequirementConfig { Item = entry.Key, Amount = entry.Value.Item1, Recover = entry.Value.Item2 };
                recipe_index++;
            }
            if (PieceToggles["enabled"])
            {
                if (Config.EnableDebugMode.Value == true) { Logger.LogInfo("Updating Piece."); }
                Piece.Requirement[] newRequirements = new Piece.Requirement[UpdatedRecipeData.Count];
                int index = 0;
                foreach (var recipe_entry in recipe)
                {
                    //recipe_entry.FixReferences();
                    Piece.Requirement piece_req = new Piece.Requirement();
                    piece_req.m_resItem = PrefabManager.Instance.GetPrefab(recipe_entry.Item.Replace("JVLmock_", ""))?.GetComponent<ItemDrop>();
                    piece_req.m_amount = recipe_entry.Amount;
                    piece_req.m_recover = recipe_entry.Recover;
                    newRequirements[index] = piece_req;
                    //newRequirements[index] = recipe_entry.GetRequirement();
                    index++;
                }
                if (Config.EnableDebugMode.Value == true) { Logger.LogInfo($"Fixed mock requirements {newRequirements.Length}."); }
                ScenePrefab.GetComponent<Piece>().m_resources = newRequirements;
                if (Config.EnableDebugMode.Value == true) { Logger.LogInfo($"New requirements set {ScenePrefab.GetComponent<Piece>().m_resources}."); }
            }
            else
            {
                // Set this piece not craftable
                ScenePrefab.GetComponent<Piece>().m_enabled = false;
            }
        }

        private void RequiredBench_SettingChanged(object sender, EventArgs e)
        {
            if (BuiltAt.Value == "" || BuiltAt.Value == null || BuiltAt.Value.ToLower() == "NONE")
            {
                Logger.LogInfo("Setting required crafting station to none.");
                ScenePrefab.GetComponent<Piece>().m_craftingStation = null;
                return;
            }

            CraftingStation craftable_at = PrefabManager.Instance.GetPrefab(BuiltAt.Value)?.GetComponent<CraftingStation>();
            if (craftable_at == null) 
            {
                Logger.LogWarning($"Required crafting station does not exist or does not have a crafting station componet, check your prefab name ({BuiltAt.Value}).");
                return;
            }

            if (Config.EnableDebugMode.Value == true) { Logger.LogInfo($"Setting crafting station to {BuiltAt.Value}."); }
            ScenePrefab.GetComponent<Piece>().m_craftingStation = craftable_at;
        }

        private void CraftingCategory_SettingChanged(object sender, EventArgs e)
        {
            ScenePrefab.GetComponent<Piece>().m_category = (PieceCategory)Enum.Parse(typeof(PieceCategory), PieceCategories.GetInternalName(BuildCategory.Value));
        }

        private bool PieceRecipeConfigUpdater(String rawrecipe, bool startup = false)
        {
            String[] RawRecipeEntries = rawrecipe.Split('|');
            // Logger.LogInfo($"{RawRecipeEntries.Length} {string.Join(", ", RawRecipeEntries)}");
            Dictionary<String, Tuple<int, bool>> updated_pieceRecipe = new Dictionary<String, Tuple<int, bool>>();
            // we only clear out the default recipe if there is recipe data provided, otherwise we will continue to use the default recipe
            // TODO: Add a sanity check to ensure that recipe formatting is correct
            if (RawRecipeEntries.Length >= 1)
            {
                foreach (String recipe_entry in RawRecipeEntries)
                {
                    //Logger.LogInfo($"{recipe_entry}");
                    String[] recipe_segments = recipe_entry.Split(',');
                    if (recipe_segments.Length != 3)
                    {
                        Logger.LogWarning($"{recipe_entry} is invalid, it does not have enough segments. Proper format is: PREFABNAME,COST,REFUND_BOOL eg: Wood,8,false");
                        return false;
                    }
                    if (Config.EnableDebugMode.Value == true)
                    {
                        String split_segments = "";
                        foreach (String segment in recipe_segments)
                        {
                            split_segments += $" {segment}";
                        }
                        //Logger.LogInfo($"recipe segments: {split_segments} from {recipe_entry}");
                    }
                    // Add a sanity check to ensure the prefab we are trying to use exists
                    if (startup == false)
                    {
                        if (PrefabManager.Instance.GetPrefab(recipe_segments[0]) == null)
                        {
                            Logger.LogWarning($"{recipe_segments[0]} is an invalid prefab and does not exist.");
                            return false;
                        }
                    }
                    if (recipe_segments[0].Length == 0 || recipe_segments[1].Length == 0 || recipe_segments[2].Length == 0)
                    {
                        Logger.LogWarning($"{recipe_entry} is invalid, one segment does not have enough data. Proper format is: PREFABNAME,CRAFT_COST,REFUND_BOOL eg: Wood,8,false");
                        return false;
                    }
                    bool refund_flag_parse;
                    if (bool.TryParse(recipe_segments[2], out refund_flag_parse) == false)
                    {
                        Logger.LogWarning($"{recipe_entry} is invalid, the REFUND_BOOL could not be parsed to (true/false). Proper format is: PREFABNAME,CRAFT_COST,REFUND_BOOL eg: Wood,8,false");
                        return false;
                    }

                    if (Config.EnableDebugMode.Value == true)
                    {
                        Logger.LogInfo($"prefab: {recipe_segments[0]} c:{recipe_segments[1]} u:{recipe_segments[2]}");
                    }
                    updated_pieceRecipe.Add(recipe_segments[0], new Tuple<int, bool>(Int32.Parse(recipe_segments[1]), refund_flag_parse));
                }
                //Logger.LogInfo("Done parsing recipe");
                UpdatedRecipeData.Clear();
                foreach (KeyValuePair<string, Tuple<int, bool>> entry in updated_pieceRecipe)
                {
                    UpdatedRecipeData.Add(entry.Key, entry.Value);
                }
                //Logger.LogInfo("Set UpdatedRecipe");
                if (Config.EnableDebugMode.Value == true)
                {
                    String recipe_string = "";
                    foreach (KeyValuePair<string, Tuple<int, bool>> entry in updated_pieceRecipe)
                    {
                        recipe_string += $" {entry.Key} c:{entry.Value.Item1} r:{entry.Value.Item2}";
                    }
                    Logger.LogInfo($"Updated recipe:{recipe_string}");
                }
                return true;
            }
            else
            {
                Logger.LogWarning($"Invalid recipe: {rawrecipe}. defaults will be used. Check your prefab names.");
                UpdatedRecipeData = RecipeData;

            }
            return false;
        }
    }
}
