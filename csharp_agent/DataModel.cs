using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace OPC_DA_Agent
{
    /// <summary>
    /// OPC数据点配置
    /// </summary>
    public class TagConfig
    {
        [JsonProperty("node_id")]
        public string NodeId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("data_type")]
        public string DataType { get; set; }

        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// OPC数据点值（键值对格式）
    /// </summary>
    public class TagValue
    {
        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("value")]
        public object Value { get; set; }

        [JsonProperty("quality")]
        public string Quality { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; } // "Good", "Bad", "Uncertain"

        [JsonProperty("data_type")]
        public string DataType { get; set; }

        // 兼容旧字段名
        [JsonProperty("node_id")]
        public string NodeId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }

    /// <summary>
    /// 键值对数据（用于批量数据传输）
    /// </summary>
    public class KeyValueData
    {
        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("value")]
        public object Value { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("quality")]
        public string Quality { get; set; }
    }

    /// <summary>
    /// 批量键值对数据响应
    /// </summary>
    public class BatchKeyValueResponse
    {
        [JsonProperty("batch_id")]
        public string BatchId { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }

        [JsonProperty("data")]
        public Dictionary<string, object> Data { get; set; }

        [JsonProperty("metadata")]
        public Dictionary<string, TagMetadata> Metadata { get; set; }

        [JsonProperty("elapsed_ms")]
        public double ElapsedMs { get; set; }
    }

    /// <summary>
    /// 标签元数据
    /// </summary>
    public class TagMetadata
    {
        [JsonProperty("data_type")]
        public string DataType { get; set; }

        [JsonProperty("quality")]
        public string Quality { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }
    }

    /// <summary>
    /// 批量数据响应
    /// </summary>
    public class BatchDataResponse
    {
        [JsonProperty("batch_id")]
        public string BatchId { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }

        [JsonProperty("data")]
        public List<TagValue> Data { get; set; }

        [JsonProperty("elapsed_ms")]
        public double ElapsedMs { get; set; }
    }

    /// <summary>
    /// 系统状态信息
    /// </summary>
    public class SystemStatus
    {
        [JsonProperty("opc_connected")]
        public bool OpcConnected { get; set; }

        [JsonProperty("opc_server_url")]
        public string OpcServerUrl { get; set; }

        [JsonProperty("tag_count")]
        public int TagCount { get; set; }

        [JsonProperty("enabled_tag_count")]
        public int EnabledTagCount { get; set; }

        [JsonProperty("uptime_seconds")]
        public double UptimeSeconds { get; set; }

        [JsonProperty("last_update_time")]
        public DateTime LastUpdateTime { get; set; }

        [JsonProperty("total_requests")]
        public long TotalRequests { get; set; }

        [JsonProperty("total_data_points")]
        public long TotalDataPoints { get; set; }

        [JsonProperty("error_count")]
        public long ErrorCount { get; set; }

        [JsonProperty("memory_usage_mb")]
        public double MemoryUsageMb { get; set; }

        [JsonProperty("cpu_usage_percent")]
        public double CpuUsagePercent { get; set; }
    }

    /// <summary>
    /// HTTP请求响应
    /// </summary>
    public class ApiResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("data")]
        public object Data { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        public ApiResponse()
        {
            Timestamp = DateTime.UtcNow;
        }

        public static ApiResponse SuccessResponse(object data = null, string message = "OK")
        {
            return new ApiResponse
            {
                Success = true,
                Message = message,
                Data = data
            };
        }

        public static ApiResponse ErrorResponse(string message, object data = null)
        {
            return new ApiResponse
            {
                Success = false,
                Message = message,
                Data = data
            };
        }
    }

    /// <summary>
    /// 批量读取请求
    /// </summary>
    public class BatchReadRequest
    {
        [JsonProperty("node_ids")]
        public List<string> NodeIds { get; set; }

        [JsonProperty("timeout_ms")]
        public int TimeoutMs { get; set; } = 5000;
    }

    /// <summary>
    /// 配置更新请求
    /// </summary>
    public class ConfigUpdateRequest
    {
        [JsonProperty("update_interval_ms")]
        public int? UpdateIntervalMs { get; set; }

        [JsonProperty("batch_size")]
        public int? BatchSize { get; set; }

        [JsonProperty("enable_compression")]
        public bool? EnableCompression { get; set; }
    }
}
/// <summary>
/// OPC节点信息
/// </summary>
public class OPCNode
{
    [JsonProperty("node_id")]
    public string NodeId { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("data_type")]
    public string DataType { get; set; }

    [JsonProperty("access_rights")]
    public string AccessRights { get; set; }

    [JsonProperty("children")]
    public List<OPCNode> Children { get; set; } = new List<OPCNode>();

    [JsonProperty("has_children")]
    public bool HasChildren { get; set; }

    [JsonProperty("is_folder")]
    public bool IsFolder { get; set; }
}

/// <summary>
/// OPC节点详细信息
/// </summary>
public class OPCNodeDetail
{
    [JsonProperty("node_id")]
    public string NodeId { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("data_type")]
    public string DataType { get; set; }

    [JsonProperty("access_rights")]
    public string AccessRights { get; set; }

    [JsonProperty("scan_rate")]
    public double ScanRate { get; set; }

    [JsonProperty("eu_type")]
    public string EuType { get; set; }

    [JsonProperty("eu_info")]
    public string EuInfo { get; set; }

    [JsonProperty("min_value")]
    public object MinValue { get; set; }

    [JsonProperty("max_value")]
    public object MaxValue { get; set; }

    [JsonProperty("initial_value")]
    public object InitialValue { get; set; }

    [JsonProperty("item_id")]
    public string ItemId { get; set; }
}