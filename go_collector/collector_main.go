package main

import (
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"net"
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
	httpClient  *HttpClient
	mqttClient  *MqttClient
	rtdbClient  *RtdbClient
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

	if c.config.HttpConfig != nil && c.config.HttpConfig.Enabled {
		c.httpClient = NewHttpClient(c.config.HttpConfig)
		fmt.Println("✓ HTTP配置完成")
	}

	if c.config.MqttConfig != nil && c.config.MqttConfig.Enabled {
		c.mqttClient = NewMqttClient(c.config.MqttConfig)
		if err := c.mqttClient.Connect(); err != nil {
			return fmt.Errorf("MQTT连接失败: %v", err)
		}
		fmt.Println("✓ MQTT连接成功")
	}

	if c.config.RtdbConfig != nil && c.config.RtdbConfig.Enabled {
		if c.config.RtdbConfig.Host != "" && c.config.RtdbConfig.Port > 0 {
			c.rtdbClient = NewRtdbClient(c.config.RtdbConfig)
			if err := c.rtdbClient.Connect(); err != nil {
				log.Printf("⚠️ RTDB连接失败: %v", err)
			} else {
				fmt.Println("✓ RTDB连接成功")
			}
		} else {
			log.Println("⚠️ RTDB已启用但地址未配置，跳过连接")
		}
	}

	go c.collectLoop()

	return nil
}

func (c *Collector) Stop() {
	c.running = false

	if c.mqttClient != nil {
		c.mqttClient.Disconnect()
	}

	if c.rtdbClient != nil {
		c.rtdbClient.Disconnect()
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
				log.Printf("映射: %s -> %s (配置文件)", topic, dbName)
			} else {
				transformedKey = c.transformer.Transform(topic)
				if transformedKey != topic {
					log.Printf("转换: %s -> %s (规则)", topic, transformedKey)
				}
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

	if c.rtdbClient != nil && c.rtdbClient.IsConnected() {
		message := map[string]interface{}{
			"timestamp": time.Now().Format(time.RFC3339),
			"values":    keyValues,
			"metadata":  metadata,
		}
		c.rtdbClient.Send(message)
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

	if len(mapping) > 0 {
		log.Printf("标签映射: %d 条", len(mapping))
		for k, v := range mapping {
			log.Printf("  %s -> %s", k, v)
		}
	}

	return mapping
}

type RtdbClient struct {
	config    *RtdbConfig
	connected bool
	conn      net.Conn
}

func NewRtdbClient(config *RtdbConfig) *RtdbClient {
	return &RtdbClient{
		config: config,
	}
}

func (c *RtdbClient) Connect() error {
	if c.config.Host == "" || c.config.Port == 0 {
		return fmt.Errorf("RTDB地址或端口未配置")
	}

	addr := fmt.Sprintf("%s:%d", c.config.Host, c.config.Port)
	conn, err := net.DialTimeout("tcp", addr, 5*time.Second)
	if err != nil {
		return fmt.Errorf("连接RTDB失败: %v", err)
	}

	c.conn = conn
	c.connected = true
	log.Printf("✅ RTDB已连接到 %s", addr)
	return nil
}

func (c *RtdbClient) Disconnect() {
	if c.conn != nil {
		c.conn.Close()
	}
	c.connected = false
	log.Println("📴 RTDB已断开")
}

func (c *RtdbClient) IsConnected() bool {
	return c.connected
}

func (c *RtdbClient) Send(message map[string]interface{}) error {
	if !c.IsConnected() {
		return fmt.Errorf("RTDB未连接")
	}

	values, ok := message["values"].(map[string]interface{})
	if !ok {
		return fmt.Errorf("无效的消息格式")
	}

	metadata, _ := message["metadata"].(map[string]map[string]interface{})

	for key, value := range values {
		line := c.formatLine(key, value, metadata[key])
		_, err := c.conn.Write([]byte(line + "\n"))
		if err != nil {
			return fmt.Errorf("发送数据失败: %v", err)
		}
		log.Printf("RTDB发送: %s", line)
	}

	return nil
}

func (c *RtdbClient) formatLine(key string, value interface{}, meta map[string]interface{}) string {
	format := c.config.Format
	if format == "" {
		format = "{key},{value},{quality},{timestamp}"
	}

	quality := 192
	timestamp := time.Now().UnixMilli()
	if meta != nil {
		if q, ok := meta["quality"].(int); ok {
			quality = q
		}
		if t, ok := meta["timestamp"].(int64); ok {
			timestamp = t
		}
	}

	result := format
	result = strings.ReplaceAll(result, "{key}", key)
	result = strings.ReplaceAll(result, "{value}", fmt.Sprintf("%v", value))
	result = strings.ReplaceAll(result, "{quality}", fmt.Sprintf("%d", quality))
	result = strings.ReplaceAll(result, "{timestamp}", fmt.Sprintf("%d", timestamp))

	return result
}

type MqttClient struct {
	config *MqttConfig
	client mqtt.Client
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

	var publishData []byte
	var err error

	if c.config.Format != "" && c.config.Format != "full" {
		publishData, err = c.formatMessage(message, c.config.Format)
	} else {
		publishData, err = json.Marshal(message)
	}

	if err != nil {
		return fmt.Errorf("JSON序列化失败: %v", err)
	}

	qos := byte(c.config.Qos)
	token := c.client.Publish(c.config.Topic, qos, c.config.Retain, string(publishData))
	if token.Wait() && token.Error() != nil {
		log.Printf("MQTT发布失败: %v", token.Error())
		return token.Error()
	}

	log.Printf("MQTT发布成功: %s", c.config.Topic)
	return nil
}

func (c *MqttClient) formatMessage(message map[string]interface{}, format string) ([]byte, error) {
	if format == "flat" {
		if values, ok := message["values"].(map[string]interface{}); ok {
			return json.Marshal(values)
		}
		return json.Marshal(message)
	}

	return json.Marshal(message)
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
