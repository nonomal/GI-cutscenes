﻿using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GICutscenes.FileTypes;

namespace GICutscenes
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(VersionList), GenerationMode = JsonSourceGenerationMode.Metadata)]
    internal partial class VersionJson : JsonSerializerContext
    { }

    internal class VersionList
    {
        public Version[]? list { get; set; }
    }
    internal class Version
    {
        public string? version { get; set; }
        public string[]? videos { get; set; }
        public Version[]? videoGroups { get; set; }
        public ulong? key { get; set; }
        public bool? encAudio { get; set; }
    }
    internal class Demuxer
    {
        //private bool audioEnc = false;
        private static ulong EncryptionKeyInFilename(string filename)
        {
            filename = Path.GetFileNameWithoutExtension(filename);
            string[] intros = { "MDAQ001_OPNew_Part1", "MDAQ001_OPNew_Part2_PlayerBoy", "MDAQ001_OPNew_Part2_PlayerGirl" };
            if (intros.Contains(filename))
            {
                filename = "MDAQ001_OP";
            }
            ulong sum = 0;

            foreach (char c in filename) sum = c + 3 * sum;

            sum &= 0xFFFFFFFFFFFFFF;
            ulong result = 0x100000000000000;
            if (sum > 0) result = sum;
            return result;
        }

        private static (ulong, bool)? EncryptionKeyInBLK(string videoFilename)
        {
            var versionsFilePath = Path.Combine(AppContext.BaseDirectory, "versions.json");
            if (!File.Exists(versionsFilePath)) throw new FileNotFoundException("File versions.json couldn't be found in the folder of the tool.");
            videoFilename = Path.GetFileNameWithoutExtension(videoFilename);
            string jsonString = File.ReadAllText(versionsFilePath);
            VersionList? versions = JsonSerializer.Deserialize<VersionList>(jsonString, VersionJson.Default.VersionList);
            if (versions?.list == null) throw new JsonException("Json content from versions.json is invalid or couldn't be parsed...");
            Version? v = Array.Find(versions.list, x => (x.videos != null && x.videos.Contains(videoFilename)) || (x.videoGroups != null && Array.Exists(x.videoGroups, y => y.videos != null && y.videos.Contains(videoFilename))));
            if (v == null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Unable to find the second key in versions.json for " + videoFilename);
                Console.ResetColor();
                return null;
            }
            ulong key = v.key ?? 0;
            if (v.videoGroups != null)
            {
                key = Array.Find(v.videoGroups, y => y.videos != null && y.videos.Contains(videoFilename))?.key ?? throw new KeyNotFoundException("Unable to find the second key in versions.json for " + videoFilename);
            }
            return (key, v.encAudio ?? false);
        }

        public static ulong? EncryptionKey(string videoFilename)
        {
            ulong key1 = EncryptionKeyInFilename(videoFilename);
            (ulong, bool)? blk = EncryptionKeyInBLK(videoFilename);
            if (blk == null) return null;
            ulong key2 = blk.Value.Item1;
            //audioEnc = blk.Value.Item2;

            ulong finalKey = 0x100000000000000;
            if ((key1 + key2 & 0xFFFFFFFFFFFFFF) != 0) finalKey = key1 + key2 & 0xFFFFFFFFFFFFFF;
            return finalKey;
        }

        public static (byte[], byte[])? KeySplitter(ulong? key)
        {
            if (key == null) return null;
            byte[] keyArray = new byte[8];
            BitConverter.GetBytes(key.Value).CopyTo(keyArray, 0);
            byte[] key1 = keyArray[..4];
            byte[] key2 = keyArray[4..];
            return (key1, key2);
        }

        public static bool Demux(string filenameArg, byte[] key1Arg, byte[] key2Arg, string output)
        {
            if (!File.Exists(filenameArg)) throw new FileNotFoundException($"File {filenameArg} doesn't exist...");
            string filename = Path.GetFileName(filenameArg);
            byte[] key1, key2;
            if (key1Arg.Length == 0 && key2Arg.Length == 0)
            {
                Console.WriteLine($"Finding encryption key for {filename}...");
                (byte[], byte[])? split = KeySplitter(EncryptionKey(filename));
                if (split == null) return false;
                key1 = split.Value.Item1;
                key2 = split.Value.Item2;
            }
            else
            {
                key1 = key1Arg;
                key2 = key2Arg;
            }

            USM file = new(filenameArg, key1, key2);
            Dictionary<string, List<string>> filePaths = file.Demux(true, true, output);  // TODO: Return file list for easier parsing

            if (!filePaths.TryGetValue("hca", out List<string> hcaPaths)) throw new Exception("No HCA files could be demuxed...");

            Task[] decodingTasks = new Task[hcaPaths.Count];
            for (int i = 0; i < decodingTasks.Length; i++)
            {
                int j = i;
                decodingTasks[i] = Task.Run(() =>
                {
                    Hca audioFile = new(hcaPaths[j], key1, key2);
                    audioFile.ConvertToWAV(output);
                });
            }
            Task.WaitAll(decodingTasks);
            Console.WriteLine("Extraction completed !");
            return true;
        }
    }
}

// Checksum unit testing
//string bytestring = "C8 C3 C1 00 02 00 00 60 E6 ED F4 00 02 00 BB 80 00 00 2C D5 00 80 03 83 E3 EF ED F0 02 AA 01 0F 01 00 80 80 00 00 00 00 E3 E9 F0 E8 00 38 F0 E1 E4 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00";
//string[] stringarray = bytestring.Split(" ");
//byte[] vs = new byte[stringarray.Length];
//for (int i = 0; i < stringarray.Length; i++)
//{
//    vs[i] = Convert.ToByte(stringarray[i], 16);
//}
//Console.WriteLine(Utils.Bswap(HCA.CheckSum(vs, vs.Length)));
// Should be equal to 13856
