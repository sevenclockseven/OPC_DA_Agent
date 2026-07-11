package main

import (
	"encoding/json"
	"fmt"
	"io/ioutil"
	"strings"

	"gopkg.in/ini.v1"
)

type ConfigManager struct{}

func NewConfigManager() *ConfigManager {
	return &ConfigManager{}
}

func (cm *ConfigManager) Load(path string) *AppConfig {
	if strings.HasSuffix(path, ".ini") {
		return cm.LoadIni(path)
	}
	return cm.LoadJson(path)
}

func (cm *ConfigManager) LoadIni(path string) *AppConfig {
	fmt.Printf("[ConfigManager] 正在加载配置文件: %s\n", path)
	cfg, err := ini.Load(path)
	if err != nil {
		fmt.Printf("[ConfigManager] 加载失败: %v\n", err)
		return nil
	}
	fmt.Println("[ConfigManager] INI 文件解析成功！")

	config := &AppConfig{}

	if section := cfg.Section("main"); section != nil {
		config.Title = section.Key("title").String()
		config.OpcServer = section.Key("opc_server").String()
	}

	if section := cfg.Section("http"); section != nil {
		config.HttpConfig = &HttpConfig{}
		config.HttpConfig.Enabled, _ = section.Key("enabled").Bool()
		config.HttpConfig.Url = section.Key("url").String()
		config.HttpConfig.Method = section.Key("method").String()
		config.HttpConfig.Timeout, _ = section.Key("timeout").Int()
	}

	if section := cfg.Section("mqtt"); section != nil {
		config.MqttConfig = &MqttConfig{}
		config.MqttConfig.Enabled, _ = section.Key("enabled").Bool()
		config.MqttConfig.Broker = section.Key("broker").String()
		config.MqttConfig.Port, _ = section.Key("port").Int()
		config.MqttConfig.Topic = section.Key("topic").String()
		config.MqttConfig.Username = section.Key("username").String()
		config.MqttConfig.Password = section.Key("password").String()
		config.MqttConfig.ClientId = section.Key("client_id").String()
		config.MqttConfig.Qos, _ = section.Key("qos").Int()
		config.MqttConfig.Retain, _ = section.Key("retain").Bool()
		config.MqttConfig.Format = section.Key("format").String()
		config.MqttConfig.JsTransform = section.Key("js_transform").String()
	}

	if section := cfg.Section("rtdb"); section != nil {
		config.RtdbConfig = &RtdbConfig{}
		config.RtdbConfig.Enabled, _ = section.Key("enabled").Bool()
		config.RtdbConfig.Format = section.Key("format").String()
	}

	if section := cfg.Section("webhook"); section != nil {
		config.WebhookConfig = &WebhookConfig{}
		config.WebhookConfig.Enabled, _ = section.Key("enabled").Bool()
		config.WebhookConfig.Url = section.Key("url").String()
		eventsStr := section.Key("events").String()
		if eventsStr != "" {
			config.WebhookConfig.Events = strings.Split(eventsStr, ",")
		}
	}

	// Task sections
	fmt.Println("[ConfigManager] 开始解析任务配置...")
	maxTasks := 100
	for i := 1; i <= maxTasks; i++ {
		sectionName := fmt.Sprintf("task%d", i)
		section := cfg.Section(sectionName)
		if section == nil {
			fmt.Printf("[ConfigManager] 没有找到 %s section，停止解析\n", sectionName)
			break
		}
		taskKey := section.Key("task")
		if taskKey == nil || taskKey.String() == "" {
			fmt.Printf("[ConfigManager] %s 没有task键，停止解析\n", sectionName)
			break
		}
		fmt.Printf("[ConfigManager] 正在解析 %s section\n", sectionName)

		task := &TaskConfig{}
		task.Enabled, _ = section.Key("task").Bool()
		task.JobIntervalSecond, _ = section.Key("job_interval_second").Int()

		for j := 1; ; j++ {
			opcKey := fmt.Sprintf("tag_opc%d", j)
			dbnKey := fmt.Sprintf("tag_dbn%d", j)

			opcTag := section.Key(opcKey).String()
			dbName := section.Key(dbnKey).String()

			if opcTag == "" && dbName == "" {
				break
			}

			task.Tags = append(task.Tags, &TagMapping{
				OpcTag: opcTag,
				DbName: dbName,
			})
		}

		config.Tasks = append(config.Tasks, task)
		fmt.Printf("[ConfigManager] 任务 %d 解析完成，标签数: %d\n", i, len(task.Tags))
	}
	fmt.Println("[ConfigManager] 所有任务解析完成！")
	fmt.Printf("[ConfigManager] 总共解析了 %d 个任务\n", len(config.Tasks))

	return config
}

// LoadJson 从JSON文件加载
func (cm *ConfigManager) LoadJson(path string) *AppConfig {
	data, err := ioutil.ReadFile(path)
	if err != nil {
		return nil // 返回 nil 以表示读取失败
	}

	var config AppConfig
	if err := json.Unmarshal(data, &config); err != nil {
		fmt.Printf("解析JSON配置失败: %v\n", err)
		return nil
	}

	return &config
}

// Save 保存配置（自动检测格式）
func (cm *ConfigManager) Save(path string, config *AppConfig) error {
	if strings.HasSuffix(path, ".ini") {
		return cm.SaveIni(path, config)
	}
	return cm.SaveJson(path, config)
}

// SaveIni 保存为INI格式
func (cm *ConfigManager) SaveIni(path string, config *AppConfig) error {
	cfg := ini.Empty()

	section := cfg.Section("main")
	section.NewKey("title", config.Title)
	section.NewKey("opc_server", config.OpcServer)

	if config.HttpConfig != nil {
		section = cfg.Section("http")
		section.NewKey("enabled", fmt.Sprintf("%v", config.HttpConfig.Enabled))
		section.NewKey("url", config.HttpConfig.Url)
		section.NewKey("method", config.HttpConfig.Method)
		section.NewKey("timeout", fmt.Sprintf("%d", config.HttpConfig.Timeout))
	}

	if config.MqttConfig != nil {
		section = cfg.Section("mqtt")
		section.NewKey("enabled", fmt.Sprintf("%v", config.MqttConfig.Enabled))
		section.NewKey("broker", config.MqttConfig.Broker)
		section.NewKey("port", fmt.Sprintf("%d", config.MqttConfig.Port))
		section.NewKey("topic", config.MqttConfig.Topic)
		section.NewKey("username", config.MqttConfig.Username)
		section.NewKey("password", config.MqttConfig.Password)
		section.NewKey("client_id", config.MqttConfig.ClientId)
		section.NewKey("qos", fmt.Sprintf("%d", config.MqttConfig.Qos))
		section.NewKey("retain", fmt.Sprintf("%v", config.MqttConfig.Retain))
		section.NewKey("format", config.MqttConfig.Format)
		section.NewKey("js_transform", config.MqttConfig.JsTransform)
	}

	if config.RtdbConfig != nil {
		section = cfg.Section("rtdb")
		section.NewKey("enabled", fmt.Sprintf("%v", config.RtdbConfig.Enabled))
		section.NewKey("format", config.RtdbConfig.Format)
	}

	if config.WebhookConfig != nil {
		section = cfg.Section("webhook")
		section.NewKey("enabled", fmt.Sprintf("%v", config.WebhookConfig.Enabled))
		section.NewKey("url", config.WebhookConfig.Url)
		section.NewKey("events", strings.Join(config.WebhookConfig.Events, ","))
	}

	for i, task := range config.Tasks {
		sectionName := fmt.Sprintf("task%d", i+1)
		section = cfg.Section(sectionName)
		section.NewKey("task", fmt.Sprintf("%v", task.Enabled))
		section.NewKey("job_interval_second", fmt.Sprintf("%d", task.JobIntervalSecond))

		for j, tag := range task.Tags {
			section.NewKey(fmt.Sprintf("tag_opc%d", j+1), tag.OpcTag)
			section.NewKey(fmt.Sprintf("tag_dbn%d", j+1), tag.DbName)
		}
	}

	return cfg.SaveTo(path)
}

// SaveJson 保存为JSON格式
func (cm *ConfigManager) SaveJson(path string, config *AppConfig) error {
	data, err := json.MarshalIndent(config, "", "  ")
	if err != nil {
		return err
	}
	return ioutil.WriteFile(path, data, 0644)
}

// ToJsonString 转换为JSON字符串
func (cm *ConfigManager) ToJsonString(config *AppConfig) string {
	data, _ := json.MarshalIndent(config, "", "  ")
	return string(data)
}

// ToIniString 转换为INI字符串
func (cm *ConfigManager) ToIniString(config *AppConfig) string {
	cfg := ini.Empty()

	section := cfg.Section("main")
	section.NewKey("title", config.Title)
	section.NewKey("opc_server", config.OpcServer)

	if config.MqttConfig != nil {
		section = cfg.Section("mqtt")
		section.NewKey("enabled", fmt.Sprintf("%v", config.MqttConfig.Enabled))
		section.NewKey("broker", config.MqttConfig.Broker)
		section.NewKey("port", fmt.Sprintf("%d", config.MqttConfig.Port))
		section.NewKey("topic", config.MqttConfig.Topic)
		section.NewKey("client_id", config.MqttConfig.ClientId)
		section.NewKey("qos", fmt.Sprintf("%d", config.MqttConfig.Qos))
		section.NewKey("retain", fmt.Sprintf("%v", config.MqttConfig.Retain))
	}

	if config.HttpConfig != nil {
		section = cfg.Section("http")
		section.NewKey("enabled", fmt.Sprintf("%v", config.HttpConfig.Enabled))
		section.NewKey("url", config.HttpConfig.Url)
		section.NewKey("method", config.HttpConfig.Method)
		section.NewKey("timeout", fmt.Sprintf("%d", config.HttpConfig.Timeout))
	}

	for i, task := range config.Tasks {
		sectionName := fmt.Sprintf("task%d", i+1)
		section = cfg.Section(sectionName)
		section.NewKey("task", fmt.Sprintf("%v", task.Enabled))
		section.NewKey("job_interval_second", fmt.Sprintf("%d", task.JobIntervalSecond))

		for j, tag := range task.Tags {
			section.NewKey(fmt.Sprintf("tag_opc%d", j+1), tag.OpcTag)
			section.NewKey(fmt.Sprintf("tag_dbn%d", j+1), tag.DbName)
		}
	}

	var buf strings.Builder
	cfg.WriteTo(&buf)
	return buf.String()
}

// CreateTemplate 创建配置模板
func (cm *ConfigManager) CreateTemplate(templateType string) *AppConfig {
	switch templateType {
	case "mqtt_basic":
		return &AppConfig{
			Title:     "MQTT基础配置",
			OpcServer: "KEPware.KEPServerEx.V4",
			MqttConfig: &MqttConfig{
				Enabled:  true,
				Broker:   "172.16.32.98",
				Port:     1883,
				Topic:    "opc/data",
				ClientId: "opc_collector_01",
				Qos:      1,
			},
			Tasks: []*TaskConfig{
				{
					Enabled:           true,
					JobIntervalSecond: 1,
					Tags: []*TagMapping{
						{OpcTag: "lt.sc.20251_M4102_ZZT", DbName: "20251_M4102_ZZT"},
					},
				},
			},
		}

	case "http_basic":
		return &AppConfig{
			Title:     "HTTP基础配置",
			OpcServer: "KEPware.KEPServerEx.V4",
			HttpConfig: &HttpConfig{
				Enabled: true,
				Url:     "http://172.16.32.98:8080/api/data",
				Method:  "POST",
				Timeout: 30000,
			},
			Tasks: []*TaskConfig{
				{
					Enabled:           true,
					JobIntervalSecond: 1,
					Tags: []*TagMapping{
						{OpcTag: "lt.sc.20251_M4102_ZZT", DbName: "20251_M4102_ZZT"},
					},
				},
			},
		}

	case "full":
		return &AppConfig{
			Title:     "完整配置模板",
			OpcServer: "KEPware.KEPServerEx.V4",
			MqttConfig: &MqttConfig{
				Enabled:  true,
				Broker:   "172.16.32.98",
				Port:     1883,
				Topic:    "opc/data",
				ClientId: "opc_collector_01",
				Qos:      1,
				Retain:   false,
			},
			HttpConfig: &HttpConfig{
				Enabled: false,
				Url:     "http://172.16.32.98:8080/api/data",
				Method:  "POST",
				Timeout: 30000,
			},
			Tasks: []*TaskConfig{
				{
					Enabled:           true,
					JobIntervalSecond: 1,
					Tags: []*TagMapping{
						{OpcTag: "lt.sc.20251_M4102_ZZT", DbName: "20251_M4102_ZZT"},
						{OpcTag: "lt.sc.20251_M4102_CYBJ", DbName: "20251_M4102_CYBJ"},
					},
				},
			},
		}

	default:
		return &AppConfig{
			Title:     "默认配置",
			OpcServer: "KEPware.KEPServerEx.V4",
		}
	}
}
