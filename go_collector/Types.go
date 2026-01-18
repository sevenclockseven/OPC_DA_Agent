package main

import "time"

// AppConfig 应用程序配置
type AppConfig struct {
	Title     string `json:"title" ini:"title"`
	Debug     bool   `json:"debug" ini:"debug"`
	TaskCount int    `json:"task_count" ini:"task_count"`

	// OPC配置
	OpcHost   string `json:"opc_host" ini:"opc_host"`
	OpcServer string `json:"opc_server" ini:"opc_server"`
	OpcMode   string `json:"opc_mode" ini:"opc_mode"`
	OpcSync   bool   `json:"opc_sync" ini:"opc_sync"`

	// RTDB配置（本地）
	RtdbHost []string `json:"rtdb_host,omitempty" ini:"rtdb_host"`
	RtdbPort []int    `json:"rtdb_port,omitempty" ini:"rtdb_port"`

	// 远程RTDB配置
	RemoteEnabled  bool     `json:"remote_enabled" ini:"remote"`
	RemoteRtdbHost []string `json:"remote_rtdb_host,omitempty" ini:"rtdb_host"`
	RemoteRtdbPort []int    `json:"remote_rtdb_port,omitempty" ini:"rtdb_port"`

	// MQTT配置
	MqttConfig *MqttConfig `json:"mqtt,omitempty"`

	// HTTP配置
	HttpConfig *HttpConfig `json:"http,omitempty"`

	// 任务配置
	Tasks []*TaskConfig `json:"tasks,omitempty"`
}

// MqttConfig MQTT配置
type MqttConfig struct {
	Enabled  bool   `json:"enabled" ini:"enabled"`
	Broker   string `json:"broker" ini:"broker"`
	Port     int    `json:"port" ini:"port"`
	Topic    string `json:"topic" ini:"topic"`
	Username string `json:"username,omitempty" ini:"username"`
	Password string `json:"password,omitempty" ini:"password"`
	ClientId string `json:"client_id" ini:"client_id"`
	Qos      int    `json:"qos" ini:"qos"`
	Retain   bool   `json:"retain" ini:"retain"`
}

// HttpConfig HTTP配置
type HttpConfig struct {
	Enabled  bool              `json:"enabled" ini:"enabled"`
	Url      string            `json:"url" ini:"url"`
	Method   string            `json:"method" ini:"method"`
	Username string            `json:"username,omitempty" ini:"username"`
	Password string            `json:"password,omitempty" ini:"password"`
	Timeout  int               `json:"timeout" ini:"timeout"`
	Headers  map[string]string `json:"headers,omitempty" ini:"headers"`
}

// TaskConfig 任务配置
type TaskConfig struct {
	Enabled           bool          `json:"enabled" ini:"task"`
	JobStartDate      time.Time     `json:"job_start_date" ini:"job_start_date"`
	JobIntervalMode   string        `json:"job_interval_mode" ini:"job_interval_mode"`
	JobIntervalSecond int           `json:"job_interval_second" ini:"job_interval_second"`
	TagDevice         string        `json:"tag_device" ini:"tag_device"`
	TagComponent      int           `json:"tag_component" ini:"tag_component"`
	TagCount          int           `json:"tag_count" ini:"tag_count"`
	TagGroup          string        `json:"tag_group" ini:"tag_group"`
	TagPrecision      int           `json:"tag_precision" ini:"tag_precision"`
	TagState          string        `json:"tag_state" ini:"tag_state"`
	Tags              []*TagMapping `json:"tags,omitempty"`
}

// TagMapping 标签映射
type TagMapping struct {
	OpcTag string `json:"opc_tag" ini:"tag_opc"`
	DbName string `json:"db_name" ini:"tag_dbn"`
}
