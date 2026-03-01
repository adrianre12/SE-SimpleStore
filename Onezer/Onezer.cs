using Sandbox.Definitions;
using Sandbox.ModAPI;
using SimpleStore.StoreBlock;
using System;
using System.Linq;
using System.Text;
using VRage.Game.Components;

namespace SimpleStore
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    partial class Onezer : MySessionComponentBase
    {
        public static Onezer Instance;

        private OnezerConfig config;
        const string VariableId = nameof(IngameStoreBlockGameLogic);

        public string DefaultCustomData;
        public override void LoadData()
        {
            Instance = this;
            if (MyAPIGateway.Session.IsServer)
                LoadDataHost();
            else
                LoadDataClient();
        }

        private void LoadDataHost()
        {
            config = OnezerConfig.LoadConfig();
            Log.Debug = config.Debug;


            if (!config.Enabled)
            {
                Log.Msg("Onezer is not Enabled");
                DefaultCustomData = DefaultConfig.CreateDefaultConfigString();
                MyAPIGateway.Utilities.SetVariable<string>(VariableId, Convert.ToBase64String(ASCIIEncoding.UTF8.GetBytes(DefaultCustomData)));
                return;
            }

            CreateDefaultPriceFiles();

            if (config.UsePriceFile)
            {
                LoadPriceFile();
            }

            DefaultCustomData = DefaultConfig.CreateDefaultConfigString();
            MyAPIGateway.Utilities.SetVariable<string>(VariableId, Convert.ToBase64String(ASCIIEncoding.UTF8.GetBytes(DefaultCustomData)));

            if (config.UseMinimalPricePerUnit)
            {
                Log.Msg($"Onezer setting all MinimalPricePerUnit={config.MinimalPricePerUnit}");

                foreach (var physicalItem in MyDefinitionManager.Static.GetAllDefinitions().OfType<MyPhysicalItemDefinition>())
                {
                    if (Log.Debug) Log.Msg($"Setting '{physicalItem.Id} from {physicalItem.MinimalPricePerUnit} to {config.MinimalPricePerUnit}");
                    physicalItem.MinimalPricePerUnit = config.MinimalPricePerUnit;
                }
            }
        }

        private void LoadDataClient()
        {
            try
            {
                string saveText;
                if (!MyAPIGateway.Utilities.GetVariable<string>(VariableId, out saveText))
                    throw new Exception($"Variable {VariableId} not found in game save!");
                DefaultCustomData = Encoding.UTF8.GetString(Convert.FromBase64String(saveText));
                Log.Msg("Client loaded DefaultCustomData");
            }
            catch (Exception e)
            {
                Log.Msg($"Error getting DefaultCustomData\n {e}");
                DefaultCustomData = "";
            }
        }

        protected override void UnloadData()
        {
            try
            {
                Instance = null;
            }
            catch (Exception e)
            {
                Log.Msg($"Error in UnloadData\n{e.ToString()}");
            }
        }
    }
}
