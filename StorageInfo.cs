using System;
using System.Collections.Generic;
using System.Management;

namespace HardwareTestApp
{
    internal class StorageInfo
    {
        public List<String> HddName;
        string diskName;
        string diskType;
        string diskSizeGB;
        string busType;
        string serial;


        public List<string> Verifica()
        {
            HddName = new List<String>();
            var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
            var query = new ObjectQuery("SELECT * FROM MSFT_PhysicalDisk");
            var searcher = new ManagementObjectSearcher(scope, query);

            foreach (ManagementObject disk in searcher.Get())
            {

                ushort busType = (ushort)(disk["BusType"] ?? 0);
                if (busType == 7) { continue; }   //não salva se for usb
                string name = disk["FriendlyName"]?.ToString();
                ushort mediaType = (ushort)(disk["MediaType"] ?? 0);
                ulong sizeBytes = (ulong)(disk["Size"] ?? 0);
                double sizeGBReal = Math.Round(sizeBytes / Math.Pow(1024, 3), 2);
                double sizeGB = Math.Round(sizeBytes / Math.Pow(1000, 3), 0);
                string tipoMidia = ($" {(mediaType == 4 ? "SSD" : mediaType == 3 ? "HDD" : "Desconhecido")}");
                Console.WriteLine($"Disco: {name}");
                Console.WriteLine($"Tamanho: {sizeGB} GB");
                Console.WriteLine($"Tipo de mídia: {(mediaType == 4 ? "SSD" : mediaType == 3 ? "HDD" : " " + mediaType)}");
                Console.WriteLine($"Tipo de barramento: {ObterTipoBarramento(busType)}");
                Console.WriteLine();
                HddName.Add(sizeGB + " GB " + tipoMidia + " " + $"{ObterTipoBarramento(busType)}");
            }
            return HddName;
        }

        static string ObterTipoBarramento(ushort busType)
        {
            return busType switch
            {
                0 => "Unknown",
                1 => "SCSI",
                2 => "ATAPI",
                3 => "ATA",
                4 => "IEEE 1394 (FireWire)",
                5 => "SSA",
                6 => "Fibre Channel",
                7 => "USB",
                8 => "RAID",
                9 => "iSCSI",
                10 => "SAS",
                11 => "SATA",
                12 => "SD",
                13 => "MMC",
                14 => "MAX",
                15 => "File-Backed Virtual",
                16 => "Storage Spaces",
                17 => "NVMe",
                _ => "Other/Unknow"
            };
        }
    }
}