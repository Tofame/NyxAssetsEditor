using System;
using System.IO;
using System.Collections.Generic;
using Tomlyn;

namespace InspectApp
{
    public class VersionTomlModel
    {
        public List<VersionEntryModel> versions { get; set; } = new List<VersionEntryModel>();
    }

    public class VersionEntryModel
    {
        public uint value { get; set; }
        public string @string { get; set; } = "";
        public string dat { get; set; } = "";
        public string spr { get; set; } = "";
        public int otb { get; set; }
    }

    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "../../Assets/signatures.toml");
                if (!File.Exists(path))
                {
                    path = "Assets/signatures.toml";
                }
                Console.WriteLine("Reading signatures.toml from: " + Path.GetFullPath(path));
                string toml = File.ReadAllText(path);
                Console.WriteLine("Read TOML length: " + toml.Length);

                var model = TomlSerializer.Deserialize<VersionTomlModel>(toml);
                Console.WriteLine("Model deserialized successfully!");
                if (model == null)
                {
                    Console.WriteLine("Model is null!");
                }
                else
                {
                    Console.WriteLine($"Versions count: {model.versions?.Count}");
                    if (model.versions != null)
                    {
                        foreach (var v in model.versions)
                        {
                            Console.WriteLine($"Parsed version: {v.@string} (value: {v.value})");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION CAUGHT:");
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
