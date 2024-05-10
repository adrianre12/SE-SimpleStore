using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace SimpleStore.StoreBlock
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_StoreBlock), false, "SimpleStoreBlock")]
    public class IngameStoreBlockGameLogic : MyGameLogicComponent
    {
        const string ConfigSettings = "Settings";
        const string Resell = "ResellItems";

        List<string> BlacklistItems = new List<string> { "CubePlacerItem", "GoodAIRewardPunishmentTool" };

        IMyStoreBlock myStoreBlock;
        MyIni config = new MyIni();

        bool lastBlockEnabledState = false;
        bool UpdateShop = true;
        int UpdateCounter = 0;
        bool resellItems = false;

        List<IMyPlayer> Players = new List<IMyPlayer>();
        List<Sandbox.ModAPI.Ingame.MyStoreQueryItem> StoreItems = new List<Sandbox.ModAPI.Ingame.MyStoreQueryItem>();

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

                if (!TryLoadConfig())
                    return;

                UpdateShop = true;
            }

            if (!myStoreBlock.IsWorking)
                return;

            UpdateCounter++;

            if (!UpdateShop && UpdateCounter <= 750)
                return;

            MyLog.Default.WriteLine("SimpleStore.StoreBlock: Starting to update store");

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
                    MyLog.Default.WriteLine($"SimpleStore.StoreBlock: InsertOrder {definition.Id.SubtypeName}");
                    result = myStoreBlock.InsertOrder(itemData, out id);
                    if (result != Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success)
                        MyLog.Default.WriteLine("SimpleStore.StoreBlock: Buy result " + result);
                }

                //sell
                if (itemConfig.Sell.Count > 0)
                {
                    int sellCount = resellItems ? Math.Max(itemConfig.Sell.Count, currentInvItemAmount) : itemConfig.Sell.Count;
                    itemData = new MyStoreItemData(definition.Id, sellCount, itemConfig.Sell.Price,
                        (amount, left, totalPrice, sellerPlayerId, playerId) => OnTransaction(amount, left, totalPrice, sellerPlayerId, playerId, definition), null);
                    MyLog.Default.WriteLine($"SimpleStore.StoreBlock: InsertOffer {definition.Id.SubtypeName}");
                    result = myStoreBlock.InsertOffer(itemData, out id);

                    if (result == Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success)
                        MyVisualScriptLogicProvider.AddToInventory(myStoreBlock.Name, definition.Id, sellCount);
                    else
                        MyLog.Default.WriteLine("SimpleStore.StoreBlock: Sell result " + result);

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

                if (player != null)
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


        private void CreateConfig()
        {
            config.AddSection(ConfigSettings);
            config.SetSectionComment(ConfigSettings, "Do not activate too many objects, the store has a limited number of slots");
            config.SetSectionComment(ConfigSettings, "Autogenerated prices are the minumum allowed by Keen, setting lower will log an error");
            config.SetSectionComment(ConfigSettings, "After changing config to force an update, turn store off wait 3s and turn it on. Auto update is every 20mins");
            config.SetSectionComment(ConfigSettings, "Format is BuyAmount:BuyPrice,SellAmount:SellPrice");

            config.Set(ConfigSettings, Resell, true);

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

            ItemConfig defaultItemConfig = new ItemConfig();

            foreach (var definition in MyDefinitionManager.Static.GetAllDefinitions())
            {
                if (BlacklistItems.Contains(definition.Id.SubtypeName))
                    continue;

                typeId = definition.Id.TypeId.ToString();
                match = Regex.Match(typeId + definition.Id.SubtypeName, @"[\[\]\r\n|=]");
                if (match.Success)
                    continue;

                if (config.ContainsSection(typeId))
                {
                    defaultItemConfig.SetDefaultPrices(definition.Id);
                    config.Set(typeId, definition.Id.SubtypeName, defaultItemConfig.ToString());
                }
            }

            config.Invalidate();
            myStoreBlock.CustomData = config.ToString();
        }

        private bool TryLoadConfig()
        {
            MyLog.Default.WriteLine("SimpleStore.StoreBlock: Start TryLoadConfig");

            bool configOK = true;
            bool updateConfig = false;

            if (config.TryParse(myStoreBlock.CustomData))
            {
                if (!config.ContainsSection(ConfigSettings) || !config.ContainsKey(ConfigSettings, Resell))
                    configOK = false;

                resellItems = config.Get(ConfigSettings, Resell).ToBoolean();

                List<string> sections = new List<string>();
                List<MyIniKey> keys = new List<MyIniKey>();
                ItemConfig itemConfig = new ItemConfig();
                string rawIniValue;

                config.GetSections(sections);
                foreach (var section in sections)
                {
                    config.GetKeys(section, keys);
                    foreach (var key in keys)
                    {
                        if (!config.Get(section, key.Name).TryGetString(out rawIniValue))
                        {
                            configOK = false;
                            MyLog.Default.WriteLine($"SimpleStore.StoreBlock: Failed to get {section}:{key.Name}");
                            break;
                        }
                        if (section == ConfigSettings)
                        {
                            continue;
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