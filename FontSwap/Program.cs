using Newtonsoft.Json.Linq;
using System.Reflection;

namespace FontSwap
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            string indexPath = @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack\ffxiv\000000.win32.index";
            string datPath = @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack\ffxiv\000000.win32.dat0";

            string currentPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            string distribPath = Path.Combine(currentPath, "distrib");
            string origPath = Path.Combine(currentPath, "distrib/orig");

            string indexOutPath = Path.Combine(distribPath, "000000.win32.index");
            string datOutPath = Path.Combine(distribPath, "000000.win32.dat1");

            string origIndexPath = Path.Combine(origPath, "000000.win32.index");

            string mpdPath = Path.Combine(currentPath, "TTMPD.mpd");
            string mplPath = Path.Combine(currentPath, "TTMPL.mpl");

            Dictionary<uint, Dictionary<uint, SqFile>> sqFiles = ReadIndex(indexPath, datPath);

            // Clean up previous distributions and backups...
            if (Directory.Exists(distribPath))
            {
                Directory.Delete(distribPath, true);
            }

            Directory.CreateDirectory(distribPath);

            if (Directory.Exists(origPath))
            {
                Directory.Delete(origPath, true);
            }

            Directory.CreateDirectory(origPath);

            // Copy over original index...
            File.Copy(indexPath, origIndexPath);

            // Create new DAT.
            byte[] origDat = File.ReadAllBytes(datPath);
            byte[] newDatHeader = new byte[0x800];
            Array.Copy(origDat, 0, newDatHeader, 0, 0x800);
            Array.Copy(BitConverter.GetBytes(0x2), 0, newDatHeader, 0x400 + 0x10, 0x4);
            File.WriteAllBytes(datOutPath, newDatHeader);

            byte[] index = File.ReadAllBytes(indexPath);
            byte[] mpd = File.ReadAllBytes(mpdPath);

            List<JObject> payloads = new List<JObject>();

            using (StreamReader sr = new StreamReader(mplPath))
            {
                while (sr.Peek() != -1)
                {
                    string? line = sr.ReadLine();

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    payloads.Add(JObject.Parse(line));
                }
            }

            foreach (JObject payload in payloads)
            {
                string fullPath = payload.GetValue("FullPath")!.ToString().Replace("\\", "/");

                string headerDir = string.Empty;
                string headerName = fullPath;

                if (headerName.Contains("/"))
                {
                    headerDir = headerName.Substring(0, headerName.LastIndexOf("/"));
                    headerName = headerName.Substring(headerName.LastIndexOf("/") + 1);
                }

                uint headerDirHash = Hash.Compute(headerDir);
                uint headerNameHash = Hash.Compute(headerName);

                if (!sqFiles.ContainsKey(headerDirHash)) throw new Exception();
                if (!sqFiles[headerDirHash].ContainsKey(headerNameHash)) throw new Exception();

                SqFile sqFile = sqFiles[headerDirHash][headerNameHash];

                int modOffset = (int)payload.GetValue("ModOffset")!;
                int modSize = (int)payload.GetValue("ModSize")!;

                byte[] buffer = new byte[modSize];
                Array.Copy(mpd, modOffset, buffer, 0, modSize);

                sqFile.UpdateOffset((int)new FileInfo(datOutPath).Length, 1, index);

                using (FileStream fs = new FileStream(datOutPath, FileMode.Append, FileAccess.Write))
                using (BinaryWriter bw = new BinaryWriter(fs))
                {
                    bw.Write(buffer);
                }
            }

            File.WriteAllBytes(indexOutPath, index);
        }

        // Read index and output all SqFile header info as dictionary map.
        static Dictionary<uint, Dictionary<uint, SqFile>> ReadIndex(string indexPath, string datPath)
        {
            Dictionary<uint, Dictionary<uint, SqFile>> sqFiles = new Dictionary<uint, Dictionary<uint, SqFile>>();

            // Read index and cache sqFile header info from ko first.
            using (FileStream fs = File.OpenRead(indexPath))
            using (BinaryReader br = new BinaryReader(fs))
            {
                // Read header offset.
                br.BaseStream.Position = 0xc;
                int headerOffset = br.ReadInt32();

                // Read file info from the header.
                br.BaseStream.Position = headerOffset + 0x8;
                int fileOffset = br.ReadInt32();
                int fileCount = br.ReadInt32() / 0x10; // file size * 16 bits.

                // Iterate through files.
                br.BaseStream.Position = fileOffset;
                for (int i = 0; i < fileCount; i++)
                {
                    SqFile sqFile = new SqFile();
                    sqFile.Key = br.ReadUInt32();
                    sqFile.DirectoryKey = br.ReadUInt32();
                    sqFile.WrappedOffset = br.ReadInt32();
                    sqFile.DatPath = datPath;

                    br.ReadInt32();

                    if (!sqFiles.ContainsKey(sqFile.DirectoryKey)) sqFiles.Add(sqFile.DirectoryKey, new Dictionary<uint, SqFile>());
                    sqFiles[sqFile.DirectoryKey].Add(sqFile.Key, sqFile);
                }
            }

            return sqFiles;
        }
    }
}