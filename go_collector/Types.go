package main

type AppConfig struct {
	Title     string `json:"title" ini:"title"`
	OpcServer string `json:"opc_server" ini:"opc_server"`
	MqttConfig *MqttConfig `json:"mqtt,omitempty"`
	HttpConfig *HttpConfig `json:"http,omitempty"`
	Tasks []*TaskConfig `json:"tasks,omitempty"`
}

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

type HttpConfig struct {
	Enabled bool   `json:"enabled" ini:"enabled"`
	Url     string `json:"url" ini:"url"`
	Method  string `json:"method" ini:"method"`
	Timeout int    `json:"timeout" ini:"timeout"`
}

type TaskConfig struct {
	Enabled           bool          `json:"enabled" ini:"task"`
	JobIntervalSecond int           `json:"job_interval_second" ini:"job_interval_second"`
	Tags              []*TagMapping `json:"tags,omitempty"`
}

type TagMapping struct {
	OpcTag string `json:"opc_tag" ini:"tag_opc"`
	DbName string `json:"db_name" ini:"tag_dbn"`
}
