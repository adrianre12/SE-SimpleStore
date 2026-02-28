using Sandbox.Definitions;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SimpleStore
{
    partial class Onezer
    {
        const string AllDefinitionsFile = "AllDefinitions.txt";
        const string ExamplePriceFile = "ExamplePrices.txt";
        const string PriceFile = "Prices.txt";

        private static List<string> BlacklistTypes = new List<string> { "MyObjectBuilder_TreeObject" };
        private void CreateDefaultPriceFiles()
        {
            StringBuilder sbAll = new StringBuilder();
            StringBuilder sbPrice = new StringBuilder();
            string subtypeName;
            string itemType;

            foreach (var definition in MyDefinitionManager.Static.GetAllDefinitions().OfType<MyPhysicalItemDefinition>())
            {
                if (Log.Debug) Log.Msg($"Found {definition.ToString()}");
                itemType = definition.Id.TypeId.ToString();
                subtypeName = definition.Id.SubtypeName;

                if (Regex.Match(itemType + subtypeName, @"[\[\]\r\n|=]").Success)
                {
                    Log.Msg($"Contains '[]\r\n|=' Skipping  {itemType}/{subtypeName}");
                    continue;
                }
                if (BlacklistTypes.Contains(itemType))
                {
                    if (Log.Debug) Log.Msg($"Skipping Blacklisted {itemType}");
                    continue;
                }

                string line = $"{itemType}/{subtypeName}={definition.MinimalPricePerUnit}";
                sbAll.AppendLine(line);
                if (definition.MinimalPricePerUnit > 0)
                    sbPrice.AppendLine(line);
            }
            try
            {
                using (var allWriter = MyAPIGateway.Utilities.WriteFileInWorldStorage(AllDefinitionsFile, typeof(Onezer)))
                {
                    allWriter.Write(sbAll.ToString());
                }
            }
            catch (Exception exc)
            {
                Log.Msg($"ERROR: Could Not Create {AllDefinitionsFile}.\n{exc.ToString()}");
            }
            try
            {
                using (var priceWriter = MyAPIGateway.Utilities.WriteFileInWorldStorage(ExamplePriceFile, typeof(Onezer)))
                {
                    priceWriter.Write(sbPrice.ToString());
                }
            }
            catch (Exception exc)
            {
                Log.Msg($"ERROR: Could Not Create {ExamplePriceFile}.\n{exc.ToString()}");
            }

        }

        private void LoadPriceFile()
        {
            Log.Msg("Loading Prices file");
            string line;
            string[] parts;
            int price;
            Dictionary<string, int> prices = new Dictionary<string, int>();

            if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(PriceFile, typeof(Onezer)))
            {
                Log.Msg($"ERROR: Prices file '{PriceFile}' does not exist.");
                return;
            }

            try
            {
                using (var priceReader = MyAPIGateway.Utilities.ReadFileInWorldStorage(PriceFile, typeof(Onezer)))
                {
                    line = priceReader.ReadLine();
                    while (line != null)
                    {
                        if (Log.Debug) Log.Msg($"Read line {line}");
                        parts = line.Split('=');
                        if (parts.Length != 2)
                        {
                            Log.Msg($"ERROR: Could not split line {line}");
                            continue;
                        }
                        if (!int.TryParse(parts[1], out price))
                        {
                            Log.Msg($"ERROR: Could not convert price {parts[1]}");
                            continue;
                        }
                        prices[parts[0]] = price;
                        if (Log.Debug) Log.Msg($"Loaded {parts[0]}={price}");
                    }
                }
            }
            catch (Exception exc)
            {
                Log.Msg($"ERROR: Reading {PriceFile}.\n{exc.ToString()}");
            }

            var allDefs = MyDefinitionManager.Static.GetAllDefinitions();
            foreach (var physicalItem in allDefs.OfType<MyPhysicalItemDefinition>())
            {

                if (Log.Debug) Log.Msg($"Setting '{physicalItem.Id} from {physicalItem.MinimalPricePerUnit} to {config.MinimalPricePerUnit}");
                physicalItem.MinimalPricePerUnit = config.MinimalPricePerUnit;
            }
        }
    }
}
