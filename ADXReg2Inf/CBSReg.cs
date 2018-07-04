using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ADXReg2Inf
{
    public static class CBSReg
    {
        public static Hashtable GetRegistries(string uri)
        {
            Hashtable registryKeysMerged = new Hashtable();

            Hashtable hashtable = new Hashtable();

            XDocument xdocument = XDocument.Load(uri);
            XNamespace @namespace = xdocument.Root.Name.Namespace;
            foreach (XElement xelement in xdocument.Root.Elements(@namespace + "registryKeys"))
            {
                if (xelement != null)
                {
                    foreach (XElement xelement2 in xelement.Elements(@namespace + "registryKey"))
                    {
                        Hashtable hashtable2 = new Hashtable();
                        foreach (XElement xelement3 in xelement2.Elements(@namespace + "registryValue"))
                        {
                            if (xelement3.Attribute("name") != null)
                            {
                                string value = null;
                                string type = null;

                                if (xelement3.Attribute("value") != null)
                                    value = xelement3.Attribute("value").Value;

                                if (xelement3.Attribute("valueType") != null)
                                    type = xelement3.Attribute("valueType").Value;

                                hashtable2.Add(xelement3.Attribute("name").Value, new KeyValuePair<string, string>(value, type));
                            }
                        }

                        if (!hashtable.ContainsKey(xelement2.Attribute("keyName").Value))
                        {
                            hashtable.Add(xelement2.Attribute("keyName").Value, hashtable2);
                        }
                        else
                        {
                            foreach (object obj in hashtable2)
                            {
                                DictionaryEntry dictionaryEntry = (DictionaryEntry)obj;
                                ((Hashtable)hashtable[xelement2.Attribute("keyName").Value])[dictionaryEntry.Key] = dictionaryEntry.Value;
                            }
                        }
                    }
                }
            }
            if (hashtable.Count > 0)
            {
                foreach (object obj2 in hashtable)
                {
                    DictionaryEntry dictionaryEntry2 = (DictionaryEntry)obj2;
                    registryKeysMerged[dictionaryEntry2.Key] = dictionaryEntry2.Value;
                }
            }

            return registryKeysMerged;
        }
    }
}
