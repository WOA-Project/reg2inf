using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ADXReg2Inf
{
    public class BaseInf
    {
        public List<Inf> Infs { get; set; }
        public List<string> ExternalRegs { get; set; }
        public string ParsingLog { get; set; }
    }

    public class Inf
    {
        public string Manufacturer { get; set; }
        public List<string> InternalRegs { get; set; }
        public List<KeyValuePair<string, Descriptor>> DeviceIDs { get; set; }
        public string InfName { get; set; }
        public string PackageName { get; set; }
        public List<Configuration> Configurations { get; set; }
        public Version Version { get; set; }
        public List<KeyValuePair<string, string>> Strings { get; set; }
        public List<Service> Services { get; set; }
    }

    public class Service
    {
        public string ServiceName { get; set; }
        public string DisplayName { get; set; }
        public string ServiceType { get; set; }
        public string StartType { get; set; }
        public string ErrorControl { get; set; }
        public string LoadOrderGroup { get; set; }
        public string Tag { get; set; }
        public string ServicePath { get; set; } //ImagePath
        public bool IsWdf { get; set; }
        public string KmdfLibraryVersion { get; set; }
        public List<string> AddReg { get; set; }
    }

    public class Configuration
    {
        public string FromDeviceID { get; set; }
        public string ServiceName { get; set; }
        public List<string> Properties { get; set; }
        public List<string> Keys { get; set; }
        public List<Interface> Interfaces { get; set; }
    }

    public class Interface
    {
        public string KSCategoryGuid { get; set; }
        public List<Tuple<string, List<string>>> RefKeys { get; set; }
    }

    public class Descriptor
    {
        public string LinkedServiceName { get; set; }
        public string Configuration { get; set; }
        public string Description { get; set; }
        public string Manufacturer { get; set; }
    }

    public class Version
    {
        public string Signature { get; set; }
        public string Class { get; set; }
        public string ClassGuid { get; set; }
        public string Provider { get; set; }
        public string DriverVer { get; set; }
        public string CatalogFile { get; set; }
        public bool PnpLockDown { get; set; }
        public List<string> SourceFiles { get; set; }
    }
}
