package main

type AppConfig struct {
	Title         string           `json:"title" ini:"title"`
	OpcServer     string           `json:"opc_server" ini:"opc_server"`
	HttpConfigs   []*HttpConfig    `json:"http_configs,omitempty"`
	MqttConfig    *MqttConfig      `json:"mqtt,omitempty"`
	RtdbConfig    *RtdbConfig      `json:"rtdb,omitempty"`
	WebhookConfig *WebhookConfig   `json:"webhook,omitempty"`
	Tasks         []*TaskConfig    `json:"tasks,omitempty"`
}

type HttpConfig struct {
	Name    string `json:"name" ini:"name"`
	Enabled bool   `json:"enabled" ini:"enabled"`
	Url     string `json:"url" ini:"url"`
	Method  string `json:"method" ini:"method"`
	Timeout int    `json:"timeout" ini:"timeout"`
}

type MqttConfig struct {
	Enabled    bool   `json:"enabled" ini:"enabled"`
	Broker     string `json:"broker" ini:"broker"`
	Port       int    `json:"port" ini:"port"`
	Topic      string `json:"topic" ini:"topic"`
	Username   string `json:"username,omitempty" ini:"username"`
	Password   string `json:"password,omitempty" ini:"password"`
	ClientId   string `json:"client_id" ini:"client_id"`
	Qos        int    `json:"qos" ini:"qos"`
	Retain     bool   `json:"retain" ini:"retain"`
	Format      string `json:"format" ini:"format"`
	JsTransform  string `json:"js_transform" ini:"js_transform"`
	Split       bool   `json:"split" ini:"split"`
}

type RtdbConfig struct {
	Enabled bool   `json:"enabled" ini:"enabled"`
	Host    string `json:"host" ini:"host"`
	Port    int    `json:"port" ini:"port"`
	Format  string `json:"format" ini:"format"`
}

type WebhookConfig struct {
	Enabled bool     `json:"enabled" ini:"enabled"`
	Url     string   `json:"url" ini:"url"`
	Events  []string `json:"events" ini:"events"`
}

type TaskConfig struct {
	Enabled           bool          `json:"enabled" ini:"task"`
	HttpSource        string        `json:"http_source" ini:"http_source"`
	JobIntervalSecond int           `json:"job_interval_second" ini:"job_interval_second"`
	Tags              []*TagMapping `json:"tags,omitempty"`
}

type TagMapping struct {
	OpcTag string `json:"opc_tag" ini:"tag_opc"`
	DbName string `json:"db_name" ini:"tag_dbn"`
}
