using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace HardwareTestApp
{
    internal class SmartTest
    {


        public void Testes()
        {
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");

                foreach (ManagementObject drive in searcher.Get())
                {
                    Console.WriteLine("Model: " + drive["Model"]);
                    Console.WriteLine("InterfaceType: " + drive["InterfaceType"]);
                    Console.WriteLine("SerialNumber: " + drive["SerialNumber"]);

                    ManagementObjectSearcher searcher2 = new ManagementObjectSearcher(
                        "SELECT * FROM MSStorageDriver_FailurePredictStatus WHERE InstanceName LIKE '%" + drive["DeviceID"].ToString().Replace("\\", "\\\\") + "%'");

                    foreach (ManagementObject status in searcher2.Get())
                    {
                        Console.WriteLine("PredictFailure: " + status["PredictFailure"]);
                    }

                    ManagementObjectSearcher searcher3 = new ManagementObjectSearcher(
                        "SELECT * FROM MSStorageDriver_FailurePredictData WHERE InstanceName LIKE '%" + drive["DeviceID"].ToString().Replace("\\", "\\\\") + "%'");

                    foreach (ManagementObject data in searcher3.Get())
                    {
                        byte[] vendorSpecific = (byte[])data["VendorSpecific"];
                        for (int i = 0; i < vendorSpecific.Length; i += 12)
                        {
                            int id = vendorSpecific[i + 2];
                            int status = vendorSpecific[i + 4];
                            int value = vendorSpecific[i + 5];
                            int worst = vendorSpecific[i + 6];
                            int vendorData = BitConverter.ToInt32(vendorSpecific, i + 7);

                            Console.WriteLine($"ID: {id}, Status: {status}, Value: {value}, Worst: {worst}, VendorData: {vendorData}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Erro: " + e.Message);
            }
        }
    }
}







