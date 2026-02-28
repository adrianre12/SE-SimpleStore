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
            string itemType;
            string defId;

            foreach (var definition in MyDefinitionManager.Static.GetAllDefinitions().OfType<MyPhysicalItemDefinition>())
            {
                defId = definition.ToString();
                if (Log.Debug) Log.Msg($"Found {defId}");

                if (Regex.Match(defId, @"[\[\]\r\n|=]").Success)
                {
                    Log.Msg($"Contains '[]\r\n|=' Skipping  {defId}");
                    continue;
                }

                itemType = definition.Id.TypeId.ToString();
                if (BlacklistTypes.Contains(itemType))
                {
                    if (Log.Debug) Log.Msg($"Skipping Blacklisted {itemType}");
                    continue;
                }

                string line = $"{defId}={definition.MinimalPricePerUnit}";
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
            Log.Msg($"Loading Prices file '{PriceFile}'");
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
                        //if (Log.Debug) Log.Msg($"Read line {line}");
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
                        line = priceReader.ReadLine();
                    }
                }
            }
            catch (Exception exc)
            {
                Log.Msg($"ERROR: Reading {PriceFile}.\n{exc.ToString()}");
            }

            string defId;
            foreach (var definition in MyDefinitionManager.Static.GetAllDefinitions().OfType<MyPhysicalItemDefinition>())
            {
                defId = definition.ToString();
                //if (Log.Debug) Log.Msg($"Found {defId}");

                if (Regex.Match(defId, @"[\[\]\r\n|=]").Success)
                {
                    Log.Msg($"Contains '[]\r\n|=' Skipping  {defId}");
                    continue;
                }

                if (!prices.TryGetValue(defId, out price))
                {
                    //if (Log.Debug) Log.Msg($"Dict prices didnt find {defId}");
                    continue;
                }
                if (price < -1 || price == 0)
                {
                    Log.Msg($"ERROR: Invalid price {defId}={price}");
                    continue;
                }
                if (definition.MinimalPricePerUnit != price)
                    Log.Msg($"Changing '{defId}' from {definition.MinimalPricePerUnit} to {price}");
                else
                    Log.Msg($"No Change '{defId}'");

                definition.MinimalPricePerUnit = price;
            }
        }
    }
}
