using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace OPC_DA_Agent
{
    /// <summary>
    /// 配置管理
    /// </summary>
    public class Config
    {
        // OPC服务器配置
        [JsonProperty("opc_server_prog_id")]
        public string OpcServerProgId { get; set; } = "OPCServer.WinCC";

        [JsonProperty("opc_username")]
        public string OpcUsername { get; set; }

        [JsonProperty("opc_password")]
        public string OpcPassword { get; set; }

        // Legacy property for backward compatibility
        [JsonProperty("opc_server_url")]
        public string OpcServerUrl
        {
            get => OpcServerProgId;
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    // Convert opcda:// URL to ProgID
                    if (value.StartsWith("opcda://"))
                    {
                        // Extract ProgID from opcda:// URL
                        // opcda://localhost/OPCServer.WinCC -> OPCServer.WinCC
                        var parts = value.Split('/');
                        if (parts.Length > 0)
                        {
                            OpcServerProgId = parts[parts.Length - 1];
                        }
                    }
                    else
                    {
                        OpcServerProgId = value;
                    }
                }
            }
        }

        // HTTP服务器配置
        [JsonProperty("http_port")]
        public int HttpPort { get; set; } = 8080;

        [JsonProperty("http_bind_ip")]
        public string HttpBindIp { get; set; } = "0.0.0.0";

        // 采集配置
        [JsonProperty("update_interval_ms")]
        public int UpdateInterval { get; set; } = 1000;

        [JsonProperty("batch_size")]
        public int BatchSize { get; set; } = 500;

        [JsonProperty("enable_compression")]
        public bool EnableCompression { get; set; } = true;

        [JsonProperty("max_connections")]
        public int MaxConnections { get; set; } = 100;

        // 标签配置
        [JsonProperty("tags_file")]
        public string TagsFile { get; set; }

        [JsonProperty("tags")]
        public List<TagConfig> Tags { get; set; } = new List<TagConfig>();

        // 日志配置
        [JsonProperty("log_file")]
        public string LogFile { get; set; } = "logs\\opc_agent.log";

        [JsonProperty("log_level")]
        public string LogLevel { get; set; } = "Info";

        [JsonProperty("max_log_size_mb")]
        public int MaxLogSizeMb { get; set; } = 10;

        [JsonProperty("log_retention_days")]
        public int LogRetentionDays { get; set; } = 7;

        // 性能配置
        [JsonProperty("enable_performance_counters")]
        public bool EnablePerformanceCounters { get; set; } = false;

        [JsonProperty("cache_enabled")]
        public bool CacheEnabled { get; set; } = true;

        [JsonProperty("cache_ttl_ms")]
        public int CacheTtlMs { get; set; } = 500;

        /// <summary>
        /// 从文件加载配置
        /// </summary>
        public static Config LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                // 创建默认配置文件
                var defaultConfig = new Config();
                defaultConfig.SaveToFile(filePath);
                return defaultConfig;
            }

            var json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<Config>(json);
        }

        /// <summary>
        /// 保存配置到文件
        /// </summary>
        public void SaveToFile(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// 验证配置
        /// </summary>
        public bool Validate(out List<string> errors)
        {
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(OpcServerProgId))
            {
                errors.Add("OPC服务器ProgID不能为空");
            }

            if (HttpPort <= 0 || HttpPort > 65535)
            {
                errors.Add("HTTP端口必须在1-65535之间");
            }

            if (UpdateInterval < 100)
            {
                errors.Add("更新间隔不能小于100ms");
            }

            if (BatchSize <= 0)
            {
                errors.Add("批次大小必须大于0");
            }

            if (Tags.Count == 0 && string.IsNullOrEmpty(TagsFile))
            {
                errors.Add("必须配置标签或指定标签文件路径");
            }

            return errors.Count == 0;
        }

        /// <summary>
        /// 获取示例配置
        /// </summary>
        public static Config GetExampleConfig()
        {
            return new Config
            {
                OpcServerProgId = "OPCServer.WinCC",
                HttpPort = 8080,
                UpdateInterval = 1000,
                BatchSize = 500,
                TagsFile = "tags.json",
                LogFile = "logs\\opc_agent.log",
                LogLevel = "Info",
                Tags = new List<TagConfig>
                {
                    new TagConfig
                    {
                        NodeId = "Channel1.Device1.Temperature",
                        Name = "Temperature",
                        Description = "温度传感器",
                        DataType = "Double",
                        Enabled = true
                    },
                    new TagConfig
                    {
                        NodeId = "Channel1.Device1.Pressure",
                        Name = "Pressure",
                        Description = "压力传感器",
                        DataType = "Double",
                        Enabled = true
                    }
                }
            };
        }
    }
}
