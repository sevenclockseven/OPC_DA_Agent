using System;
using System.Collections.Generic;

namespace OpcNetApi
{
    /// <summary>
    /// Mock implementation of OPC DA Server class for GitHub Actions build
    /// </summary>
    public class Server
    {
        private string _name;
        private bool _connected;

        public string Name { get; set; }
        public bool IsConnected => _connected;

        public Server(string name)
        {
            _name = name;
        }

        public void Connect()
        {
            // Mock implementation - just mark as connected
            _connected = true;
        }

        public void Disconnect()
        {
            _connected = false;
        }

        public Group CreateGroup(string name, bool active, int updateRate, object timeBias, object percentDeadband, object localeID, object clid)
        {
            return new Group(name, active, updateRate);
        }
    }

    /// <summary>
    /// Mock implementation of OPC DA Group class
    /// </summary>
    public class Group : IDisposable
    {
        private string _name;
        private bool _active;
        private int _updateRate;

        public string Name { get; set; }
        public bool IsActive { get; set; }
        public bool IsSubscribed { get; set; }

        public Group(string name, bool active, int updateRate)
        {
            _name = name;
            _active = active;
            _updateRate = updateRate;
        }

        public OPCItemResult[] AddItems(string[] itemNames, object[] requestTypes, object[] clientHandles)
        {
            // Return mock results for all items
            var results = new OPCItemResult[itemNames.Length];
            for (int i = 0; i < itemNames.Length; i++)
            {
                results[i] = new OPCItemResult
                {
                    ServerHandle = i + 1000, // Mock handle
                    ResultID = new OPCResultID { Succeeded = () => true }
                };
            }
            return results;
        }

        public OPCReadResult[] SyncRead(int dataSource, string[] itemNames, object[] values)
        {
            // Return mock read results
            var results = new OPCReadResult[itemNames.Length];
            var random = new Random();
            
            for (int i = 0; i < itemNames.Length; i++)
            {
                results[i] = new OPCReadResult
                {
                    Value = random.NextDouble() * 100, // Mock value
                    Quality = new OPCQuality(),
                    Timestamp = DateTime.Now,
                    ResultID = new OPCResultID { Succeeded = () => true }
                };
            }
            return results;
        }

        public void Dispose()
        {
            // Mock cleanup
        }
    }

    /// <summary>
    /// Mock implementation of OPC DA Item Result
    /// </summary>
    public class OPCItemResult
    {
        public int ServerHandle { get; set; }
        public OPCResultID ResultID { get; set; }
    }

    /// <summary>
    /// Mock implementation of OPC DA Read Result
    /// </summary>
    public class OPCReadResult
    {
        public object Value { get; set; }
        public OPCQuality Quality { get; set; }
        public DateTime Timestamp { get; set; }
        public OPCResultID ResultID { get; set; }
    }

    /// <summary>
    /// Mock implementation of OPC Quality
    /// </summary>
    public class OPCQuality
    {
        public override string ToString()
        {
            return "Good";
        }
    }

    /// <summary>
    /// Mock implementation of OPC Result ID
    /// </summary>
    public class OPCResultID
    {
        public Func<bool> Succeeded { get; set; }

        public override string ToString()
        {
            return "Success";
        }
    }

    /// <summary>
    /// Mock implementation of OPC Data Source
    /// </summary>
    public static class OpcDataSource
    {
        public const int CacheOrDevice = 1;
    }
}
