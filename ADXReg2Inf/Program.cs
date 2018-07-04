using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ADXReg2Inf
{
    class Program
    {
        static void Main(string[] args)
        {
            string inputDir = @"C:\IT-Repos\GetLumiaBSP\GetLumiaBSP\bin\Debug\out\Registry";
            string outputDir = @"E:\Happanero_8.1_ARM64\L950XL";

            var files = System.IO.Directory.EnumerateFiles(inputDir, "*.reg", System.IO.SearchOption.TopDirectoryOnly).ToList();

            foreach (var file in files)
            {
                var reg = RegImporter.GetRegistry(file);

                if (reg == null)
                {
                    Console.WriteLine($"(reg2inf) Cannot read {file}");
                    continue;
                }

                var inf = Reg2Inf.GenerateBaseInf(reg);

                if (inf == null)
                {
                    Console.WriteLine($"(reg2inf) Error extracting from {file}");
                    continue;
                }

                if (inf.Infs.Count > 0)
                {
                    var result = Reg2Inf.ExportInf(inf, file.Split('\\').Last(), outputDir);
                    if (result)
                        Console.WriteLine($"(reg2inf) Exported to {file.Split('\\').Last().Replace(".reg", "")}");
                    else
                        Console.WriteLine($"(reg2inf) Failed to export to {file.Split('\\').Last().Replace(".reg", "")} folder");
                }
            }
            Console.ReadKey();
        }

    }
}
