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
using VRage.Game.Definitions.SessionComponents;
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
        private List<MyDefinitionId> AutoRefineList = new List<MyDefinitionId>();

        internal IMyStoreBlock myStoreBlock;
        private MyIni config = new MyIni();

        private static Random rnd = new Random();

        bool lastBlockEnabledState = false;
        bool UpdateShop = true;
        int UpdateCounter = 0;
        bool resellItems = false; //deprecated
        int spawnDistance = DefaultConfig.DefaultSpawnDistance;
        int refreshCounterLimit = DefaultConfig.DefaultRefreshPeriod;
        float refineYield = DefaultConfig.DefaultRefineYield;
        bool debugLog = false;
        float transactionFee = 0.02f;
        private long lastBlockOwner = -1;

        List<IMyPlayer> Players = new List<IMyPlayer>();
        List<Sandbox.ModAPI.Ingame.MyStoreQueryItem> StoreItems = new List<Sandbox.ModAPI.Ingame.MyStoreQueryItem>();

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;

            myStoreBlock = Entity as IMyStoreBlock;

            if (!MyAPIGateway.Session.IsServer)
                return;

            MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: Ores loaded ({RefineOre.OresLoaded()})");

            MyLog.Default.WriteLine("SimpleStore.StoreBlock: Loaded...");
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();
            NeedsUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;

            if (MyAPIGateway.Session.IsServer)
            {
                var x = MyDefinitionManager.Static.GetDefinition<MySessionComponentEconomyDefinition>("Default");
                if (x != null)
                {
                    transactionFee = x.TransactionFee;
                    //MyLog.Default.WriteLine($"SimpleStore.StoreBlock: TransactionFee loaded ({transactionFee})");
                }
            }
        }

        public override void UpdateAfterSimulation100()
        {
            if (myStoreBlock.CustomData == "")
                myStoreBlock.CustomData = Onezer.Instance.DefaultCustomData;

            if (MyAPIGateway.Session.IsServer)
                UpdateAfterSimulation100Host();
        }


        public void UpdateAfterSimulation100Host()
        {
            if (myStoreBlock.Enabled != lastBlockEnabledState)
            {
                lastBlockEnabledState = myStoreBlock.Enabled;

                if (myStoreBlock.Enabled == false)
                    return;

                if (!TryLoadConfig())
                    return;

                UpdateShop = true;
            }

            if (!myStoreBlock.IsWorking)
                return;

            UpdateCounter++;

            if (!UpdateShop && UpdateCounter <= refreshCounterLimit)
                return;

            MyLog.Default.WriteLine("SimpleStore.StoreBlock: Starting to update store");

            ResetNPCAccountBallance();

            myStoreBlock.GetPlayerStoreItems(StoreItems);

            foreach (var item in StoreItems)
            {
                myStoreBlock.CancelStoreItem(item.Id);
            }

            Match match;
            string rawIniValue;
            ItemConfig itemConfig = new ItemConfig();
            string subtypeName = "";
            AutoRefineList.Clear();

            foreach (var definition in MyDefinitionManager.Static.GetAllDefinitions())
            {
                subtypeName = DefaultConfig.FixKey(definition.Id.SubtypeName);
                match = Regex.Match(definition.Id.TypeId.ToString() + subtypeName, @"[\[\]\r\n|=]");
                if (match.Success)
                    continue;

                var section = definition.Id.TypeId.ToString().Remove(0, 16); //remove "MyObjectBuilder_"

                if (!config.ContainsSection(section) || !config.ContainsKey(section, subtypeName))
                    continue;

                if (!config.Get(section, subtypeName).TryGetString(out rawIniValue))
                    continue;

                if (!itemConfig.TryParse(rawIniValue))
                {
                    myStoreBlock.Enabled = false;
                    MyLog.Default.WriteLine($"SimpleStore.StoreBlock: Config error in Custom Data. Grid='{myStoreBlock.CubeGrid.CustomName}' Store='{myStoreBlock.CustomName}'");

                    continue;
                }

                var currentInvItemAmount = MyVisualScriptLogicProvider.GetEntityInventoryItemAmount(myStoreBlock.Name, definition.Id);
                MyVisualScriptLogicProvider.RemoveFromEntityInventory(myStoreBlock.Name, definition.Id, currentInvItemAmount);
                itemConfig.Buy.SetResellCount(currentInvItemAmount);
                if (resellItems)
                    itemConfig.Buy.SetAutoResell();

                var prefab = MyDefinitionManager.Static.GetPrefabDefinition(definition.Id.SubtypeName);

                var result = Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success;

                long id;
                MyStoreItemData itemData;

                //sell from the players point of view
                int sellCount = itemConfig.Sell.Count;
                if (prefab == null && sellCount > 0)
                {
                    itemData = new MyStoreItemData(definition.Id, sellCount, itemConfig.Sell.Price,
                        (amount, left, totalPrice, sellerPlayerId, playerId) => OnTransactionSell(amount, left, totalPrice, sellerPlayerId, playerId, definition), null);

                    MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: InsertOrder {definition.Id.SubtypeName}  Count={sellCount} Price={itemConfig.Sell.Price}");
                    result = myStoreBlock.InsertOrder(itemData, out id);
                    if (result != Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success)
                        MyLog.Default.WriteLine($"SimpleStore.StoreBlock: Sell result {definition.Id.SubtypeName}: {result}");
                    if (itemConfig.Sell.IsAutoRefine)
                    {
                        MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: Sell AutoRefine {definition.Id.SubtypeName}");
                        AutoRefineList.Add(definition.Id);
                    }
                }

                //buy
                int buyCount = itemConfig.Buy.Count;
                if (buyCount > 0)
                {
                    itemData = new MyStoreItemData(definition.Id, buyCount, itemConfig.Buy.Price,
                        (amount, left, totalPrice, sellerPlayerId, playerId) => OnTransactionBuy(amount, left, totalPrice, sellerPlayerId, playerId, definition), null);

                    MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: InsertOffer {definition.Id.SubtypeName} Count={buyCount} Price={itemConfig.Buy.Price}");
                    result = myStoreBlock.InsertOffer(itemData, out id);

                    if (result == Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success)
                        MyVisualScriptLogicProvider.AddToInventory(myStoreBlock.Name, definition.Id, buyCount);
                    else
                        MyLog.Default.WriteLine($"SimpleStore.StoreBlock: Buy result {definition.Id.SubtypeName}: {result}");

                }
            }

            UpdateCounter = 0;
            if (UpdateShop)
            {
                UpdateCounter = rnd.Next((int)(refreshCounterLimit - DefaultConfig.MinRefreshPeriod * 0.2)); // stop them all refreshing at the same time.
                MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: UpdateCounter={UpdateCounter}");
            }
            UpdateShop = false;
        }

        private void OnTransactionSell(int amountSold, int amountRemaining, long priceOfTransaction, long ownerOfBlock, long buyerSeller, MyDefinitionBase compDef)
        {
            MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: OnTransactionSell {compDef.Id.TypeId} {compDef.Id.SubtypeName}");
            MyAPIGateway.Multiplayer.Players.RequestChangeBalance(ownerOfBlock, priceOfTransaction);

            if (!AutoRefineList.Contains(compDef.Id))
                return;

            MyBlueprintDefinitionBase.Item[] ingots;
            int amountReq;
            if (RefineOre.TryGetIngots(compDef.Id.SubtypeName, out amountReq, out ingots))
            {
                int refineN = amountSold / amountReq;
                MyVisualScriptLogicProvider.RemoveFromEntityInventory(myStoreBlock.Name, compDef.Id, refineN * amountReq);
                MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: OnTransactionSell amountSold={amountSold} amountReq={amountReq} refineN={refineN} oreUsed={refineN * amountReq}");
                foreach (var ingot in ingots)
                {
                    int amountRefined = (int)(refineYield * refineN * ((float)ingot.Amount));
                    MyVisualScriptLogicProvider.AddToInventory(myStoreBlock.Name, ingot.Id, amountRefined);
                    MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: OnTransactionSell added {amountRefined} {ingot.Id.SubtypeName}");
                }
            }
            else
            {
                MyLog.Default.WriteLine($"SimpleStore.StoreBlock: OnTransactionSell TryGetIngots failed {compDef.Id.SubtypeName}");
            }
        }

        private void OnTransactionBuy(int amountBought, int amountRemaining, long priceOfTransaction, long ownerOfBlock, long buyerSeller, MyDefinitionBase compDef)
        {
            MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: OnTransactionBuy {compDef.Id.TypeId} {compDef.Id.SubtypeName}");

            MyAPIGateway.Multiplayer.Players.RequestChangeBalance(ownerOfBlock, (long)(-priceOfTransaction * (1 - transactionFee)));

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
                    var position = player.GetPosition() + (player.Character.LocalMatrix.Forward * spawnDistance);

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
                    MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: OnTransactionBuy Spawned {compDef.Id.SubtypeName}");

                }
            }
        }

        private void ResetNPCAccountBallance()
        {
            if (lastBlockOwner == myStoreBlock.OwnerId)
                return;

            lastBlockOwner = myStoreBlock.OwnerId;

            List<IMyIdentity> identities = new List<IMyIdentity>();
            MyAPIGateway.Multiplayer.Players.GetAllIdentites(identities, identity => identity.IdentityId == myStoreBlock.OwnerId);
            foreach (var identity in identities)
            {
                //Log.Msg($"name='{identity.DisplayName} id={identity.IdentityId} model={identity.Model == null} dead={identity.IsDead}");
                if (identity.IdentityId == myStoreBlock.OwnerId && identity.Model != null) // dirty hack to identify non npc.
                {
                    MyLog.Default.WriteLine($"SimpleStore.StoreBlock: Owned by '{identity.DisplayName}', not adjusting Account balance");
                    return;
                }
            }

            MyLog.Default.WriteLine($"SimpleStore.StoreBlock: Owned by NPC, adjusting Account balance, I can't do anything about the errors");

            MyAPIGateway.Multiplayer.Players.RequestChangeBalance(myStoreBlock.OwnerId, (long)-100 * 1000 * 1000000);
            MyAPIGateway.Multiplayer.Players.RequestChangeBalance(myStoreBlock.OwnerId, (long)-100 * 1000 * 1000000);

            MyAPIGateway.Multiplayer.Players.RequestChangeBalance(myStoreBlock.OwnerId, (long)100 * 1000 * 1000000);
        }

        private bool TryLoadConfig()
        {
            MyLog.Default.WriteLine("SimpleStore.StoreBlock: Loading Config");

            bool configOK = true;
            bool storeConfig = false;

            if (config.TryParse(myStoreBlock.CustomData))
            {
                if (!config.ContainsSection(DefaultConfig.ConfigSettings))
                    configOK = false;

                resellItems = config.Get(DefaultConfig.ConfigSettings, DefaultConfig.Resell).ToBoolean(false);
                spawnDistance = config.Get(DefaultConfig.ConfigSettings, DefaultConfig.SpawnDistance).ToInt32(DefaultConfig.DefaultSpawnDistance);
                refreshCounterLimit = Math.Max((int)(config.Get(DefaultConfig.ConfigSettings, DefaultConfig.RefreshPeriod).ToInt32(DefaultConfig.DefaultRefreshPeriod) * 37.5), DefaultConfig.MinRefreshPeriod);
                refineYield = config.Get(DefaultConfig.ConfigSettings, DefaultConfig.RefineYield).ToSingle(DefaultConfig.DefaultRefineYield);
                debugLog = config.Get(DefaultConfig.ConfigSettings, DefaultConfig.DebugLog).ToBoolean(false);

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
                        if (section == DefaultConfig.ConfigSettings)
                        {
                            continue;
                        }

                        if (!itemConfig.TryParse(rawIniValue))
                        {
                            config.Set(section, key.Name, itemConfig.ToString());
                            storeConfig = true;
                            continue;
                        }

                        if (itemConfig.IsRemoveError())
                        {
                            config.Set(section, key.Name, itemConfig.ToString());
                            storeConfig = true;
                        }
                    }
                }

                if (storeConfig)
                {
                    config.Invalidate();
                    myStoreBlock.CustomData = config.ToString();
                }

                if (!configOK)
                {
                    MyLog.Default.WriteLine($"SimpleStore.StoreBlock: Config error Grid='{myStoreBlock.CubeGrid.CustomName}' Store='{myStoreBlock.CustomName}'");
                    myStoreBlock.Enabled = false;
                }

            }
            else
            {
                configOK = false;
                MyLog.Default.WriteLine("SimpleStore.StoreBlock: Config Syntax error");
                myStoreBlock.Enabled = false;
            }
            return configOK;
        }



    }
}