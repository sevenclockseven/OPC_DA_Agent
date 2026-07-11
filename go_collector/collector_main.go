package main

import (
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"net/http"
	"strings"
	"time"

	mqtt "github.com/eclipse/paho.mqtt.golang"
	"log"
	"os"
	"os/signal"
	"runtime"
	"syscall"
)

func main() {
	// 立即输出，避免缓冲
	os.Stdout.Sync()
	os.Stderr.Sync()

	// 解析命令行参数
	configPath := flag.String("config", "collector.ini", "配置文件路径")
	webPort := flag.Int("web-port", 9090, "Web服务器端口")
	showHelp := flag.Bool("help", false, "显示帮助信息")
	showVersion := flag.Bool("version", false, "显示版本信息")
	flag.Parse()

	if *showHelp {
		showHelpInfo()
		return
	}

	if *showVersion {
		fmt.Println("OPC DA Collector v1.0.0")
		return
	}

	// 初始化日志
	log.SetFlags(log.LstdFlags | log.Lshortfile)
	log.SetOutput(os.Stderr)

	// 打印启动信息
	fmt.Println("=== OPC DA Collector ===")
	fmt.Printf("操作系统: %s\n", runtime.GOOS)
	fmt.Printf("架构: %s\n", runtime.GOARCH)
	fmt.Printf("工作目录: %s\n", getCurrentDir())
	fmt.Println("正在加载配置...")

	// 加载配置
	configManager := NewConfigManager()
	config := configManager.Load(*configPath)
	if config == nil {
		log.Fatalf("无法加载配置文件: %s", *configPath)
	}

	fmt.Println("配置加载成功！")

	fmt.Println("=== OPC DA Collector ===")
	fmt.Printf("配置文件: %s\n", *configPath)
	fmt.Printf("系统标题: %s\n", config.Title)
	fmt.Printf("OPC服务器: %s\n", config.OpcServer)
	os.Stdout.Sync() // 立即刷新输出

	// 启动Web服务器
	if *webPort > 0 {
		go func() {
			webServer := NewWebServer(*configPath)
			if err := webServer.Start(*webPort); err != nil {
				log.Printf("Web服务器启动失败: %v", err)
			}
		}()
		fmt.Printf("Web服务器: http://localhost:%d\n", *webPort)
	}

	// 启动采集器
	collector := NewCollector(config)
	if err := collector.Start(); err != nil {
		log.Fatalf("采集器启动失败: %v", err)
	}

	fmt.Println("采集器已启动")
	fmt.Println("按 Ctrl+C 停止")

	// 等待退出信号
	waitForShutdown()

	// 停止采集器
	collector.Stop()

	fmt.Println("程序已退出")
	os.Stdout.Sync()
}

func getCurrentDir() string {
	if dir, err := os.Getwd(); err == nil {
		return dir
	}
	return "未知"
}

func showHelpInfo() {
	fmt.Println("OPC DA Collector - OPC DA数据采集程序")
	fmt.Println()
	fmt.Println("用法:")
	fmt.Println("  collector [选项]")
	fmt.Println()
	fmt.Println("选项:")
	fmt.Println("  --config <path>      配置文件路径 (默认: collector.ini)")
	fmt.Println("  --web-port <port>    Web服务器端口 (默认: 9090, 0=禁用)")
	fmt.Println("  --help               显示此帮助信息")
	fmt.Println("  --version            显示版本信息")
	fmt.Println()
	fmt.Println("示例:")
	fmt.Println("  collector")
	fmt.Println("  collector --config my_config.ini")
	fmt.Println("  collector --web-port 8080")
	fmt.Println()
	fmt.Println("Web界面:")
	fmt.Println("  http://localhost:9090/")
}

func waitForShutdown() {
	sigChan := make(chan os.Signal, 1)
	signal.Notify(sigChan, syscall.SIGINT, syscall.SIGTERM)
	<-sigChan
}

// Collector 采集器
type Collector struct {
	config      *AppConfig
	transformer *KeyTransformer
	mqttClient  *MqttClient
	httpClient  *HttpClient
	running     bool
}

func NewCollector(config *AppConfig) *Collector {
	transformer := NewKeyTransformer()
	transformer.LoadFromFile("transform.json")

	return &Collector{
		config:      config,
		transformer: transformer,
	}
}

func (c *Collector) Start() error {
	c.running = true

	// 初始化MQTT
	if c.config.MqttConfig != nil && c.config.MqttConfig.Enabled {
		c.mqttClient = NewMqttClient(c.config.MqttConfig)
		if err := c.mqttClient.Connect(); err != nil {
			return fmt.Errorf("MQTT连接失败: %v", err)
		}
		fmt.Println("✓ MQTT连接成功")
	}

	// 初始化HTTP
	if c.config.HttpConfig != nil && c.config.HttpConfig.Enabled {
		c.httpClient = NewHttpClient(c.config.HttpConfig)
		fmt.Println("✓ HTTP配置完成")
	}

	// 启动采集任务
	go c.collectLoop()

	return nil
}

func (c *Collector) Stop() {
	c.running = false

	if c.mqttClient != nil {
		c.mqttClient.Disconnect()
	}

	fmt.Println("采集器已停止")
}

func (c *Collector) collectLoop() {
	// 模拟数据采集
	ticker := time.NewTicker(time.Duration(c.config.Tasks[0].JobIntervalSecond) * time.Second)
	defer ticker.Stop()

	for c.running {
		select {
		case <-ticker.C:
			c.collectData()
		}
	}
}

func (c *Collector) collectData() {
	c.transformer.LoadFromFile("transform.json")

	var rawData []map[string]interface{}

	if c.httpClient != nil && c.config.HttpConfig != nil && c.config.HttpConfig.Enabled {
		fetched, err := c.fetchFromHttp()
		if err != nil {
			log.Printf("HTTP获取数据失败: %v", err)
			return
		}
		rawData = fetched
	} else {
		rawData = []map[string]interface{}{
			{"topic": "lt.sc.20251_M4102_ZZT", "value": 1.0, "quality": 192, "errorCode": 0},
			{"topic": "lt.sc.20251_M4102_CYBJ", "value": 0.0, "quality": 192, "errorCode": 0},
			{"topic": "lt.sc.20251_M4102_JYBJ", "value": 0.0, "quality": 192, "errorCode": 0},
		}
	}

	tagMapping := c.buildTagMapping()

	keyValues := make(map[string]interface{})
	metadata := make(map[string]map[string]interface{})

	for _, item := range rawData {
		if errorCode, ok := item["errorCode"].(int); ok && errorCode == 0 {
			topic := item["topic"].(string)
			value := item["value"]
			quality := item["quality"]

			var transformedKey string
			if dbName, ok := tagMapping[topic]; ok {
				transformedKey = dbName
			} else {
				transformedKey = c.transformer.Transform(topic)
			}

			keyValues[transformedKey] = value
			metadata[transformedKey] = map[string]interface{}{
				"quality":   quality,
				"timestamp": time.Now().Format(time.RFC3339),
			}
		}
	}

	if c.mqttClient != nil && c.mqttClient.IsConnected() {
		message := map[string]interface{}{
			"timestamp": time.Now().Format(time.RFC3339),
			"values":    keyValues,
			"metadata":  metadata,
		}
		c.mqttClient.Publish(message)
	}

	if c.httpClient != nil {
		message := map[string]interface{}{
			"timestamp": time.Now().Format(time.RFC3339),
			"values":    keyValues,
			"metadata":  metadata,
		}
		c.httpClient.Send(message)
	}
}

func (c *Collector) buildTagMapping() map[string]string {
	mapping := make(map[string]string)

	for _, task := range c.config.Tasks {
		if !task.Enabled {
			continue
		}
		for _, tag := range task.Tags {
			if tag.OpcTag != "" && tag.DbName != "" {
				mapping[tag.OpcTag] = tag.DbName
			}
		}
	}

	return mapping
}

// MqttClient MQTT客户端
type MqttClient struct {
	config    *MqttConfig
	client    mqtt.Client
	connected bool
}

func NewMqttClient(config *MqttConfig) *MqttClient {
	return &MqttClient{
		config: config,
	}
}

func (c *MqttClient) Connect() error {
	opts := mqtt.NewClientOptions()
	broker := fmt.Sprintf("tcp://%s:%d", c.config.Broker, c.config.Port)
	opts.AddBroker(broker)
	opts.SetClientID(c.config.ClientId)

	// 设置用户名密码
	if c.config.Username != "" {
		opts.SetUsername(c.config.Username)
	}
	if c.config.Password != "" {
		opts.SetPassword(c.config.Password)
	}

	// 设置 QoS 和 Retain
	opts.SetCleanSession(true)

	// 创建客户端
	c.client = mqtt.NewClient(opts)

	// 连接
	token := c.client.Connect()
	if token.Wait() && token.Error() != nil {
		return fmt.Errorf("MQTT连接失败: %v", token.Error())
	}

	c.connected = true
	log.Printf("✅ MQTT已连接到 %s", broker)
	return nil
}

func (c *MqttClient) IsConnected() bool {
	if c.client == nil {
		return false
	}
	return c.client.IsConnected()
}

func (c *MqttClient) Publish(message map[string]interface{}) error {
	if !c.IsConnected() {
		return fmt.Errorf("MQTT未连接")
	}

	jsonData, err := json.Marshal(message)
	if err != nil {
		return fmt.Errorf("JSON序列化失败: %v", err)
	}

	qos := byte(c.config.Qos)
	token := c.client.Publish(c.config.Topic, qos, c.config.Retain, string(jsonData))
	if token.Wait() && token.Error() != nil {
		log.Printf("MQTT发布失败: %v", token.Error())
		return token.Error()
	}

	log.Printf("MQTT发布成功: %s", c.config.Topic)
	return nil
}

func (c *MqttClient) Disconnect() {
	if c.client != nil && c.client.IsConnected() {
		c.client.Disconnect(250)
		c.connected = false
		log.Println("📴 MQTT已断开连接")
	}
}

// HttpClient HTTP客户端
type HttpClient struct {
	config *HttpConfig
}

func NewHttpClient(config *HttpConfig) *HttpClient {
	return &HttpClient{
		config: config,
	}
}

func (c *HttpClient) Send(message map[string]interface{}) {
	jsonData, err := json.Marshal(message)
	if err != nil {
		log.Printf("HTTP JSON序列化失败: %v", err)
		return
	}

	client := &http.Client{
		Timeout: time.Duration(c.config.Timeout) * time.Millisecond,
	}

	method := c.config.Method
	if method == "" {
		method = "POST"
	}

	var resp *http.Response
	if method == "GET" {
		resp, err = client.Get(c.config.Url)
	} else {
		resp, err = client.Post(c.config.Url, "application/json",
			strings.NewReader(string(jsonData)))
	}

	if err != nil {
		log.Printf("HTTP发送失败: %v", err)
		return
	}
	defer resp.Body.Close()

	log.Printf("HTTP发送成功: %s (状态码: %d)", c.config.Url, resp.StatusCode)
}

func (c *Collector) fetchFromHttp() ([]map[string]interface{}, error) {
	if c.config.HttpConfig == nil || !c.config.HttpConfig.Enabled {
		return nil, fmt.Errorf("HTTP未启用")
	}

	client := &http.Client{
		Timeout: time.Duration(c.config.HttpConfig.Timeout) * time.Millisecond,
	}

	resp, err := client.Get(c.config.HttpConfig.Url)
	if err != nil {
		return nil, fmt.Errorf("HTTP请求失败: %v", err)
	}
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, fmt.Errorf("读取响应失败: %v", err)
	}

	var apiResp struct {
		Success bool                   `json:"success"`
		Data    map[string]interface{} `json:"data"`
		Message string                 `json:"message"`
	}
	if err := json.Unmarshal(body, &apiResp); err != nil {
		return nil, fmt.Errorf("解析JSON失败: %v", err)
	}

	if !apiResp.Success {
		return nil, fmt.Errorf("API返回错误: %s", apiResp.Message)
	}

	var result []map[string]interface{}
	for key, value := range apiResp.Data {
		result = append(result, map[string]interface{}{
			"topic":     key,
			"value":     value,
			"quality":   192,
			"errorCode": 0,
		})
	}

	return result, nil
}
