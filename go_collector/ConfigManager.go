package main

import (
	"encoding/json"
	"fmt"
	"io/ioutil"
	"strconv"
	"strings"
	"time"

	"gopkg.in/ini.v1"
)

// ConfigManager 配置管理器
type ConfigManager struct{}

func NewConfigManager() *ConfigManager {
	return &ConfigManager{}
}

// Load 加载配置（自动检测格式）
func (cm *ConfigManager) Load(path string) *AppConfig {
	if strings.HasSuffix(path, ".ini") {
		return cm.LoadIni(path)
	}
	return cm.LoadJson(path)
}

// LoadIni 从INI文件加载
func (cm *ConfigManager) LoadIni(path string) *AppConfig {
	fmt.Printf("[ConfigManager] 正在加载配置文件: %s\n", path) // 调试输出
	cfg, err := ini.Load(path)
	if err != nil {
		fmt.Printf("[ConfigManager] 加载失败: %v\n", err)
		return nil // 返回 nil 以表示加载失败
	}
	fmt.Println("[ConfigManager] INI 文件解析成功！") // 调试输出

	config := &AppConfig{}

	// Main section
	if section := cfg.Section("main"); section != nil {
		config.Title = section.Key("title").String()
		config.Debug, _ = section.Key("debug").Bool()
		config.TaskCount, _ = section.Key("task_count").Int()
		config.OpcHost = section.Key("opc_host").String()
		config.OpcServer = section.Key("opc_server").String()
		config.OpcMode = section.Key("opc_mode").String()
		config.OpcSync, _ = section.Key("opc_sync").Bool()

		// RTDB hosts and ports
		rtdbHostStr := section.Key("rtdb_host").String()
		if rtdbHostStr != "" {
			config.RtdbHost = strings.Split(rtdbHostStr, ",")
		}

		rtdbPortStr := section.Key("rtdb_port").String()
		if rtdbPortStr != "" {
			ports := strings.Split(rtdbPortStr, ",")
			config.RtdbPort = make([]int, len(ports))
			for i, p := range ports {
				config.RtdbPort[i], _ = strconv.Atoi(strings.TrimSpace(p))
			}
		}
	}

	// Remote section
	if section := cfg.Section("remote"); section != nil {
		config.RemoteEnabled, _ = section.Key("remote").Bool()

		rtdbHostStr := section.Key("rtdb_host").String()
		if rtdbHostStr != "" {
			config.RemoteRtdbHost = strings.Split(rtdbHostStr, ",")
		}

		rtdbPortStr := section.Key("rtdb_port").String()
		if rtdbPortStr != "" {
			ports := strings.Split(rtdbPortStr, ",")
			config.RemoteRtdbPort = make([]int, len(ports))
			for i, p := range ports {
				config.RemoteRtdbPort[i], _ = strconv.Atoi(strings.TrimSpace(p))
			}
		}
	}

	// MQTT section
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
	}

	// HTTP section
	if section := cfg.Section("http"); section != nil {
		config.HttpConfig = &HttpConfig{}
		config.HttpConfig.Enabled, _ = section.Key("enabled").Bool()
		config.HttpConfig.Url = section.Key("url").String()
		config.HttpConfig.Method = section.Key("method").String()
		config.HttpConfig.Username = section.Key("username").String()
		config.HttpConfig.Password = section.Key("password").String()
		config.HttpConfig.Timeout, _ = section.Key("timeout").Int()

		// Parse headers
		headersStr := section.Key("headers").String()
		if headersStr != "" {
			config.HttpConfig.Headers = make(map[string]string)
			pairs := strings.Split(headersStr, ";")
			for _, pair := range pairs {
				parts := strings.Split(pair, ":")
				if len(parts) == 2 {
					config.HttpConfig.Headers[strings.TrimSpace(parts[0])] = strings.TrimSpace(parts[1])
				}
			}
		}
	}

	// Task sections
	fmt.Println("[ConfigManager] 开始解析任务配置...")
	maxTasks := 100 // 最多解析100个任务，防止无限循环
	for i := 1; i <= maxTasks; i++ {
		sectionName := fmt.Sprintf("task%d", i)
		section := cfg.Section(sectionName)
		if section == nil {
			fmt.Printf("[ConfigManager] 没有找到 %s section，停止解析\n", sectionName)
			break
		}
		// 检查section是否真的有task键，防止无限循环
		taskKey := section.Key("task")
		if taskKey == nil || taskKey.String() == "" {
			fmt.Printf("[ConfigManager] %s 没有task键，停止解析\n", sectionName)
			break
		}
		fmt.Printf("[ConfigManager] 正在解析 %s section\n", sectionName)

		task := &TaskConfig{}
		task.Enabled, _ = section.Key("task").Bool()
		jobStartDateStr := section.Key("job_start_date").String()
		if jobStartDateStr != "" {
			task.JobStartDate, _ = time.Parse("2006-01-02 15:04:05", jobStartDateStr)
		}
		task.JobIntervalMode = section.Key("job_interval_mode").String()
		task.JobIntervalSecond, _ = section.Key("job_interval_second").Int()
		task.TagDevice = section.Key("tag_device").String()
		task.TagComponent, _ = section.Key("tag_component").Int()
		task.TagCount, _ = section.Key("tag_count").Int()
		task.TagGroup = section.Key("tag_group").String()
		task.TagPrecision, _ = section.Key("tag_precision").Int()
		task.TagState = section.Key("tag_state").String()

		// Parse tags
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

	// Main section
	section := cfg.Section("main")
	section.NewKey("title", config.Title)
	section.NewKey("debug", fmt.Sprintf("%v", config.Debug))
	section.NewKey("task_count", fmt.Sprintf("%d", config.TaskCount))
	if config.RtdbHost != nil {
		section.NewKey("rtdb_host", strings.Join(config.RtdbHost, ","))
	}
	if config.RtdbPort != nil {
		ports := make([]string, len(config.RtdbPort))
		for i, p := range config.RtdbPort {
			ports[i] = fmt.Sprintf("%d", p)
		}
		section.NewKey("rtdb_port", strings.Join(ports, ","))
	}
	section.NewKey("opc_host", config.OpcHost)
	section.NewKey("opc_server", config.OpcServer)
	section.NewKey("opc_mode", config.OpcMode)
	section.NewKey("opc_sync", fmt.Sprintf("%v", config.OpcSync))

	// Remote section
	section = cfg.Section("remote")
	section.NewKey("remote", fmt.Sprintf("%v", config.RemoteEnabled))
	if config.RemoteRtdbHost != nil {
		section.NewKey("rtdb_host", strings.Join(config.RemoteRtdbHost, ","))
	}
	if config.RemoteRtdbPort != nil {
		ports := make([]string, len(config.RemoteRtdbPort))
		for i, p := range config.RemoteRtdbPort {
			ports[i] = fmt.Sprintf("%d", p)
		}
		section.NewKey("rtdb_port", strings.Join(ports, ","))
	}

	// MQTT section
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
	}

	// HTTP section
	if config.HttpConfig != nil {
		section = cfg.Section("http")
		section.NewKey("enabled", fmt.Sprintf("%v", config.HttpConfig.Enabled))
		section.NewKey("url", config.HttpConfig.Url)
		section.NewKey("method", config.HttpConfig.Method)
		section.NewKey("username", config.HttpConfig.Username)
		section.NewKey("password", config.HttpConfig.Password)
		section.NewKey("timeout", fmt.Sprintf("%d", config.HttpConfig.Timeout))

		if config.HttpConfig.Headers != nil && len(config.HttpConfig.Headers) > 0 {
			headers := make([]string, 0, len(config.HttpConfig.Headers))
			for k, v := range config.HttpConfig.Headers {
				headers = append(headers, fmt.Sprintf("%s:%s", k, v))
			}
			section.NewKey("headers", strings.Join(headers, ";"))
		}
	}

	// Task sections
	for i, task := range config.Tasks {
		sectionName := fmt.Sprintf("task%d", i+1)
		section = cfg.Section(sectionName)
		section.NewKey("task", fmt.Sprintf("%v", task.Enabled))
		section.NewKey("job_start_date", task.JobStartDate.Format("2006-01-02 15:04:05"))
		section.NewKey("job_interval_mode", task.JobIntervalMode)
		section.NewKey("job_interval_second", fmt.Sprintf("%d", task.JobIntervalSecond))
		section.NewKey("tag_device", task.TagDevice)
		section.NewKey("tag_component", fmt.Sprintf("%d", task.TagComponent))
		section.NewKey("tag_count", fmt.Sprintf("%d", task.TagCount))
		section.NewKey("tag_group", task.TagGroup)
		section.NewKey("tag_precision", fmt.Sprintf("%d", task.TagPrecision))
		section.NewKey("tag_state", task.TagState)

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

	// Main section
	section := cfg.Section("main")
	section.NewKey("title", config.Title)
	section.NewKey("debug", fmt.Sprintf("%v", config.Debug))
	section.NewKey("task_count", fmt.Sprintf("%d", config.TaskCount))
	if config.RtdbHost != nil {
		section.NewKey("rtdb_host", strings.Join(config.RtdbHost, ","))
	}
	if config.RtdbPort != nil {
		ports := make([]string, len(config.RtdbPort))
		for i, p := range config.RtdbPort {
			ports[i] = fmt.Sprintf("%d", p)
		}
		section.NewKey("rtdb_port", strings.Join(ports, ","))
	}
	section.NewKey("opc_host", config.OpcHost)
	section.NewKey("opc_server", config.OpcServer)
	section.NewKey("opc_mode", config.OpcMode)
	section.NewKey("opc_sync", fmt.Sprintf("%v", config.OpcSync))

	// Remote section
	section = cfg.Section("remote")
	section.NewKey("remote", fmt.Sprintf("%v", config.RemoteEnabled))

	// MQTT section
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
	}

	// HTTP section
	if config.HttpConfig != nil {
		section = cfg.Section("http")
		section.NewKey("enabled", fmt.Sprintf("%v", config.HttpConfig.Enabled))
		section.NewKey("url", config.HttpConfig.Url)
		section.NewKey("method", config.HttpConfig.Method)
		section.NewKey("timeout", fmt.Sprintf("%d", config.HttpConfig.Timeout))
	}

	// Task sections
	for i, task := range config.Tasks {
		sectionName := fmt.Sprintf("task%d", i+1)
		section = cfg.Section(sectionName)
		section.NewKey("task", fmt.Sprintf("%v", task.Enabled))
		section.NewKey("job_start_date", task.JobStartDate.Format("2006-01-02 15:04:05"))
		section.NewKey("job_interval_mode", task.JobIntervalMode)
		section.NewKey("job_interval_second", fmt.Sprintf("%d", task.JobIntervalSecond))
		section.NewKey("tag_device", task.TagDevice)
		section.NewKey("tag_component", fmt.Sprintf("%d", task.TagComponent))
		section.NewKey("tag_count", fmt.Sprintf("%d", task.TagCount))
		section.NewKey("tag_group", task.TagGroup)
		section.NewKey("tag_precision", fmt.Sprintf("%d", task.TagPrecision))
		section.NewKey("tag_state", task.TagState)

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
			Debug:     false,
			TaskCount: 1,
			OpcHost:   "172.16.32.98",
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
					JobStartDate:      time.Now(),
					JobIntervalMode:   "second",
					JobIntervalSecond: 1,
					TagDevice:         "2025",
					TagComponent:      1,
					TagCount:          100,
					TagGroup:          "sc",
					TagPrecision:      3,
					TagState:          "2025_sc_state",
					Tags: []*TagMapping{
						{OpcTag: "lt.sc.20251_M4102_ZZT", DbName: "20251_M4102_ZZT"},
					},
				},
			},
		}

	case "http_basic":
		return &AppConfig{
			Title:     "HTTP基础配置",
			Debug:     false,
			TaskCount: 1,
			OpcHost:   "172.16.32.98",
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
					JobStartDate:      time.Now(),
					JobIntervalMode:   "second",
					JobIntervalSecond: 1,
					TagDevice:         "2025",
					TagComponent:      1,
					TagCount:          100,
					TagGroup:          "sc",
					TagPrecision:      3,
					TagState:          "2025_sc_state",
					Tags: []*TagMapping{
						{OpcTag: "lt.sc.20251_M4102_ZZT", DbName: "20251_M4102_ZZT"},
					},
				},
			},
		}

	case "full":
		return &AppConfig{
			Title:     "完整配置模板",
			Debug:     false,
			TaskCount: 1,
			OpcHost:   "172.16.32.98",
			OpcServer: "KEPware.KEPServerEx.V4",
			OpcMode:   "open",
			OpcSync:   true,
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
					JobStartDate:      time.Now(),
					JobIntervalMode:   "second",
					JobIntervalSecond: 1,
					TagDevice:         "2025",
					TagComponent:      1,
					TagCount:          100,
					TagGroup:          "sc",
					TagPrecision:      3,
					TagState:          "2025_sc_state",
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
			Debug:     false,
			TaskCount: 1,
			OpcHost:   "172.16.32.98",
			OpcServer: "KEPware.KEPServerEx.V4",
		}
	}
}
