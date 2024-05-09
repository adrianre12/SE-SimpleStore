using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Localization;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.ModAPI;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace SimpleStore.StoreBlock
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_StoreBlock), false, "SimpleStoreBlock")]
    public class IngameStoreBlockGameLogic : MyGameLogicComponent
    {
        const string ConfigSettings = "Settings";

        List<string> BlacklistItems = new List<string>{ "Stone", "Ice", "Scrap", "Organic", "CubePlacerItem", "GoodAIRewardPunishmentTool" };

        IMyStoreBlock myStoreBlock;
        MyIni config = new MyIni();

        bool lastBlockEnabledState = false;
        bool UpdateShop = true;
        int UpdateCounter = 0;

        List<IMyPlayer> Players = new List<IMyPlayer>();
        List<Sandbox.ModAPI.Ingame.MyStoreQueryItem> StoreItems = new List<Sandbox.ModAPI.Ingame.MyStoreQueryItem>();

 //       Dictionary<MyDefinitionId, int> ComponentMinimalPrice = new Dictionary<MyDefinitionId, int>();
 //       Dictionary<MyDefinitionId, int> BlockMinimalPrice = new Dictionary<MyDefinitionId, int>();

        class ItemConfig
        {
            public class StoreItem
            {
                public int Count = 0;
                public int Price = 0;

                public override string ToString()
                {
                    return $"{Count}:{Price}";
                }

                public bool TryParse(string raw)
                {
                    string[] countPrice = raw.Split(':');
                    if (countPrice.Length !=2)
                        return false;

                    if (!int.TryParse(countPrice[0], out this.Count))
                        return false;

                    if (!int.TryParse(countPrice[1], out this.Price))
                        return false;

                    return true;
                }
            }

            const string errString = "<Error";

            public StoreItem Buy;
            public StoreItem Sell;
            bool error = false;
            bool removeError = false;
            string raw;

            public ItemConfig() 
            {
                Buy = new StoreItem();
                Sell = new StoreItem();
                raw = "";
            }

            public bool TryParse(string raw)
            {
                this.raw = raw;
                this.error = true;
                string[] buySell = raw.Split(',');
                if(buySell.Length < 2) 
                    return false;

                if (!this.Buy.TryParse(buySell[0]))
                    return false;

                if (!this.Sell.TryParse(buySell[1]))
                    return false;

                if (buySell.Length >2 && buySell[2].Contains(errString))
                {
                    this.removeError = true;
                }

                this.error = false;
                return true;
            }

            public override string ToString()
            {
                if (this.error)
                {
                    if (this.raw.Contains(errString))
                        return raw;
                    return $"{raw},{errString}";
                }
                return $"{Buy},{Sell}";
            }

            public bool IsRemoveError()
            {
                return this.removeError;
            }
        }


        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;

            myStoreBlock = Entity as IMyStoreBlock;

            if (!MyAPIGateway.Session.IsServer)
                return;

            MyLog.Default.WriteLine("SimpleStore.StoreBlock: loaded...");
        }

        public override void UpdateAfterSimulation100()
        {
            if (myStoreBlock.CustomData == "")
                CreateConfig();

            if (!MyAPIGateway.Session.IsServer)
                return;

            if (myStoreBlock.Enabled != lastBlockEnabledState)
            {
                lastBlockEnabledState = myStoreBlock.Enabled;

                if(!TryLoadConfig())
                    return;

                UpdateShop = true;
            }

            if (!myStoreBlock.IsWorking)
                return;

            UpdateCounter++;

            if (!UpdateShop && UpdateCounter <= 750)
                return;

            myStoreBlock.GetPlayerStoreItems(StoreItems);

            foreach (var item in StoreItems)
            {
                myStoreBlock.CancelStoreItem(item.Id);
            }

            Match match;
            string rawIniValue;
            ItemConfig itemConfig = new ItemConfig();

            foreach (var definition in MyDefinitionManager.Static.GetAllDefinitions())
            {
                var typeId = definition.Id.TypeId.ToString();

                match = Regex.Match(typeId + definition.Id.SubtypeName, @"[\[\]\r\n|=]");

                if (match.Success)
                    continue;

                var currentInvItemAmount = MyVisualScriptLogicProvider.GetEntityInventoryItemAmount(myStoreBlock.Name, definition.Id);
                MyVisualScriptLogicProvider.RemoveFromEntityInventory(myStoreBlock.Name, definition.Id, currentInvItemAmount);

                if (!config.ContainsSection(typeId) || !config.ContainsKey(typeId, definition.Id.SubtypeName))
                    continue;

                if (!config.Get(typeId, definition.Id.SubtypeName).TryGetString(out rawIniValue))
                    continue;

                if (!itemConfig.TryParse(rawIniValue))
                {
                    myStoreBlock.Enabled = false;
                    MyLog.Default.WriteLine("SimpleStore.StoreBlock: Config error in Custom Data.");

                    continue;
                }

                var prefab = MyDefinitionManager.Static.GetPrefabDefinition(definition.Id.SubtypeName);

                var result = Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success;

                long id;
                MyStoreItemData itemData;

                //buy
                if (prefab == null && itemConfig.Buy.Count > 0)
                {
                    itemData = new MyStoreItemData(definition.Id, itemConfig.Buy.Count, itemConfig.Buy.Price, null, null);
                    result = myStoreBlock.InsertOrder(itemData, out id);
                }

                if (result != Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success)
                {
                    MyLog.Default.WriteLine("SimpleStore.StoreBlock: result " + result);
                    break;
                }

                //sell
                {
                    itemData = new MyStoreItemData(definition.Id, itemConfig.Sell.Count, itemConfig.Sell.Price,
                        (amount, left, totalPrice, sellerPlayerId, playerId) => OnTransaction(amount, left, totalPrice, sellerPlayerId, playerId, definition), null);
                    result = myStoreBlock.InsertOffer(itemData, out id);

                    if (result == Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success)
                    {
                        MyVisualScriptLogicProvider.AddToInventory(myStoreBlock.Name, definition.Id, itemConfig.Sell.Count);
                    }
                }
            }

            UpdateCounter = 0;
            UpdateShop = false;
        }

        private void OnTransaction(int amountSold, int amountRemaining, long priceOfTransaction, long ownerOfBlock, long buyerSeller, MyDefinitionBase compDef)
        {
            var prefab = MyDefinitionManager.Static.GetPrefabDefinition(compDef.Id.SubtypeName);

            if (prefab != null)
            {
                Players.Clear();
                MyAPIGateway.Multiplayer.Players.GetPlayers(Players);

                IMyPlayer player = Players.FirstOrDefault(_player => _player.IdentityId == buyerSeller);

                if  (player != null)
                {
                    var inventory = player.Character.GetInventory();
                    var item = inventory.FindItem(compDef.Id);

                    if (item != null)
                    {
                        inventory.RemoveItemAmount(item, item.Amount);
                    }
                    else
                    {
                        player.RequestChangeBalance(priceOfTransaction);
                        MyLog.Default.WriteLine("SimpleStore.StoreBlock: Error can't find Prefab Item (no Spawn)");
                        return;
                    }

                    MyEntity mySafeZone = null;
                    Vector3D spawnPos;
                    float naturalGravityInterference;

                    var spawningOptions = SpawningOptions.RotateFirstCockpitTowardsDirection | SpawningOptions.SetAuthorship | SpawningOptions.UseOnlyWorldMatrix;
                    var position = player.GetPosition() + (player.Character.LocalMatrix.Forward * 100);

                    MyAPIGateway.Physics.CalculateNaturalGravityAt(position, out naturalGravityInterference);

                    foreach (var safeZone in MySessionComponentSafeZones.SafeZones)
                    {
                        if (safeZone == null || myStoreBlock == null)
                            continue;

                        if (safeZone.PositionComp.WorldAABB.Contains(myStoreBlock.PositionComp.WorldAABB) == ContainmentType.Contains)
                        {
                            mySafeZone = safeZone;
                            break;
                        }
                    }

                    if (naturalGravityInterference != 0f)
                    {
                        var planet = MyGamePruningStructure.GetClosestPlanet(position);
                        var surfacePosition = planet.GetClosestSurfacePointGlobal(position);
                        var upDir = Vector3D.Normalize(surfacePosition - planet.PositionComp.GetPosition());
                        var forwardDir = Vector3D.CalculatePerpendicularVector(upDir);

                        spawnPos = (Vector3D)MyEntities.FindFreePlace(surfacePosition, prefab.BoundingSphere.Radius, ignoreEnt: mySafeZone);

                        MyVisualScriptLogicProvider.SpawnPrefabInGravity(compDef.Id.SubtypeName, spawnPos, forwardDir, ownerId: player.IdentityId, spawningOptions: spawningOptions);
                    }
                    else
                    {
                        spawnPos = (Vector3D)MyEntities.FindFreePlace(position, prefab.BoundingSphere.Radius, ignoreEnt: mySafeZone);

                        MyVisualScriptLogicProvider.SpawnPrefab(compDef.Id.SubtypeName, spawnPos, Vector3D.Forward, Vector3D.Up, ownerId: player.IdentityId, spawningOptions: spawningOptions);
                    }

                    MyVisualScriptLogicProvider.AddGPS(compDef.Id.SubtypeName, compDef.Id.SubtypeName, spawnPos, Color.Green, disappearsInS: 0, playerId: player.IdentityId);
                }
            }
        }

        /*private void CalculateItemMinimalPrice(MyDefinitionId itemId, float baseCostProductionSpeedMultiplier, ref int minimalPrice)
        {
            minimalPrice = 0;
            MyPhysicalItemDefinition myPhysicalItemDefinition;
            if (MyDefinitionManager.Static.TryGetDefinition(itemId, out myPhysicalItemDefinition) && myPhysicalItemDefinition.MinimalPricePerUnit != -1)
            {
                minimalPrice += myPhysicalItemDefinition.MinimalPricePerUnit;
                return;
            }
            MyBlueprintDefinitionBase myBlueprintDefinitionBase;
            if (!MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(itemId, out myBlueprintDefinitionBase))
            {
                return;
            }
            float num = myPhysicalItemDefinition.IsIngot ? 1f : MyAPIGateway.Session.AssemblerEfficiencyMultiplier;
            int num2 = 0;
            foreach (MyBlueprintDefinitionBase.Item item in myBlueprintDefinitionBase.Prerequisites)
            {
                int num3 = 0;
                CalculateItemMinimalPrice(item.Id, baseCostProductionSpeedMultiplier, ref num3);
                float num4 = (float)item.Amount / num;
                num2 += (int)((float)num3 * num4);
            }
            float num5 = myPhysicalItemDefinition.IsIngot ? MyAPIGateway.Session.RefinerySpeedMultiplier : MyAPIGateway.Session.AssemblerSpeedMultiplier;
            for (int j = 0; j < myBlueprintDefinitionBase.Results.Length; j++)
            {
                MyBlueprintDefinitionBase.Item item2 = myBlueprintDefinitionBase.Results[j];
                if (item2.Id == itemId)
                {
                    float num6 = (float)item2.Amount;
                    if (num6 != 0f)
                    {
                        float num7 = 1f + (float)Math.Log((double)(myBlueprintDefinitionBase.BaseProductionTimeInSeconds + 1f)) * baseCostProductionSpeedMultiplier / num5;
                        minimalPrice += (int)((float)num2 * (1f / num6) * num7);
                        return;
                    }
                }
            }
        }*/

        /*    public void CalculatePrefabMinimalPrice(string prefabName, float baseCostProductionSpeedMultiplier, ref int minimalPrice)
            {
                minimalPrice = 0;
                int num = 0;
                MyPrefabDefinition prefabDefinition = MyDefinitionManager.Static.GetPrefabDefinition(prefabName);
                if (prefabDefinition != null && prefabDefinition.CubeGrids != null && prefabDefinition.CubeGrids.Length != 0 && !string.IsNullOrEmpty(prefabDefinition.CubeGrids[0].DisplayName))
                {
                    MyObjectBuilder_CubeGrid[] cubeGrids = prefabDefinition.CubeGrids;
                    for (int j = 0; j < cubeGrids.Length; j++)
                    {
                        foreach (MyObjectBuilder_CubeBlock myObjectBuilder_CubeBlock in cubeGrids[j].CubeBlocks)
                        {
                            MyDefinitionId myDefinitionId = new MyDefinitionId(myObjectBuilder_CubeBlock.TypeId, myObjectBuilder_CubeBlock.SubtypeName);
                            if (!BlockMinimalPrice.TryGetValue(myDefinitionId, out num))
                            {
                                CalculateBlockMinimalPrice(myDefinitionId, baseCostProductionSpeedMultiplier, ref num);
                            }

                            minimalPrice += num;
                        }
                    }
                }
            }*/

        /* private void CalculateBlockMinimalPrice(MyDefinitionId blockId, float baseCostProductionSpeedMultiplier, ref int minimalPrice)
         {
             minimalPrice = 0;
             MyCubeBlockDefinition myCubeBlockDefinition;
             if (!MyDefinitionManager.Static.TryGetCubeBlockDefinition(blockId, out myCubeBlockDefinition))
             {
                 return;
             }

             foreach (MyCubeBlockDefinition.Component component in myCubeBlockDefinition.Components)
             {
                 int num = 0;
                 if (!ComponentMinimalPrice.TryGetValue(component.Definition.Id, out num))
                 {
                     CalculateItemMinimalPrice(component.Definition.Id, baseCostProductionSpeedMultiplier, ref num);
                     ComponentMinimalPrice[component.Definition.Id] = num;
                 }
                 minimalPrice += num * component.Count;
             }
         }*/

        /*
           foreach (var definition in MyDefinitionManager.Static.GetAllDefinitions())
            {
                var maxAmount = 0;
                var typeId = definition.Id.TypeId.ToString();

                match = Regex.Match(typeId + definition.Id.SubtypeName, @"[\[\]\r\n|=]");

                if (match.Success)
                    continue;

                var currentInvItemAmount = MyVisualScriptLogicProvider.GetEntityInventoryItemAmount(myStoreBlock.Name, definition.Id);
                MyVisualScriptLogicProvider.RemoveFromEntityInventory(myStoreBlock.Name, definition.Id, currentInvItemAmount);

                if (!config.ContainsSection(typeId) || !config.ContainsKey(typeId, definition.Id.SubtypeName))
                    continue;

                if (!config.Get(typeId, definition.Id.SubtypeName).ToBoolean())
                    continue;

                var prefab = MyDefinitionManager.Static.GetPrefabDefinition(definition.Id.SubtypeName);

                if (definition.Id.TypeId == typeof(MyObjectBuilder_Component) || definition.Id.TypeId == typeof(MyObjectBuilder_Ore)
                    || definition.Id.TypeId == typeof(MyObjectBuilder_Ingot))
                {
                    maxAmount = prefab == null ? config.Get(ConfigSettings, ConfigComponent).ToInt32() : config.Get(ConfigSettings, ConfigShip).ToInt32();
                }
                else if (definition.Id.TypeId == typeof(MyObjectBuilder_AmmoMagazine))
                {
                    maxAmount = config.Get(ConfigSettings, ConfigAmmo).ToInt32();
                }
                else if (definition.Id.TypeId == typeof(MyObjectBuilder_PhysicalGunObject) || definition.Id.TypeId == typeof(MyObjectBuilder_OxygenContainerObject)
                    || definition.Id.TypeId == typeof(MyObjectBuilder_GasContainerObject) || definition.Id.TypeId == typeof(MyObjectBuilder_ConsumableItem))
                {
                    maxAmount = config.Get(ConfigSettings, ConfigCharacter).ToInt32();
                }

                var minimalPrice = 0;
                var result = Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success;
                var orderOrOffer = random.Next(0, 3);

                if (prefab == null)
                {
                    CalculateItemMinimalPrice(definition.Id, 1f, ref minimalPrice);
                }
                else
                {
                    CalculatePrefabMinimalPrice(prefab.Id.SubtypeName, 1f, ref minimalPrice);
                }

                long id;
                MyStoreItemData itemData;

                var itemAmount = random.Next(1, Math.Max(maxAmount + 1, 1));
                var itemPrice = (int)Math.Round(minimalPrice * ((random.Next(5000, 15001) / 100000.0f) + 1.0f));

                if (orderOrOffer == 0)
                {
                    itemData = new MyStoreItemData(definition.Id, itemAmount, itemPrice,
                        (amount, left, totalPrice, sellerPlayerId, playerId) => OnTransaction(amount, left, totalPrice, sellerPlayerId, playerId, definition), null);
                    result = myStoreBlock.InsertOffer(itemData, out id);

                    if (result == Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success)
                    {
                        MyVisualScriptLogicProvider.AddToInventory(myStoreBlock.Name, definition.Id, itemAmount);
                    }
                }
                else if (prefab == null && orderOrOffer == 2)
                {
                    itemData = new MyStoreItemData(definition.Id, itemAmount, (int)Math.Round(itemPrice * 0.7), null, null);
                    result = myStoreBlock.InsertOrder(itemData, out id);
                }

                if (result != Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success)
                {
                    MyLog.Default.WriteLine("SimpleStore.StoreBlock: result " + result);
                    break;
                }
            }

            UpdateCounter = 0;
            UpdateShop = false;
         */

        private void CreateConfig()
        {
            config.AddSection(ConfigSettings);
            config.SetSectionComment(ConfigSettings, "Do not activate too many objects the store has a limited number of slots");

            config.AddSection("MyObjectBuilder_Ore");
            config.AddSection("MyObjectBuilder_Ingot");
            config.AddSection("MyObjectBuilder_PhysicalGunObject");
            config.AddSection("MyObjectBuilder_AmmoMagazine");
            config.AddSection("MyObjectBuilder_Component");
            config.AddSection("MyObjectBuilder_OxygenContainerObject");
            config.AddSection("MyObjectBuilder_GasContainerObject");
            config.AddSection("MyObjectBuilder_ConsumableItem");

            string typeId;
            Match match;

            string defaultItemConfig = new ItemConfig().ToString();

            foreach (var definition in MyDefinitionManager.Static.GetAllDefinitions())
            {
                if (BlacklistItems.Contains(definition.Id.SubtypeName))
                    continue;

                typeId = definition.Id.TypeId.ToString();
                match = Regex.Match(typeId + definition.Id.SubtypeName, @"[\[\]\r\n|=]");

                if (!match.Success && config.ContainsSection(typeId))
                    config.Set(typeId, definition.Id.SubtypeName, defaultItemConfig);
            }

            config.Invalidate();
            myStoreBlock.CustomData = config.ToString();
        }

        private bool TryLoadConfig()
        {
            bool configOK = true;
            bool updateConfig = false;

            if (config.TryParse(myStoreBlock.CustomData))
            {
                if (!config.ContainsSection(ConfigSettings))
                    configOK = false;

                List<string> sections = new List<string>();
                List<MyIniKey> keys = new List<MyIniKey>();
                ItemConfig itemConfig = new ItemConfig();
                string rawIniValue;

                config.GetSections(sections);
                foreach ( var section in sections)
                {
                    config.GetKeys(section, keys);
                    foreach ( var key in keys)
                    {
                        if(!config.Get(section,key.Name).TryGetString(out rawIniValue)){
                            configOK = false;
                            MyLog.Default.WriteLine($"SimpleStore.StoreBlock: Failed to get {section}:{key.Name}");
                            break;
                        }
                        if (!itemConfig.TryParse(rawIniValue))
                        {
                            config.Set(section, key.Name, itemConfig.ToString());
                            updateConfig = true;
                            myStoreBlock.Enabled = false;

                            continue;
                        }

                        if (itemConfig.IsRemoveError())
                        {
                            config.Set(section, key.Name, itemConfig.ToString());
                            updateConfig = true;
                        }
                    }
                }

                if (updateConfig)
                {
                    config.Invalidate();
                    myStoreBlock.CustomData = config.ToString();
                    TryLoadConfig();
                }

                if (!configOK)
                {
                    MyLog.Default.WriteLine("SimpleStore.StoreBlock: Config Value error");
                }
                
            }
            else
            {
                configOK = false;
                MyLog.Default.WriteLine("SimpleStore.StoreBlock: Config Syntax error");
            }
            return configOK;
        }
    }
}