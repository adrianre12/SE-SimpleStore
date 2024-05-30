using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Utils;

namespace SimpleStore.StoreBlock
{
    public static class MyIniExtension
    {
        /// <summary>
        /// Sorts the key values in sections but does not support end comments or end content.
        /// </summary>
        /// <param name="myIni"></param>
        /// <returns></returns>
        public static string ToStringSorted(this MyIni myIni)
        {
            if (myIni == null)
            {
                MyLog.Default.WriteLine($"SimpleStore.StoreBlock: ToStringSorted myIni=null");
                return "";
            }
            StringBuilder tmpContentBuilder = new StringBuilder();
            List<MyIniKey> tmpKeyList = new List<MyIniKey>();
            string[] tmpStrings;

            bool flag = false;
            string sectionComment = null;
            string keyComment = null;
            string key = null;
            List<string> sections = new List<string>();

            myIni.GetSections(sections);
            if (sections == null)
            {
                MyLog.Default.WriteLine($"SimpleStore.StoreBlock: ToStringSorted sections=null");
                return "";
            }

            foreach (string section in sections)
            {

                if (flag)
                {
                    tmpContentBuilder.Append('\n');
                }
                flag = true;
                sectionComment = myIni.GetSectionComment(section);
                if (sectionComment != null)
                {
                    tmpStrings = sectionComment.Split('\n');
                    foreach (string tmpString in tmpStrings)
                    {
                        if (string.IsNullOrEmpty(tmpString))
                            continue;
                        tmpContentBuilder.Append(";");
                        tmpContentBuilder.Append(tmpString);
                        tmpContentBuilder.Append('\n');
                    }
                }
                tmpContentBuilder.Append("[");
                tmpContentBuilder.Append(section);
                tmpContentBuilder.Append("]\n");

                myIni.GetKeys(section, tmpKeyList);

                tmpKeyList.Sort((k1, k2) => k1.Name.CompareTo(k2.Name));

                foreach (var tmpKey in tmpKeyList)
                {
                    key = tmpKey.Name;

                    keyComment = myIni.GetComment(section, key);
                    if (keyComment != null)
                    {
                        tmpStrings = keyComment.Split('\n');
                        foreach (string tmpString in tmpStrings)
                        {
                            if (string.IsNullOrEmpty(tmpString))
                                continue;
                            tmpContentBuilder.Append(";");
                            tmpContentBuilder.Append(keyComment);
                            tmpContentBuilder.Append('\n');
                        }
                    }

                    tmpContentBuilder.Append(key);
                    tmpContentBuilder.Append('=');

                    tmpContentBuilder.Append(myIni.Get(tmpKey).ToString());
                    tmpContentBuilder.Append('\n');
                }
            }
            // cant do end comment or end content as it is not exposed

            return tmpContentBuilder.ToString(); ;
        }
    }
}
