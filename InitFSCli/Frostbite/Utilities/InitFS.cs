using Frost4.Frostbite.Utilities.BinaryFile.Surface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace InitFSCli.Frostbite.Utilities
{
    public class InitFS
    {
        private readonly string MetaHeading = "# Created from Frost4's InitFSCli, is used later for repacking.\n" +
            "# DO NOT DELETE IF YOU WANT TO REPACK";
        private readonly string MetaFileName = "META.TXT";

        public string Name { get; set; }
        public bool IsWin32 { get; set; }
        public byte[] Keys { get; set; }
        public List<Entry> FileEntryList { get; set; }

        public InitFS(string input)
        {
            if (File.Exists(input))
            {
                ReadFromFile(input);
            }
            else if (Directory.Exists(input))
            {
                ReadFromDirectory(input);
            }
            else
            {
                throw new ArgumentException("Data does not point to an existing file or directory on the system.", "input");
            }
        }

        public void ExtractAllData(string directory)
        {
            directory += '\\';

            Directory.CreateDirectory(directory);
            string metadata = $"{MetaHeading}\n\n" +
                $"{Name}\n" +
                $"{IsWin32}\n" +
                Convert.ToBase64String(Keys);

            foreach (Entry fileEntry in FileEntryList)
            {
                if (fileEntry.Data is List<Flag> flagList)
                {
                    foreach (Flag flag in flagList)
                    {
                        if (flag.Name.Equals("$file") && flag.Data is List<Flag> subFlagList)
                        {
                            string fs = string.Empty;
                            string name = null;
                            byte[] payload = null;

                            foreach (Flag subFlag in subFlagList)
                            {
                                switch (subFlag.Name)
                                {
                                    case "fs":
                                        fs = (string)subFlag.Data;
                                        break;
                                    case "name":
                                        name = (string)subFlag.Data;
                                        break;
                                    case "payload":
                                        payload = (byte[])subFlag.Data;
                                        break;
                                }
                            }

                            if (string.IsNullOrEmpty(name))
                            {
                                ThrowNullReferenceException("name");
                            }

                            if (payload == null)
                            {
                                ThrowNullReferenceException("payload");
                            }

                            metadata += $"\n\n{name}";
                            if (!string.IsNullOrEmpty(fs))
                            {
                                metadata += $"\n{fs}";
                            }

                            string nameDirectory = Path.GetDirectoryName(name);
                            Directory.CreateDirectory(directory + nameDirectory);
                            File.WriteAllBytes(directory + name, payload);
                        }
                    }
                }
            }

            File.WriteAllText($"{directory}\\{MetaFileName}", metadata);
        }

        public void PackData(string path)
        {
            var primaryEntry = new Entry
            {
                Format = EntryFormat.EntryArray,
                Data = FileEntryList,
            };

            primaryEntry.Write(path, IsWin32, Keys);
        }

        private void ReadFromFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new ArgumentException("Data does not point to a valid file on the system.", "path");
            }

            using var reader = new BinaryReader(File.OpenRead(path));

            Name = Path.GetFileName(path);
            IsWin32 = SurfaceFile.IsHeaderPresent(reader);
            Keys = IsWin32 ? reader.ReadBytes(SurfaceConstants.KeysLength) : SurfaceFile.GetBlankKeys;
            FileEntryList = (List<Entry>)SurfaceFile.CreateEntry(path, IsWin32).Data;
        }

        private void ReadFromDirectory(string directory)
        {
            string metaPath = $"{directory}\\{MetaFileName}";

            if (!File.Exists(metaPath))
            {
                throw new ArgumentException("Data does not point to a valid initfs directory on the system.", "directory");
            }

            string[] metadataText = File.ReadAllLines(metaPath).Skip(3).ToArray();
            string initFSName = metadataText[0];
            bool isWin32 = bool.Parse(metadataText[1]);
            byte[] keys = isWin32 ? Convert.FromBase64String(metadataText[2]) : SurfaceFile.GetBlankKeys;
            metadataText = metadataText.Skip(4).ToArray();

            var fileEntryList = new List<Entry>();

            for (uint i = 0; i < metadataText.Length; i++)
            {
                string filePath = metadataText[i];

                i++;
                string fs = i < metadataText.Length ? metadataText[i] : null;
                i += string.IsNullOrEmpty(fs) ? 0 : 1;

                var fsFlag = new Flag
                {
                    Format = FlagFormat.String,
                    Name = "fs",
                    Data = fs,
                };

                var nameFlag = new Flag
                {
                    Format = FlagFormat.String,
                    Name = "name",
                    Data = filePath
                };

                var payloadFlag = new Flag
                {
                    Format = FlagFormat.ByteArray,
                    Name = "payload",
                    Data = File.ReadAllBytes($"{directory}\\{filePath}")
                };

                var subFlagList = new List<Flag>();

                if (!string.IsNullOrEmpty(fs))
                {
                    subFlagList.Add(fsFlag);
                }

                subFlagList.Add(nameFlag);
                subFlagList.Add(payloadFlag);

                var flagList = new List<Flag>()
                {
                    new Flag()
                    {
                        Format = FlagFormat.FlagList,
                        Name = "$file",
                        Data = subFlagList,
                    },
                };

                var fileEntry = new Entry
                {
                    Format = EntryFormat.FlagList,
                    Data = flagList,
                };

                fileEntryList.Add(fileEntry);
            }

            Name = initFSName;
            IsWin32 = isWin32;
            Keys = keys;
            FileEntryList = fileEntryList;
        }

        private void ThrowNullReferenceException(string flag)
        {
            throw new NullReferenceException($"Flag \"{flag}\" not found or contained null value, not expected.");
        }
    }
}
