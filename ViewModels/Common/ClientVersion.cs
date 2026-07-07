using System;
using System.Collections.Generic;
using System.IO;
using Tomlyn;

namespace NyxAssetsEditor.ViewModels.Common
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

	public class ClientVersion
	{
		public string DisplayName { get; }
		public uint Version { get; }
		public uint DatSignature { get; }
		public uint SprSignature { get; }

		public ClientVersion(string displayName, uint version, uint datSignature, uint sprSignature)
		{
			DisplayName = displayName;
			Version = version;
			DatSignature = datSignature;
			SprSignature = sprSignature;
		}

		private static List<ClientVersion>? _availableVersions;

		public static List<ClientVersion> AvailableVersions
		{
			get
			{
				if (_availableVersions == null)
				{
					_availableVersions = LoadVersions();
				}
				return _availableVersions;
			}
		}

		private static List<ClientVersion> LoadVersions()
		{
			try
			{
				using (var stream = Avalonia.Platform.AssetLoader.Open(new System.Uri("avares://NyxAssetsEditor/Assets/signatures.toml")))
				using (var reader = new StreamReader(stream))
				{
					string toml = reader.ReadToEnd();
					var model = TomlSerializer.Deserialize<VersionTomlModel>(toml);
					var list = new List<ClientVersion>();
					if (model != null && model.versions != null)
					{
						foreach (var entry in model.versions)
						{
							uint datSig = 0;
							uint sprSig = 0;
							if (!string.IsNullOrEmpty(entry.dat))
								uint.TryParse(entry.dat, System.Globalization.NumberStyles.HexNumber, null, out datSig);
							if (!string.IsNullOrEmpty(entry.spr))
								uint.TryParse(entry.spr, System.Globalization.NumberStyles.HexNumber, null, out sprSig);

							list.Add(new ClientVersion(entry.@string, entry.value, datSig, sprSig));
						}
					}
					return list;
				}
			}
			catch (System.Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to load signatures.toml: {ex.Message}");
				return new List<ClientVersion>
				{
					new ClientVersion("10.98", 1098, 0, 0),
					new ClientVersion("8.60", 860, 0, 0),
					new ClientVersion("7.60", 760, 0, 0)
				};
			}
		}
	}
}
