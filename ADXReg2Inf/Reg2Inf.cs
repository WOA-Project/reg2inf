using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;

namespace ADXReg2Inf
{
    public static class Reg2Inf
    {
        public static string SystemNameKey = "RTSYSTEM";
        public static string SoftwareNameKey = "RTSOFTWARE";

        public static BaseInf GenerateBaseInf(Dictionary<string, Hashtable> reg, bool suppressWUDFSupportErrorAsTest = false)
        {
            try
            {
                BaseInf baseInf = new BaseInf
                {
                    Infs = new List<Inf>(),
                    ExternalRegs = new List<string>(),
                    ParsingLog = ""
                };

                var infFiles = GetDriverInfFiles(reg);

                if (!suppressWUDFSupportErrorAsTest)
                {
                    var wudf = GetDriverWUDF(reg);
                    if (wudf.Count > 0)
                    {
                        Console.WriteLine("(reg2inf) WUDF Driver is not supported yet");
                        return baseInf;
                    }
                }

                infFiles.ForEach(infFile =>
                {
                    Inf inf = new Inf
                    {
                        DeviceIDs = new List<KeyValuePair<string, Descriptor>>(),
                        Configurations = new List<Configuration>(),
                        Strings = new List<KeyValuePair<string, string>>(),
                        Services = new List<Service>(),
                        InternalRegs = new List<string>(),
                        Manufacturer = ""
                    };

                    var infName = infFile.Key.GetPartAfterTo("DRIVERINFFILES");
                    inf.InfName = infName;

                    var packageName = infFile.Value["Active"] as string;
                    inf.PackageName = packageName.Replace("\"", "");

                    var configuration = infFile.Value["Configurations"] as string;

                    Version version = new Version
                    {
                        CatalogFile = infName.ToLower().Replace(".inf", ".cat"),
                        Signature = "$WINDOWS NT$",
                        SourceFiles = new List<string>()
                    };

                    var Guids = GetDriverGuids(reg);
                    Guids.ForEach(guid =>
                    {
                        foreach (DictionaryEntry infNameForGuid in guid.Value)
                        {
                            if ((infNameForGuid.Key as string).ToUpper() == infName.ToUpper())
                            {
                                version.ClassGuid = guid.Key.GetPartAfterTo(@"DEVICEIDS");
                                version.Class = Helper.GetClassFromGuid(version.ClassGuid);
                                break;
                            }
                        }
                    });

                    var Ids = GetDriverIds(reg);
                    Ids.ForEach(id =>
                    {
                        foreach (DictionaryEntry infNameForId in id.Value)
                        {
                            if ((infNameForId.Key as string).ToUpper() == infName.ToUpper())
                            {
                                var deviceId = id.Key.GetPartAfterTo(@"DEVICEIDS");

                                //SEARCH FOR DESCRIPTOR
                                var descs = GetDriverPackagesDescriptors(reg, inf.PackageName, deviceId);
                                Descriptor desc = new Descriptor
                                {
                                    Manufacturer = (descs[0].Value["Manufacturer"] as string).Replace("\"", ""),
                                    Description = (descs[0].Value["Description"] as string).Replace("\"", ""),
                                    Configuration = (descs[0].Value["Configuration"] as string).Replace("\"", "")
                                };

                                inf.Manufacturer = (descs[0].Value["Manufacturer"] as string).Replace("\"", "");

                                inf.DeviceIDs.Add(new KeyValuePair<string, Descriptor>(deviceId, desc));

                                //SEARCH FOR CONFIGURATION
                                var confs = GetDriverPackagesConfigurations(reg, inf.PackageName, desc.Configuration);
                                confs.ForEach(conf =>
                                {
                                    var config = new Configuration
                                    {
                                        Properties = new List<string>(),
                                        Keys = new List<string>(),
                                        ServiceName = (conf.Value["Service"] as string).Replace("\"", ""),
                                        FromDeviceID = deviceId
                                    };

                                    desc.LinkedServiceName = (conf.Value["Service"] as string).Replace("\"", "");

                                    var props = GetDriverPackagesConfigurationProperties(reg, inf.PackageName, desc.Configuration);


                                    config.Properties.AddRange(Helper.GetConvertedRegKeysIntoInfKeysForProperties(props));

                                    var keys = GetDriverPackagesConfigurationGeneric(reg, inf.PackageName, desc.Configuration);

                                    config.Keys.AddRange(Helper.GetConvertedRegKeysIntoInfKeys(keys, RegHive.HKR, desc.Configuration.ToUpper(), baseInf));

                                    inf.Configurations.Add(config);
                                });

                                break;
                            }
                        }
                    });

                    var servicesName = inf.Configurations.Select(x => x.ServiceName).Distinct().ToList();

                    var strings = GetDriverPackagesStrings(reg, inf.PackageName);
                    strings.ForEach(@string =>
                    {
                        foreach (DictionaryEntry item in @string.Value)
                        {
                            inf.Strings.Add(new KeyValuePair<string, string>(item.Key as string, item.Value as string));
                        }
                    });

                    servicesName.ForEach(serviceName =>
                    {
                        var service = GetServiceByName(reg, serviceName);
                        if (service.Count > 0)
                        {
                            var serv = new Service();
                            serv.AddReg = new List<string>();

                            serv.ServiceName = serviceName;

                            if (service[0].Value.ContainsKey("DisplayName"))
                            {
                                var displayName = (service[0].Value["DisplayName"] as string).Replace("\"", "");
                                if (displayName.Contains(",") && displayName.Contains(";"))
                                {
                                    var onlyDisplayName = displayName.Split(',')[1];
                                    serv.DisplayName = onlyDisplayName.Split(';')[0];

                                    var stringsFound = inf.Strings.Where(x => x.Key.ToLower() == serv.DisplayName.Replace("%", "").ToLower()).ToList();
                                    if (stringsFound.Count == 0)
                                        inf.Strings.Add(new KeyValuePair<string, string>(onlyDisplayName.Split(';')[0].Replace("%", ""), "\"" + onlyDisplayName.Split(';')[1] + "\""));
                                }
                                else
                                {
                                    serv.DisplayName = displayName;
                                }
                            }

                            if (service[0].Value.ContainsKey("ErrorControl"))
                            {
                                var errorControl = (service[0].Value["ErrorControl"] as string).Replace("dword:", "", StringComparison.InvariantCultureIgnoreCase);
                                serv.ErrorControl = Convert.ToInt32(errorControl, 16).ToString();
                            }

                            if (service[0].Value.ContainsKey("ImagePath"))
                            {
                                var imagePath = (service[0].Value["ImagePath"] as string);
                                serv.ServicePath = Helper.StringArrayHexToString(Helper.CleanSplitHexString(imagePath));
                            }

                            if (service[0].Value.ContainsKey("Start"))
                            {
                                var startType = (service[0].Value["Start"] as string).Replace("dword:", "", StringComparison.InvariantCultureIgnoreCase);
                                serv.StartType = Convert.ToInt32(startType, 16).ToString();
                            }

                            if (service[0].Value.ContainsKey("Type"))
                            {
                                var serviceType = (service[0].Value["Type"] as string).Replace("dword:", "", StringComparison.InvariantCultureIgnoreCase);
                                serv.ServiceType = Convert.ToInt32(serviceType, 16).ToString();
                            }

                            if (service[0].Value.ContainsKey("Group"))
                            {
                                var group = (service[0].Value["Group"] as string);
                                serv.LoadOrderGroup = group.Replace("\"", "");
                            }

                            if (service[0].Value.ContainsKey("Tag"))
                            {
                                var tag = (service[0].Value["Tag"] as string).Replace("dword:", "", StringComparison.InvariantCultureIgnoreCase);
                                serv.Tag = Convert.ToInt32(tag, 16).ToString();
                            }

                            var hash = new Hashtable();
                            foreach (DictionaryEntry item in service[0].Value)
                            {
                                if (!((item.Key as string) == "Owners" || (item.Key as string) == "DisplayName" || (item.Key as string) == "ErrorControl" || (item.Key as string) == "ImagePath"
                                || (item.Key as string) == "Start" || (item.Key as string) == "Type" || (item.Key as string) == "Group" || (item.Key as string) == "Tag"))
                                {
                                    hash.Add(item.Key, item.Value);
                                }
                            }

                            var regToAdd = GetRegsByServiceName(reg, serviceName);

                            serv.AddReg.AddRange(Helper.GetConvertedRegKeysIntoInfKeys(regToAdd, RegHive.HKR, serviceName, baseInf));

                            var wdf = GetWDFByServiceName(reg, serviceName);

                            if (wdf.Count > 0)
                            {
                                serv.IsWdf = true;
                                serv.KmdfLibraryVersion = (wdf[0].Value["KmdfLibraryVersion"] as string).ToString();
                            }


                            if (hash.Count > 0)
                                serv.AddReg.AddRange(Helper.GetConvertedRegKeysIntoInfKeys(new List<KeyValuePair<string, Hashtable>>()
                                                    { new KeyValuePair<string, Hashtable>(service[0].Key, hash) }, RegHive.HKR, serviceName, baseInf));
                            inf.Services.Add(serv);
                        }
                    });

                    var DriverPackage = GetDriverPackages(reg, inf.PackageName);
                    DriverPackage.ForEach(package =>
                    {
                        if (package.Key.ToUpper().EndsWith(inf.PackageName.ToUpper()))
                        {
                            version.DriverVer = Helper.GetProperDriverVer(Helper.StringToByteArray(Helper.CleanHexString(package.Value["Version"] as string)));
                            version.Provider = package.Value["Provider"] as string;
                        }
                    });

                    var pnpLockDownFiles = GetPnpLockDownFiles(reg);
                    pnpLockDownFiles.ForEach(file =>
                    {
                        var ownerString = file.Value["Owners"] as string;

                        if (ownerString != null)
                        {
                            var owner = Helper.StringArrayHexToString(Helper.CleanSplitHexString(ownerString));
                            if (owner.ToLower() == inf.InfName.ToLower())
                            {
                                version.PnpLockDown = true;
                                version.SourceFiles.Add(file.Key.GetPartAfterTo("PNPLOCKDOWNFILES"));
                            }
                        }
                    });

                    inf.Version = version;
                    baseInf.Infs.Add(inf);
                });

                Dictionary<string,Hashtable> debloatedReg = reg;

                baseInf.Infs.ForEach(inf =>  inf.Services.ForEach(service => debloatedReg = RegImporter.DebloatReg(debloatedReg, BloatType.SPECIFIC_SERVICE, $@"\{service.ServiceName}")) );

                debloatedReg = RegImporter.DebloatReg(debloatedReg, BloatType.ALL);

                if (baseInf.Infs.Count == 1)
                {
                    var list = debloatedReg.ToList();
                    baseInf.Infs[0].InternalRegs.AddRange(Helper.GetConvertedRegKeysIntoInfKeys(list, RegHive.AUTO_DETECT, "", baseInf));
                    //Internal
                }
                else if (baseInf.Infs.Count > 1)
                {
                    var list = debloatedReg.ToList();
                    baseInf.ExternalRegs.AddRange(Helper.GetConvertedRegKeysIntoInfKeys(list, RegHive.AUTO_DETECT, "", baseInf));
                    //ExternalRegs
                }

                return baseInf;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public static bool ExportInf(BaseInf baseInf, string regName, string saveFolder, bool forceArm64 = false)
        {
            var generalSaveDir = Directory.CreateDirectory(Path.Combine(saveFolder, regName.Replace(".reg", "")));
            try
            {
                baseInf.Infs.ForEach(inf => {

                    if (forceArm64)
                        inf.PackageName = inf.PackageName.Replace("arm_", "arm64_");

                    List<string> Sys32_Driver_List = new List<string>();
                    List<string> Sys32_List = new List<string>();

                    StringBuilder sb = new StringBuilder(@";Autogenerated by GetLumiaBSP
;Original name=" + regName + "\r\n\r\n");

                    var saveDir = Directory.CreateDirectory(Path.Combine(generalSaveDir.FullName, inf.PackageName));

                    sb.AppendLine($"[Version]");
                    sb.AppendLine($"Signature       =   \"{inf.Version.Signature}\"");
                    sb.AppendLine($"Class           =   {inf.Version.Class}"); //TODO
                    sb.AppendLine($"ClassGuid       =   {inf.Version.ClassGuid}");
                    sb.AppendLine($"Provider        =   {inf.Version.Provider}");
                    sb.AppendLine($"DriverVer       =   {inf.Version.DriverVer}");
                    sb.AppendLine($"CatalogFile     =   {inf.Version.CatalogFile}");
                    if (inf.Version.PnpLockDown)
                        sb.AppendLine("PnpLockDown     =   1");

                    if (inf.Version.SourceFiles.Count > 0)
                    {
                        sb.AppendLine("\r\n[SourceDisksNames]");
                        sb.AppendLine("1=%diskIns%");
                        sb.AppendLine("\r\n[SourceDisksFiles]");
                        inf.Version.SourceFiles.ForEach(file => sb.AppendLine(file.Split('/').Last() + "=1"));
                        sb.AppendLine("\r\n[DestinationDirs]");



                        inf.Version.SourceFiles.ForEach(file =>
                        {
                            var code = (file.ToLower().Contains("system32/drivers") ? DirCodes.System32_Drivers : DirCodes.System32);

                            if (code == DirCodes.System32_Drivers)
                                Sys32_Driver_List.Add(file.Split('/').Last()); //Drivers_Dir_12
                            else if (code == DirCodes.System32)
                                Sys32_List.Add(file.Split('/').Last()); //Drivers_Dir_11
                        });

                        if (Sys32_Driver_List.Count > 0)
                            sb.AppendLine("Drivers_Dir_12=12");

                        if (Sys32_List.Count > 0)
                            sb.AppendLine("Drivers_Dir_11=11");

                        if (Sys32_Driver_List.Count > 0)
                        {
                            sb.AppendLine("\r\n[Drivers_Dir_12]");
                            Sys32_Driver_List.ForEach(f => sb.AppendLine(f));
                        }

                        if (Sys32_List.Count > 0)
                        {
                            sb.AppendLine("\r\n[Drivers_Dir_11]");
                            Sys32_List.ForEach(f => sb.AppendLine(f));
                        }
                    }

                    var arch = inf.PackageName.ToLower().Contains("arm64_") ? "ARM64" : "ARM";

                    sb.AppendLine("\r\n[Manufacturer]");
                    sb.AppendLine($"{(string.IsNullOrEmpty(inf.Manufacturer) ? "\"somebody\"" : inf.Manufacturer)} = Standard, NT{arch}");


                    if (inf.DeviceIDs.Count > 0)
                    {
                        sb.AppendLine($"\r\n[Standard.NT{arch}]");
                        inf.DeviceIDs.ForEach(deviceId => sb.AppendLine($"{deviceId.Value.Description}      =   {deviceId.Value.Configuration},     {deviceId.Key}"));

                        inf.DeviceIDs.Select(x => x.Value.Configuration.ToLower()).Distinct().ToList().ForEach(deviceId =>
                        {
                            sb.AppendLine($"\r\n[{deviceId}]");
                            if (Sys32_Driver_List.Count > 0)
                                sb.AppendLine("CopyFiles=Drivers_Dir_12");

                            if (Sys32_List.Count > 0)
                                sb.AppendLine("CopyFiles=Drivers_Dir_11");

                            if (inf.InternalRegs.Count > 0)
                                sb.AppendLine("AddReg=DeviceReg");

                            //TODO: Reboot SUPPORT.
                            //if ()
                        });
                    }

                    if (inf.InternalRegs.Count > 0)
                    {
                        sb.AppendLine("\r\n[DeviceReg]");
                        inf.InternalRegs.ForEach(reg => sb.AppendLine(reg));
                    }

                    inf.Configurations.Where(x => string.IsNullOrEmpty(x.ServiceName))
                        .Select(x => x.FromDeviceID).ToList().ForEach(conf =>
                        {
                            var match = inf.DeviceIDs.Where(x => x.Key.ToLower() == conf.ToLower()).ToList();
                            if (match.Count > 0)
                            {
                                sb.AppendLine($"\r\n[{match[0].Value.Configuration}.Services]");
                                sb.AppendLine($"AddService = , 0x00000002;");
                            }
                        });

                    inf.Services.ForEach(service =>
                    {
                        var match = inf.DeviceIDs.Where(x => x.Value.LinkedServiceName == service.ServiceName).Select(x => x.Value.Configuration).Distinct().ToList();
                        if (match.Count > 0)
                        {
                            var distinct = inf.Configurations.Where(confx => confx.ServiceName == service.ServiceName).Select(confx => confx.Properties).Distinct().ToList();

                            if (distinct.Count > 0)
                            {
                                sb.AppendLine("AddReg = WDTFSOCDeviceCategory");
                                sb.AppendLine("\r\n[WDTFSOCDeviceCategory]");
                                distinct[0].ForEach(key => sb.AppendLine(key));
                            }

                            sb.AppendLine($"\r\n[{match[0]}.Services]");
                            sb.AppendLine($"AddService = {service.ServiceName}, 0x00000002, {service.ServiceName}_service_inst"); //TODO: not every driver has SPSVCINST_ASSOCSERVICE

                            sb.AppendLine($"\r\n[{service.ServiceName}_service_inst]");

                            if (!string.IsNullOrEmpty(service.DisplayName))
                                sb.AppendLine($"DisplayName     =   {service.DisplayName}");
                            if (!string.IsNullOrEmpty(service.ServiceType))
                                sb.AppendLine($"ServiceType     =   {service.ServiceType}");
                            if (!string.IsNullOrEmpty(service.StartType))
                                sb.AppendLine($"StartType       =   {service.StartType}");
                            if (!string.IsNullOrEmpty(service.ErrorControl))
                                sb.AppendLine($"ErrorControl    =   {service.ErrorControl}");
                            if (!string.IsNullOrEmpty(service.ServicePath))
                                sb.AppendLine($"ServiceBinary   =   {service.ServicePath.ToLower().Replace(@"\systemroot\", "", StringComparison.InvariantCultureIgnoreCase).Replace("system32\\drivers", "%12%", StringComparison.InvariantCultureIgnoreCase).Replace("system32", "%11%", StringComparison.InvariantCultureIgnoreCase)}");
                            if (!string.IsNullOrEmpty(service.LoadOrderGroup))
                                sb.AppendLine($"LoadOrderGroup  =   {service.LoadOrderGroup}");
                            if (!string.IsNullOrEmpty(service.Tag))
                                sb.AppendLine($"Tag             =   {service.Tag}");

                            if (service.IsWdf)
                            {
                                sb.AppendLine($"\r\n[{match[0]}.Wdf]");
                                sb.AppendLine($"KmdfService      =   {service.ServiceName}, {service.ServiceName}_wdfsect");
                                sb.AppendLine($"\r\n[{service.ServiceName}_wdfsect]");
                                sb.AppendLine($"KmdfLibraryVersion = {service.KmdfLibraryVersion}");
                            }

                            if (service.AddReg.Count > 0)
                            {
                                sb.AppendLine($"AddReg          =   {service.ServiceName}_Reg");
                                sb.AppendLine($"\r\n[{service.ServiceName}_Reg]");
                                service.AddReg.ForEach(reg => sb.AppendLine(reg));

                                //TODO: add regs from driverpackages
                            }
                        }
                    });

                    if (inf.Strings.Count > 0)
                    {
                        sb.AppendLine($"\r\n[Strings]");
                        inf.Strings.ForEach(@string => sb.AppendLine(@string.Key + "=" + @string.Value));
                        sb.AppendLine($"diskIns=\"GetLumiaBSP Installation Disk\"");
                    }

                    File.WriteAllText(Path.Combine(saveDir.FullName, inf.InfName), sb.ToString());
                });

                if (!string.IsNullOrWhiteSpace(baseInf.ParsingLog))
                    File.WriteAllText(Path.Combine(generalSaveDir.FullName, "ParsingFailures.log"), baseInf.ParsingLog);

                if (baseInf.ExternalRegs.Count > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    baseInf.ExternalRegs.ForEach(reg => sb.AppendLine(reg));
                    File.WriteAllText(Path.Combine(generalSaveDir.FullName, "UnknownRegOwners.log"), sb.ToString());
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        #region Key Extraction methods

        public static List<KeyValuePair<string, Hashtable>> GetDriverInfFiles(Dictionary<string, Hashtable> reg)
           => reg.Where(s => s.Key.ToUpper().Contains(@"DRIVERDATABASE\DRIVERINFFILES")).ToList();

        public static List<KeyValuePair<string, Hashtable>> GetDriverWUDF(Dictionary<string, Hashtable> reg)
           => reg.Where(s => s.Key.ToUpper().Contains(@"CURRENTVERSION\WUDF")).ToList();

        public static List<KeyValuePair<string, Hashtable>> GetDriverPackages(Dictionary<string, Hashtable> reg, string packageName)
           => reg.Where(s => s.Key.ToUpper().Contains(@"DRIVERDATABASE\DRIVERPACKAGES\" + packageName.ToUpper())).ToList();

        public static List<KeyValuePair<string, Hashtable>> GetDriverGuids(Dictionary<string, Hashtable> reg)
           => reg.Where(s => s.Key.ToUpper().Contains(@"DRIVERDATABASE\DEVICEIDS\{")).ToList();

        public static List<KeyValuePair<string, Hashtable>> GetDriverIds(Dictionary<string, Hashtable> reg)
           => reg.Where(s => s.Key.ToUpper().Contains(@"DRIVERDATABASE\DEVICEIDS\") && !(s.Key.ToUpper().Contains(@"DRIVERDATABASE\DEVICEIDS\{"))).ToList();

        public static List<KeyValuePair<string, Hashtable>> GetDriverPackagesDescriptors(Dictionary<string, Hashtable> reg, string packageName, string device)
           => reg.Where(s => s.Key.ToUpper().Contains(@"DRIVERDATABASE\DRIVERPACKAGES\" + packageName.ToUpper() + @"\DESCRIPTORS\" + device.ToUpper())).ToList();

        public static List<KeyValuePair<string, Hashtable>> GetDriverPackagesConfigurations(Dictionary<string, Hashtable> reg, string packageName, string configName)
           => reg.Where(s => s.Key.ToUpper().EndsWith(@"DRIVERDATABASE\DRIVERPACKAGES\" + packageName.ToUpper() + @"\CONFIGURATIONS\" + configName.ToUpper())).ToList();

        public static List<KeyValuePair<string, Hashtable>> GetDriverPackagesConfigurationProperties(Dictionary<string, Hashtable> reg, string packageName, string configName)
           => reg.Where(s => s.Key.ToUpper().Contains(@"DRIVERDATABASE\DRIVERPACKAGES\" + packageName.ToUpper() + @"\CONFIGURATIONS\" + configName.ToUpper() + @"\PROPERTIES")).ToList();

        public static List<KeyValuePair<string, Hashtable>> GetDriverPackagesConfigurationServices(Dictionary<string, Hashtable> reg, string packageName, string configName, string serviceName)
           => reg.Where(s => s.Key.ToUpper().Contains(@"DRIVERDATABASE\DRIVERPACKAGES\" + packageName.ToUpper() + @"\CONFIGURATIONS\" + configName.ToUpper() + @"\SERVICES\" + serviceName.ToUpper())).ToList();

        public static List<KeyValuePair<string, Hashtable>> GetDriverPackagesConfigurationGeneric(Dictionary<string, Hashtable> reg, string packageName, string configName)
           => reg.Where(s => s.Key.ToUpper().Contains(@"DRIVERDATABASE\DRIVERPACKAGES\" + packageName.ToUpper() + @"\CONFIGURATIONS\" + configName.ToUpper() + @"\") &&
                            !(s.Key.ToUpper().Contains(@"DRIVERDATABASE\DRIVERPACKAGES\" + packageName.ToUpper() + @"\CONFIGURATIONS\" + configName.ToUpper() + @"\PROPERTIES")) &&
                            !(s.Key.ToUpper().Contains(@"DRIVERDATABASE\DRIVERPACKAGES\" + packageName.ToUpper() + @"\CONFIGURATIONS\" + configName.ToUpper() + @"\PROPERTIES\WDF")) &&
                            !(s.Key.ToUpper().Contains(@"DRIVERDATABASE\DRIVERPACKAGES\" + packageName.ToUpper() + @"\CONFIGURATIONS\" + configName.ToUpper() + @"\SERVICES"))).ToList();

        public static List<KeyValuePair<string, Hashtable>> GetDriverPackagesStrings(Dictionary<string, Hashtable> reg, string packageName)
           => reg.Where(s => s.Key.ToUpper().Contains(@"DRIVERDATABASE\DRIVERPACKAGES\" + packageName.ToUpper() + @"\STRINGS")).ToList();

        public static List<KeyValuePair<string, Hashtable>> GetServiceByName(Dictionary<string, Hashtable> reg, string serviceName)
           => reg.Where(s => s.Key.ToUpper().EndsWith(@"CURRENTCONTROLSET\SERVICES\" + serviceName.ToUpper())).ToList();

        public static List<KeyValuePair<string, Hashtable>> GetRegsByServiceName(Dictionary<string, Hashtable> reg, string serviceName)
           => reg.Where(s => s.Key.ToUpper().Contains(@"CURRENTCONTROLSET\SERVICES\" + serviceName.ToUpper() + @"\") && 
                            !s.Key.ToUpper().Contains(@"CURRENTCONTROLSET\SERVICES\" + serviceName.ToUpper() + @"\PARAMETERS\WDF")).ToList();

        public static List<KeyValuePair<string, Hashtable>> GetWDFByServiceName(Dictionary<string, Hashtable> reg, string serviceName)
           => reg.Where(s => s.Key.ToUpper().Contains(@"CURRENTCONTROLSET\SERVICES\" + serviceName.ToUpper() + @"\PARAMETERS\WDF")).ToList();

        public static List<KeyValuePair<string, Hashtable>> GetPnpLockDownFiles(Dictionary<string, Hashtable> reg)
           => reg.Where(s => s.Key.ToUpper().Contains(@"WINDOWS\CURRENTVERSION\SETUP\PNPLOCKDOWNFILES\")).ToList();

        #endregion
    }

    public static class Helper
    {
        public static string GetPartAfterTo(this string str, string part)
        {
            var index = str.LastIndexOf(part, StringComparison.InvariantCultureIgnoreCase);

            if (index + part.Length == str.Length)
                return "";

            str = str.Substring(index + part.Length + 1);
            return str;
        }

        public static byte[] StringToByteArray(string hex)
        {
            int NumberChars = hex.Length;
            byte[] bytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }

        public static string[] CleanSplitHexString(string hex)
        {
            if (hex.ToUpper().Contains("HEX:"))
                return hex.Replace("hex:", "", StringComparison.InvariantCultureIgnoreCase).Replace(" ", "").Split(',');
            else if (hex.ToUpper().Contains("HEX(2):"))
                return hex.Replace("hex(2):", "", StringComparison.InvariantCultureIgnoreCase).Replace(" ", "").Split(',');
            else if (hex.ToUpper().Contains("HEX(7):"))
                return hex.Replace("hex(7):", "", StringComparison.InvariantCultureIgnoreCase).Replace(" ", "").Split(',');

            Debugger.Break();

            return hex.Replace("hex(7):", "", StringComparison.InvariantCultureIgnoreCase).Split(',');
        }

        public static string CleanHexString(string hex)
        {
            if (hex.ToUpper().Contains("HEX:"))
                return hex.Replace("hex:", "", StringComparison.InvariantCultureIgnoreCase).Replace(",", "");
            else if (hex.ToUpper().Contains("HEX(2):"))
                return hex.Replace("hex(2):", "", StringComparison.InvariantCultureIgnoreCase).Replace(",", "");
            else if (hex.ToUpper().Contains("HEX(7):"))
                return hex.Replace("hex(7):", "", StringComparison.InvariantCultureIgnoreCase).Replace(",", "");
            else if (hex.ToUpper().Contains("HEX(FFFF"))
                return hex.Replace("hex(FFFF", "", StringComparison.InvariantCultureIgnoreCase).Replace(")", "").Replace(",", "");
            else if (!hex.ToUpper().StartsWith("H"))
                return hex.Replace(",", "");

            Debugger.Break();

            return hex.Replace("hex(7):", "", StringComparison.InvariantCultureIgnoreCase).Replace(",", "");
        }

        public static string StringArrayHexToString(string[] hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex[i * 2].Substring(0, 2), 16);

            return Encoding.UTF8.GetString(bytes);
        }

        public static string GetProperDriverVer(byte[] bytes)
        {
            return DateTime.FromFileTimeUtc(BitConverter.ToInt64(bytes, 0x18)).ToString("MM/dd/yyyy")
                + "," + BitConverter.ToInt16(bytes, 0x26)
                + "." + BitConverter.ToInt16(bytes, 0x24)
                + "." + BitConverter.ToInt16(bytes, 0x22)
                + "." + BitConverter.ToInt16(bytes, 0x20).ToString("D4");
        }

        public static List<string> GetConvertedRegKeysIntoInfKeys(List<KeyValuePair<string, Hashtable>> regs, RegHive kind, string relativeTo = "", BaseInf baseInf = null)
        {
            var list = new List<string>();

            regs.ForEach(reg =>
            {
                var k = "";

                switch (kind)
                {
                    case RegHive.HKLM:
                        k = kind + "," + reg.Key.GetPartAfterTo("HKEY_LOCAL_MACHINE").Replace(Reg2Inf.SystemNameKey, "SYSTEM", StringComparison.InvariantCultureIgnoreCase).Replace(Reg2Inf.SoftwareNameKey, "SOFTWARE", StringComparison.InvariantCultureIgnoreCase);
                        break;
                    case RegHive.HKCU:
                        k = kind + "," + reg.Key.GetPartAfterTo("HKEY_CURRENT_USER").Replace(Reg2Inf.SystemNameKey, "SYSTEM", StringComparison.InvariantCultureIgnoreCase).Replace(Reg2Inf.SoftwareNameKey, "SOFTWARE", StringComparison.InvariantCultureIgnoreCase);
                        break;
                    case RegHive.HKCR:
                        k = kind + "," + reg.Key.GetPartAfterTo("HKEY_CLASSES_ROOT").Replace(Reg2Inf.SystemNameKey, "SYSTEM", StringComparison.InvariantCultureIgnoreCase).Replace(Reg2Inf.SoftwareNameKey, "SOFTWARE", StringComparison.InvariantCultureIgnoreCase);
                        break;
                    case RegHive.HKU:
                        k = kind + "," + reg.Key.GetPartAfterTo("HKEY_USERS").Replace(Reg2Inf.SystemNameKey, "SYSTEM", StringComparison.InvariantCultureIgnoreCase).Replace(Reg2Inf.SoftwareNameKey, "SOFTWARE", StringComparison.InvariantCultureIgnoreCase);
                        break;
                    case RegHive.HKR:
                        k = kind + "," + reg.Key.GetPartAfterTo(relativeTo).Replace(Reg2Inf.SystemNameKey, "SYSTEM", StringComparison.InvariantCultureIgnoreCase).Replace(Reg2Inf.SoftwareNameKey, "SOFTWARE", StringComparison.InvariantCultureIgnoreCase);
                        break;
                    case RegHive.NONE:
                        k = reg.Key.GetPartAfterTo(relativeTo).Replace(Reg2Inf.SystemNameKey, "SYSTEM", StringComparison.InvariantCultureIgnoreCase).Replace(Reg2Inf.SoftwareNameKey, "SOFTWARE", StringComparison.InvariantCultureIgnoreCase);
                        break;
                    case RegHive.AUTO_DETECT:
                        k = (reg.Key.ToUpper().StartsWith("HKEY_LOCAL_MACHINE") ? "HKLM" : reg.Key.ToUpper().StartsWith("HKEY_CURRENT_USER") ? "HKCU" :
                            reg.Key.ToUpper().StartsWith("HKEY_CLASSES_ROOT") ? "HKCR" : "HKU") + "," +
                            reg.Key.Replace("HKEY_LOCAL_MACHINE\\", "", StringComparison.InvariantCultureIgnoreCase).Replace("HKEY_CURRENT_USER\\", "", StringComparison.InvariantCultureIgnoreCase)
                            .Replace("HKEY_CLASSES_ROOT\\", "", StringComparison.InvariantCultureIgnoreCase).Replace("HKEY_USERS\\", "", StringComparison.InvariantCultureIgnoreCase)
                            .Replace(Reg2Inf.SystemNameKey, "SYSTEM", StringComparison.InvariantCultureIgnoreCase).Replace(Reg2Inf.SoftwareNameKey, "SOFTWARE", StringComparison.InvariantCultureIgnoreCase);
                        break;
                    default:
                        break;
                }

                foreach (DictionaryEntry keynamevalue in reg.Value)
                {
                    var key = (keynamevalue.Key as string).Replace("@", "");

                    if ((keynamevalue.Value as string).ToLower().Contains("dword:")) //REG_DWORD
                        list.Add(k + "," + key + "," + "0x00010001,0x" + (keynamevalue.Value as string).Split(':')[1]);
                    else if ((keynamevalue.Value as string).ToLower().Contains("hex:")) //REG_BINARY
                        list.Add(k + "," + key + "," + "0x00000001," + string.Join(",", CleanSplitHexString(keynamevalue.Value as string)));
                    else if ((keynamevalue.Value as string).ToLower().Contains("\"")) //REG_SZ
                        list.Add(k + "," + key + "," + "0x00000000," + (keynamevalue.Value as string).Replace("\\\\", "\\"));
                    else if ((keynamevalue.Value as string).ToLower().Contains("hex(2)")) //REG_MULTI_SZ
                        list.Add(k + "," + key + "," + "0x00020000,\"" + StringArrayHexToString(CleanSplitHexString(keynamevalue.Value as string)).Replace("%", "%%") + "\"");
                    else if ((keynamevalue.Value as string).ToLower().Contains("hex(7)")) //REG_EXPAND_SZ
                    {
                        string tmp = "";
                        StringArrayHexToString(CleanSplitHexString(keynamevalue.Value as string)).Replace("\r", "").Split('\n').ToList().ForEach(s => tmp += $"\"{s}\",");

                        if (tmp.EndsWith(","))
                            tmp = tmp.Substring(0, tmp.Length - 1);

                        list.Add(k + "," + key + "," + "0x00010000," + tmp);
                    }
                    else
                    {
                        //Console.WriteLine("(reg2inf) unrecognized key: " + keynamevalue.Value);
                        if (baseInf != null)
                            baseInf.ParsingLog += $"{reg.Key.Replace(Reg2Inf.SystemNameKey, "SYSTEM", StringComparison.InvariantCultureIgnoreCase).Replace(Reg2Inf.SoftwareNameKey, "SOFTWARE", StringComparison.InvariantCultureIgnoreCase)} --> {keynamevalue.Key}={keynamevalue.Value}\r\n";
                    }

                }
            });

            return list;
        }

        public static List<string> GetConvertedRegKeysIntoInfKeysForProperties(List<KeyValuePair<string, Hashtable>> regs)
        {
            var list = new List<string>();

            regs.ForEach(reg =>
            {
                var k = reg.Key.GetPartAfterTo("PROPERTIES").Replace("\\", ",");

                foreach (DictionaryEntry keynamevalue in reg.Value)
                {
                    var cos = (keynamevalue.Value as string).Split(':');
                    list.Add(k + "," + CleanHexString(cos[0]) + ",," + cos[1].Split(',')[0]);
                }
            });

            return list;
        }

        public static string GetClassFromGuid(string guid)
        {
            Dictionary<Guid, string> dcgtn = new Dictionary<Guid, string>();

            #region Establish known classes
            dcgtn[new Guid("{72631e54-78a4-11d0-bcf7-00aa00b7b32a}")] = "Battery";
            dcgtn[new Guid("{5630831C-06C9-4856-B327-F5D32586E060}")] = "Proximity";
            dcgtn[new Guid("{53D29EF7-377C-4D14-864B-EB3A85769359}")] = "Biometric";
            dcgtn[new Guid("{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}")] = "Bluetooth";
            dcgtn[new Guid("{4d36e965-e325-11ce-bfc1-08002be10318}")] = "CDROM";
            dcgtn[new Guid("{4d36e967-e325-11ce-bfc1-08002be10318}")] = "DiskDrive";
            dcgtn[new Guid("{4d36e968-e325-11ce-bfc1-08002be10318}")] = "Display";
            dcgtn[new Guid("{e2f84ce7-8efa-411c-aa69-97454ca4cb57}")] = "Extension";
            dcgtn[new Guid("{4d36e969-e325-11ce-bfc1-08002be10318}")] = "FDC";
            dcgtn[new Guid("{4d36e980-e325-11ce-bfc1-08002be10318}")] = "FloppyDisk";
            dcgtn[new Guid("{6bdd1fc3-810f-11d0-bec7-08002be2092f}")] = "GPS";
            dcgtn[new Guid("{4d36e96a-e325-11ce-bfc1-08002be10318}")] = "HDC";
            dcgtn[new Guid("{745a17a0-74d3-11d0-b6fe-00a0c90f57da}")] = "HIDClass";
            dcgtn[new Guid("{48721b56-6795-11d2-b1a8-0080c72e74a2}")] = "Dot4";
            dcgtn[new Guid("{49ce6ac8-6f86-11d2-b1e5-0080c72e74a2}")] = "Dot4Print";
            dcgtn[new Guid("{7ebefbc0-3200-11d2-b4c2-00a0C9697d07}")] = "61883";
            dcgtn[new Guid("{c06ff265-ae09-48f0-812c-16753d7cba83}")] = "AVC";
            dcgtn[new Guid("{d48179be-ec20-11d1-b6b8-00c04fa372a7}")] = "SBP2";
            dcgtn[new Guid("{6bdd1fc1-810f-11d0-bec7-08002be2092f}")] = "1394";
            dcgtn[new Guid("{6bdd1fc6-810f-11d0-bec7-08002be2092f}")] = "Image";
            dcgtn[new Guid("{6bdd1fc5-810f-11d0-bec7-08002be2092f}")] = "Infrared";
            dcgtn[new Guid("{4d36e96b-e325-11ce-bfc1-08002be10318}")] = "Keyboard";
            dcgtn[new Guid("{ce5939ae-ebde-11d0-b181-0000f8753ec4}")] = "MediumChanger";
            dcgtn[new Guid("{4d36e970-e325-11ce-bfc1-08002be10318}")] = "MTD";
            dcgtn[new Guid("{4d36e96d-e325-11ce-bfc1-08002be10318}")] = "Modem";
            dcgtn[new Guid("{4d36e96e-e325-11ce-bfc1-08002be10318}")] = "Monitor";
            dcgtn[new Guid("{4d36e96f-e325-11ce-bfc1-08002be10318}")] = "Mouse";
            dcgtn[new Guid("{4d36e971-e325-11ce-bfc1-08002be10318}")] = "Multifunction";
            dcgtn[new Guid("{4d36e96c-e325-11ce-bfc1-08002be10318}")] = "Media";
            dcgtn[new Guid("{50906cb8-ba12-11d1-bf5d-0000f805f530}")] = "MultiportSerial";
            dcgtn[new Guid("{4d36e972-e325-11ce-bfc1-08002be10318}")] = "Net";
            dcgtn[new Guid("{4d36e973-e325-11ce-bfc1-08002be10318}")] = "NetClient";
            dcgtn[new Guid("{4d36e974-e325-11ce-bfc1-08002be10318}")] = "NetService";
            dcgtn[new Guid("{4d36e975-e325-11ce-bfc1-08002be10318}")] = "NetTrans";
            dcgtn[new Guid("{268c95a1-edfe-11d3-95c3-0010dc4050a5}")] = "SecurityAccelerator";
            dcgtn[new Guid("{4d36e977-e325-11ce-bfc1-08002be10318}")] = "PCMCIA";
            dcgtn[new Guid("{4d36e978-e325-11ce-bfc1-08002be10318}")] = "Ports";
            dcgtn[new Guid("{4d36e979-e325-11ce-bfc1-08002be10318}")] = "Printer";
            dcgtn[new Guid("{4658ee7e-f050-11d1-b6bd-00c04fa372a7}")] = "PNPPrinters";
            dcgtn[new Guid("{50127dc3-0f36-415e-a6cc-4cb3be910b65}")] = "Processor";
            dcgtn[new Guid("{4d36e97b-e325-11ce-bfc1-08002be10318}")] = "SCSIAdapter";
            dcgtn[new Guid("{5175d334-c371-4806-b3ba-71fd53c9258d}")] = "Sensor";
            dcgtn[new Guid("{50dd5230-ba8a-11d1-bf5d-0000f805f530}")] = "SmartCardReader";
            dcgtn[new Guid("{5c4c3332-344d-483c-8739-259e934c9cc8}")] = "SoftwareComponent";
            dcgtn[new Guid("{71a27cdd-812a-11d0-bec7-08002be2092f}")] = "Volume";
            dcgtn[new Guid("{4d36e97d-e325-11ce-bfc1-08002be10318}")] = "System";
            dcgtn[new Guid("{6d807884-7d21-11cf-801c-08002be10318}")] = "TapeDrive";
            dcgtn[new Guid("{88BAE032-5A81-49f0-BC3D-A4FF138216D6}")] = "USBDevice";
            dcgtn[new Guid("{25dbce51-6c8f-4a72-8a6d-b54c2b4fc835}")] = "WCEUSBS";
            dcgtn[new Guid("{eec5ad98-8080-425f-922a-dabf3de3f69a}")] = "WPD";
            dcgtn[new Guid("{997b5d8d-c442-4f2e-baf3-9c8e671e9e21}")] = "SideShow";
            dcgtn[new Guid("{36FC9E60-C465-11CF-8056-444553540000}")] = "USB";
            #endregion

            return dcgtn[new Guid(guid)];
        }

        public static string Replace(this string str, string oldValue, string @newValue, StringComparison comparisonType)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));
            if (str.Length == 0)
                return str;
            if (oldValue == null)
                throw new ArgumentNullException(nameof(oldValue));
            if (oldValue.Length == 0)
                throw new ArgumentException("String cannot be of zero length.");

            StringBuilder resultStringBuilder = new StringBuilder(str.Length);

            bool isReplacementNullOrEmpty = string.IsNullOrEmpty(@newValue);

            const int valueNotFound = -1;
            int foundAt;
            int startSearchFromIndex = 0;
            while ((foundAt = str.IndexOf(oldValue, startSearchFromIndex, comparisonType)) != valueNotFound)
            {
                int @charsUntilReplacment = foundAt - startSearchFromIndex;
                bool isNothingToAppend = @charsUntilReplacment == 0;
                if (!isNothingToAppend)
                    resultStringBuilder.Append(str, startSearchFromIndex, @charsUntilReplacment);

                if (!isReplacementNullOrEmpty)
                    resultStringBuilder.Append(@newValue);

                startSearchFromIndex = foundAt + oldValue.Length;
                if (startSearchFromIndex == str.Length)
                    return resultStringBuilder.ToString();
            }
            int @charsUntilStringEnd = str.Length - startSearchFromIndex;
            resultStringBuilder.Append(str, startSearchFromIndex, @charsUntilStringEnd);

            return resultStringBuilder.ToString();
        }
    }

    public enum RegHive
    {
        HKLM, //HKEY_LOCAL_MACHINE
        HKCU, //HKEY_CURRENT_USER
        HKCR, //HKEY_CLASSES_ROOT
        HKU, //HKEY_USERS
        HKR, //relative
        NONE,
        AUTO_DETECT
    }

    public enum DirCodes
    {
        System32_Drivers = 12,
        System32 = 11
    }
}
