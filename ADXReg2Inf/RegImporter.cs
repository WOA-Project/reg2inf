using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ADXReg2Inf
{
    public static class RegImporter
    {
        public static Dictionary<string, Hashtable> GetRegistry(string regPath)
        {
            Dictionary<string, Hashtable> registry = new Dictionary<string, Hashtable>();

            string stockReg = System.IO.File.ReadAllText(regPath);

            List<string> reg = stockReg.Replace("controlset001", "CurrentControlSet", StringComparison.InvariantCultureIgnoreCase).Replace("\\\r\n", "").Replace("\r", "").Split('\n').ToList();//normalized reg

            reg = reg.Where(x => (x.StartsWith("[") || x.StartsWith("@") || x.StartsWith("\""))).ToList();

            Hashtable tmp = new Hashtable();
            for (int i = 0; i < reg.Count; i++)
            {
                if (reg[i].StartsWith("["))
                    tmp = new Hashtable();

                var j = i;

                for (j = i + 1; j < reg.Count; j++)
                {
                    if (reg[j].StartsWith("["))
                        break;
                    else
                        tmp.Add(reg[j].Split('=')[0].Replace("\"", ""), reg[j].Split('=')[1]);
                }

                if (tmp.Count > 0)
                {
                    if (!registry.ContainsKey(reg[i].Replace("[", "").Replace("]", "")))
                        registry.Add(reg[i].Replace("[", "").Replace("]", ""), tmp);
                }
                    
                i = j - 1;
            }

            return registry;
        }

        public static Dictionary<string, Hashtable> DebloatReg(Dictionary<string, Hashtable> dictionary, BloatType bloatToRemove, string srvName = "")
        {
            switch (bloatToRemove)
            {
                case BloatType.PNPLOCKDOWN:

                    List<string> keysToRemove = new List<string>();

                    foreach (var item in dictionary)
                        if (item.Key.ToUpper().Contains(@"CURRENTVERSION\SETUP\PNPLOCKDOWNFILES") ||
                            item.Key.ToUpper().Contains(@"CURRENTVERSION\SETUP\PNPRESOURCES"))
                            keysToRemove.Add(item.Key);

                    keysToRemove.ForEach(key => dictionary.Remove(key));
                    break;
                case BloatType.ALL:
                    List<string> keysToRemove1 = new List<string>();

                    foreach (var item in dictionary)
                        if (item.Key.ToUpper().Contains(@"CURRENTVERSION\SETUP\PNPLOCKDOWNFILES") ||
                            item.Key.ToUpper().Contains(@"CURRENTVERSION\SETUP\PNPRESOURCES") ||
                            item.Key.ToUpper().Contains(@"DRIVERDATABASE\DEVICEIDS") ||
                            item.Key.ToUpper().Contains(@"DRIVERDATABASE\DRIVERINFFILES") ||
                            item.Key.ToUpper().Contains(@"DRIVERDATABASE\DRIVERPACKAGES"))
                            keysToRemove1.Add(item.Key);

                    keysToRemove1.ForEach(key => dictionary.Remove(key));
                    break;
                case BloatType.SPECIFIC_SERVICE:
                    List<string> keysToRemove2 = new List<string>();

                    foreach (var item in dictionary)
                        if (item.Key.ToUpper().Contains(@"CURRENTCONTROLSET\SERVICES" + srvName.ToUpper()))
                            keysToRemove2.Add(item.Key);

                    keysToRemove2.ForEach(key => dictionary.Remove(key));
                    break;
                default:
                    break;
            }


            return dictionary;
        }
    }

    public enum BloatType
    {
        PNPLOCKDOWN,
        SPECIFIC_SERVICE,
        ALL
    }
}
