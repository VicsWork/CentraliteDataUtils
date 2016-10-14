using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Data.SqlClient;
using System.Data.SqlTypes;

using System.Net;
using System.Net.NetworkInformation;

using Microsoft.Win32;

using CentraliteData;

namespace CentraliteDataUtils
{
    public class DataUtils
    {
        static string _db_connection_str = Properties.Settings.Default.DBConnectionString;
        static public string DBConnStr { get { return _db_connection_str; } set { _db_connection_str = value; } }

        /// <summary>
        /// New data context from DBConStr
        /// </summary>
        static public CentraliteDataContext DataContext
        {
            get {return new CentraliteDataContext(DBConnStr);}
        }

        static CentraliteDataContext CentraliteDataContext()
        {
            return DataContext;
        }

        // The site id is cached
        static int _site_id = -1;
        static public int Site_ID
        {
            get
            {
                if (_site_id < 0)
                {
                    try
                    {
                        string macaddr_str = MachineInfo.GetMacAndIpAddress().Item1;
                        using (CentraliteDataContext dc = DataContext)
                        {
                            _site_id = dc.StationSites.Where(d => d.StationMac == macaddr_str).Select(s => s.ProductionSiteId).Single<int>();
                        }
                    }
                    catch { };
                }
                return _site_id;
            }
        }

        // The machine id is cached
        static int _machine_id = -1;
        static public int Machine_ID
        {
            get
            {
                if (_machine_id < 0)
                {
                    try
                    {
                        try
                        {

                            // Set machine id
                            // This is commented out because we are updating data below
                            //_machine_id = dc.TestStationMachines.Where(m => m.Name == Environment.MachineName).Select(s => s.Id).Single<int>();

                            // Machine guid column was added after data had already been inserted
                            // Update data
                            using (CentraliteDataContext dc = DataContext)
                            {
                                TestStationMachine machine = dc.TestStationMachines.Single<TestStationMachine>(m => m.Name == Environment.MachineName);
                                _machine_id = machine.Id;
                                try
                                {
                                    // Update machine GUI if null
                                    if (machine.MachineGuid == null)
                                    {
                                        machine.MachineGuid = MachineInfo.GetMachineGuid();
                                        dc.SubmitChanges();
                                    }
                                }
                                catch (Exception ex) { string m = ex.Message; };
                            }
                        }
                        catch { };

                        if (_machine_id < 0)
                        {
                            _machine_id = insertMachine();
                        }
                    }
                    catch (Exception ex) { string msg = ex.Message; };
                }

                return _machine_id;
            }
        }

        /// <summary>
        /// Gets the machine id from database
        /// It creates an entry if not found
        /// </summary>
        /// <returns></returns>
        public static int insertMachine()
        {
            var mac_ip = MachineInfo.GetMacAndIpAddress();
            string macaddr_str = mac_ip.Item1;
            string ip_str = mac_ip.Item2;

            string description = null;
            try { description = MachineInfo.GetComputerDescription(); }
            catch (Exception) { };

            CentraliteDataContext dc = new CentraliteDataContext();

            // Set a computer description based on domain and nic if one was not found
            if (description == null || description == "")
            {
                string production_site_name = null;
                try
                {
                    var ss = dc.StationSites.Where(d => d.StationMac == macaddr_str).Single();
                    production_site_name = ss.ProductionSite.Name;
                }
                catch (Exception) { };

                NetworkInterface nic = MachineInfo.GetFirstNic();
                if (production_site_name != null)
                    description = string.Format("{0}, {1}", production_site_name, nic.Description);
                else
                    description = string.Format("{0}, {1}", Environment.UserDomainName, nic.Description);
            }


            TestStationMachine machine = new TestStationMachine();
            machine.Name = Environment.MachineName;
            machine.IpAddress = ip_str;
            machine.MacAddress = macaddr_str;
            machine.Description = description;
            try
            {
                string machineguid = MachineInfo.GetMachineGuid();
                // Database should default to "00000000-0000-0000-0000-000000000000"
                // Just check to make sure we got the same length
                if (machineguid.Length <= 36)
                    machine.MachineGuid = machineguid;
            }
            catch { };

            dc.TestStationMachines.InsertOnSubmit(machine);
            dc.SubmitChanges();
            dc.Dispose();

            return machine.Id;

        }

        /// <summary>
        /// Gets the EUI Id
        /// </summary>
        /// <param name="eui"></param>
        /// <returns>EUI ID</returns>
        public static int GetEUIID(string eui)
        {
            int id = -1;
            using (CentraliteDataContext dc = new CentraliteDataContext())
            {

                var q = dc.EuiLists.Where(d => d.EUI == eui).Select(d => d.Id);
                if (q.Any())
                {
                    id = q.Single<int>();
                }
                else
                {
                    EuiList euid = new EuiList();
                    euid.EUI = eui;
                    euid.ProductionSiteId = Site_ID;

                    dc.EuiLists.InsertOnSubmit(euid);
                    dc.SubmitChanges();
                    dc.Dispose();

                    id = euid.Id;
                }

            }
            return id;
        }

        /// <summary>
        /// Used to find ISA adapter ip address providing location
        /// </summary>
        /// <param name="location"></param>
        /// <returns></returns>
        public static string[] GetISAAdapterIPsFromLikeLocation(string location)
        {
            using (CentraliteDataContext dc = DataContext)
            {
                var q = dc.InsightAdapters.Where(d => d.Location.Contains(location)).Select(d => d.IpAddress);
                return q.ToArray<string>();
            }
        }
    }

    /// <summary>
    /// Machine information functions
    /// </summary>
    public class MachineInfo
    {

        /// <summary>
        /// Gets the specified Nics mac and ip address
        /// </summary>
        /// <param name="nic"></param>
        /// <returns></returns>
        public static Tuple<string, string> GetMacAndIpAddress()
        {
            string macaddr_str = "000000000000";
            string ip_str = "0.0.0.0";

            // Get the first network interface
            NetworkInterface nic = null;
            try { nic = GetFirstNic(); }
            catch (Exception) { };
            if (nic != null)
            {
                try
                {
                    macaddr_str = nic.GetPhysicalAddress().ToString();
                    foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            ip_str = ua.Address.ToString();
                            break;
                        }
                    }
                }
                catch (Exception) { };
            }

            return Tuple.Create(macaddr_str, ip_str);
        }


        /// <summary>
        /// Gets the first Network Interface of the system
        /// </summary>
        /// <returns>First Network Interface of the system</returns>
        public static NetworkInterface GetFirstNic()
        {
            //var myInterfaceAddress = NetworkInterface.GetAllNetworkInterfaces()
            //    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            //    .OrderByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            //    .Select(n => n.GetPhysicalAddress())
            //    .FirstOrDefault();

            NetworkInterface myInterfaceAddress = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                .FirstOrDefault();

            return myInterfaceAddress;
        }

        /// <summary>
        /// Gets the machine gui id stored in the registry
        /// </summary>
        /// <returns></returns>
        public static string GetMachineGuid()
        {
            string location = @"SOFTWARE\Microsoft\Cryptography";
            string name = "MachineGuid";

            using (RegistryKey localMachineX64View =
                RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                using (RegistryKey rk = localMachineX64View.OpenSubKey(location))
                {
                    if (rk == null)
                        throw new KeyNotFoundException(
                            string.Format("Key Not Found: {0}", location));

                    object machineGuid = rk.GetValue(name);
                    if (machineGuid == null)
                        throw new IndexOutOfRangeException(
                            string.Format("Index Not Found: {0}", name));

                    return machineGuid.ToString();
                }
            }
        }


        /// <summary>
        /// Returns the computer description
        /// </summary>
        /// <returns>the computer description</returns>
        public static string GetComputerDescription()
        {
            string key = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\lanmanserver\parameters";
            string computerDescription = (string)Registry.GetValue(key, "srvcomment", null);

            return computerDescription;
        }

        public static IPAddress GetFirstGatewayAddress()
        {
            NetworkInterface nic = GetFirstNic();

            var gate = nic.GetIPProperties().GatewayAddresses
                .Where(n => n.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .FirstOrDefault();

            return gate.Address;

        }

    }

}
