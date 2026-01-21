

using Sandbox.ModAPI;
using System;
using System.Xml.Serialization;


namespace SimpleStore.Onezer
{
    public partial class OnezerConfig
    {
        internal const string configFilename = "Config-Onezer.xml";

        [XmlIgnore]
        public bool ConfigLoaded;

        public bool Debug = false;
        public bool Enabled = false;
        public int MinimalPricePerUnit = 1;

        public void Verify()
        {
            MinimalPricePerUnit = MinimalPricePerUnit < 1 ? 1 : MinimalPricePerUnit;
        }

        public static OnezerConfig LoadConfig()
        {
            if (MyAPIGateway.Utilities.FileExistsInWorldStorage(configFilename, typeof(OnezerConfig)) == true)
            {
                try
                {
                    OnezerConfig config = null;
                    var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(configFilename, typeof(OnezerConfig));
                    string configcontents = reader.ReadToEnd();
                    config = MyAPIGateway.Utilities.SerializeFromXML<OnezerConfig>(configcontents);
                    config.ConfigLoaded = true;
                    Log.Msg($"Loaded Existing Settings From {configFilename}");
                    config.Verify();
                    return config;
                }
                catch (Exception exc)
                {
                    Log.Msg(exc.ToString());
                    Log.Msg($"ERROR: Could Not Load Settings From {configFilename}. Using Empty Configuration.");
                    return new OnezerConfig();
                }

            }

            Log.Msg($"{configFilename} Doesn't Exist. Creating Default Configuration. ");

            var defaultSettings = new OnezerConfig();

            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(configFilename, typeof(OnezerConfig)))
                {
                    writer.Write(MyAPIGateway.Utilities.SerializeToXML<OnezerConfig>(defaultSettings));
                }
            }
            catch (Exception exc)
            {
                Log.Msg(exc.ToString());
                Log.Msg($"ERROR: Could Not Create {configFilename}. Default Settings Will Be Used.");
            }

            return defaultSettings;
        }
    }
}
