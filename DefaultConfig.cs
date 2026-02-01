using Sandbox.Definitions;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace SimpleStore.StoreBlock
{
    internal class DefaultConfig
    {
        public const string ConfigSettings = "Settings";
        public const string Resell = "ResellItems"; //deprecated
        public const string SpawnDistance = "SpawnDistance";
        public const string RefreshPeriod = "RefreshPeriodMins";
        public const string RefineYield = "RefineYield";
        public const string DebugLog = "Debug"; //hidden config

        public const int DefaultSpawnDistance = 100;
        public const int DefaultRefreshPeriod = 20; //mins
        public const int MinRefreshPeriod = 375;  // 100's of ticks mins * 37.5
        public const float DefaultRefineYield = 1;

        public static List<string> BlacklistItems = new List<string> { "RestrictedConstruction", "CubePlacerItem", "GoodAIRewardPunishmentTool", "SpaceCredit" };
        public static List<string> WhitelistItems = new List<string> { "NATO_5p56x45mm", "Organic", "Scrap" };


        public static string CreateDefaultConfigString()
        {
            Log.Msg("Start CreateDefaultConfig");
            MyIni config = new MyIni();
            config.Clear();
            config.AddSection(ConfigSettings);
            var sb = new StringBuilder();
            sb.AppendLine("Do not activate too many items, maximum is 30 slots.");
            sb.AppendLine("Auto-generated prices are the Keen minimum, setting lower value will log an error.");
            sb.AppendLine("To force a store refresh, turn store block off wait 3s and turn it on. Auto refresh minimum and default is 20 minutes");
            sb.AppendLine("Config errors will cause the store block to turn off");
            sb.AppendLine("Format is BuyAmount:BuyPrice,SellAmount:SellPrice");
            config.SetSectionComment(ConfigSettings, sb.ToString());

            //config.Set(ConfigSettings, Resell, false);
            config.Set(ConfigSettings, SpawnDistance, DefaultSpawnDistance);
            config.Set(ConfigSettings, RefreshPeriod, DefaultRefreshPeriod);
            config.Set(ConfigSettings, RefineYield, DefaultRefineYield);

            config.AddSection("Ore");
            config.AddSection("Ingot");
            config.AddSection("Component");
            config.AddSection("PhysicalGunObject");
            config.AddSection("AmmoMagazine");
            config.AddSection("OxygenContainerObject");
            config.AddSection("GasContainerObject");
            config.AddSection("ConsumableItem");
            config.AddSection("PhysicalObject");

            string section;
            Match match;

            ItemConfig defaultItemConfig = new ItemConfig();
            string subtypeName = "";
            bool ok;
            foreach (var definition in MyDefinitionManager.Static.GetAllDefinitions())
            {
                if (BlacklistItems.Contains(definition.Id.SubtypeName))
                    continue;

                subtypeName = FixKey(definition.Id.SubtypeName);
                match = Regex.Match(definition.Id.TypeId.ToString() + subtypeName, @"[\[\]\r\n|=]");
                if (match.Success)
                    continue;

                section = definition.Id.TypeId.ToString().Remove(0, 16); //remove "MyObjectBuilder_"

                if (config.ContainsSection(section))
                {
                    ok = false;
                    if (!definition.Public && MyDefinitionManager.Static.GetPrefabDefinition(definition.Id.SubtypeName) != null)
                    {
                        ok = true;
                    }
                    if (WhitelistItems.Contains(definition.Id.SubtypeName) || (definition.Public && MyDefinitionManager.Static.GetPhysicalItemDefinition(definition.Id).CanPlayerOrder))
                    {
                        ok = true;
                    }
                    if (ok)
                    {
                        defaultItemConfig.SetDefaultPrices(definition.Id);
                        config.Set(section, subtypeName, defaultItemConfig.ToString());
                        continue;
                    }
                    Log.Msg($"skipping {subtypeName} public={definition.Public}");
                }
            }

            config.Invalidate();
            return config.ToStringSorted();
        }


        public static string FixKey(string key)
        {
            return key.Replace('[', '{').Replace(']', '}'); // Replace [ ]  with { } for mods like Better Stone
        }
    }
}
