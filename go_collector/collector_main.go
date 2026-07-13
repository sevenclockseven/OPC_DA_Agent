package main

import (
	"bufio"
	"context"
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
	os.Stdout.Sync()

	collector := NewCollector(config)

	if *webPort > 0 {
		go func() {
			webServer := NewWebServer(*configPath, collector)
			if err := webServer.Start(*webPort); err != nil {
				log.Printf("Web服务器启动失败: %v", err)
			}
		}()
		fmt.Printf("Web服务器: http://localhost:%d\n", *webPort)
	}

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

type Collector struct {
	config      *AppConfig
	httpClients []*HttpClient
	mqttClient  *MqttClient
	rtdbClient  *RtdbClient
	running     bool
	cancelFunc  context.CancelFunc
}

type TaskRunner struct {
	task        *TaskConfig
	transformer *KeyTransformer
	config      *AppConfig
}

func NewCollector(config *AppConfig) *Collector {
	return &Collector{
		config: config,
	}
}

func (c *Collector) Start() error {
	ctx, cancel := context.WithCancel(context.Background())
	c.cancelFunc = cancel
	c.running = true

	c.httpClients = make([]*HttpClient, 0)
	for _, httpConfig := range c.config.HttpConfigs {
		if httpConfig.Enabled {
			client := NewHttpClient(httpConfig)
			c.httpClients = append(c.httpClients, client)
			fmt.Printf("✓ HTTP数据源[%s]配置完成\n", httpConfig.Name)
		}
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

	for _, task := range c.config.Tasks {
		if task.Enabled {
			runner := &TaskRunner{
				task:        task,
				config:      c.config,
				transformer: NewKeyTransformer(),
			}
			transformFile := "transform.json"
			if task.HttpSource != "" {
				transformFile = "transform_" + task.HttpSource + ".json"
			}
			runner.transformer.LoadFromFile(transformFile)
			go runner.run(ctx, c)
		}
	}

	return nil
}

func (c *Collector) Stop() {
	c.running = false
	if c.cancelFunc != nil {
		c.cancelFunc()
	}

	if c.mqttClient != nil {
		c.mqttClient.Disconnect()
	}

	if c.rtdbClient != nil {
		c.rtdbClient.Disconnect()
	}

	fmt.Println("采集器已停止")
}

func (c *Collector) Reload(newConfig *AppConfig) {
	c.Stop()
	c.config = newConfig
	c.Start()
	log.Println("✅ 配置已热加载")
}

func (tr *TaskRunner) run(ctx context.Context, collector *Collector) {
	// 数据源 URL 含 /api/stream 时走 SSE 订阅推送，否则保持原有定时轮询
	if tr.task.HttpSource != "" {
		for _, client := range collector.httpClients {
			if client.config.Name == tr.task.HttpSource && strings.Contains(client.config.Url, "/api/stream") {
				tr.runSse(ctx, collector, client)
				return
			}
		}
	}

	interval := time.Duration(tr.task.JobIntervalSecond) * time.Second
	if interval <= 0 {
		interval = time.Second
	}
	ticker := time.NewTicker(interval)
	defer ticker.Stop()

	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			tr.collectData(collector)
		}
	}
}

func (tr *TaskRunner) runSse(ctx context.Context, collector *Collector, client *HttpClient) {
	backoff := time.Second
	for {
		select {
		case <-ctx.Done():
			return
		default:
		}

		streamURL := client.config.Url
		log.Printf("SSE 连接 %s", streamURL)
		req, err := http.NewRequest("GET", streamURL, nil)
		if err != nil {
			log.Printf("SSE 请求创建失败: %v", err)
			time.Sleep(backoff)
			continue
		}
		resp, err := http.DefaultClient.Do(req)
		if err != nil {
			log.Printf("SSE 连接失败: %v", err)
			time.Sleep(backoff)
			backoff = minDuration(backoff*2, 30*time.Second)
			continue
		}
		if resp.StatusCode != 200 {
			resp.Body.Close()
			log.Printf("SSE 返回状态码 %d", resp.StatusCode)
			time.Sleep(backoff)
			backoff = minDuration(backoff*2, 30*time.Second)
			continue
		}
		backoff = time.Second

		scanner := bufio.NewScanner(resp.Body)
		scanner.Buffer(make([]byte, 1024*1024), 8*1024*1024)
		for scanner.Scan() {
			select {
			case <-ctx.Done():
				resp.Body.Close()
				return
			default:
			}
			line := scanner.Text()
			if strings.HasPrefix(line, "data:") {
				payload := strings.TrimSpace(strings.TrimPrefix(line, "data:"))
				if payload != "" {
					tr.handleSsePayload(collector, payload)
				}
			}
		}
		log.Printf("SSE 连接断开: %v", scanner.Err())
		resp.Body.Close()
		time.Sleep(backoff)
		backoff = minDuration(backoff*2, 30*time.Second)
	}
}

func (tr *TaskRunner) handleSsePayload(collector *Collector, payload string) {
	var envelope struct {
		Ts     string                   `json:"ts"`
		Values []map[string]interface{} `json:"values"`
	}
	if err := json.Unmarshal([]byte(payload), &envelope); err != nil {
		log.Printf("SSE 报文解析失败: %v", err)
		return
	}
	if len(envelope.Values) == 0 {
		return
	}

	rawData := make([]map[string]interface{}, 0, len(envelope.Values))
	for _, v := range envelope.Values {
		rawData = append(rawData, map[string]interface{}{
			"topic":   v["key"],
			"value":   v["value"],
			"quality": qualityToInt(v["quality"]),
		})
	}
	tr.processAndPublish(collector, rawData)
}

func qualityToInt(q interface{}) int {
	switch v := q.(type) {
	case string:
		if v == "Good" {
			return 192
		}
		return 0
	case float64:
		return int(v)
	case int:
		return v
	case int32:
		return int(v)
	case int64:
		return int(v)
	}
	return 0
}

func minDuration(a, b time.Duration) time.Duration {
	if a < b {
		return a
	}
	return b
}

func (tr *TaskRunner) collectData(collector *Collector) {
	transformFile := "transform.json"
	if tr.task.HttpSource != "" {
		transformFile = "transform_" + tr.task.HttpSource + ".json"
	}
	tr.transformer.LoadFromFile(transformFile)

	var rawData []map[string]interface{}

	if tr.task.HttpSource != "" {
		for _, client := range collector.httpClients {
			if client.config.Name == tr.task.HttpSource {
				fetched, err := collector.fetchFromHttp(client)
				if err != nil {
					log.Printf("HTTP[%s]获取数据失败: %v", tr.task.HttpSource, err)
					return
				}
				rawData = fetched
				break
			}
		}
	} else if len(collector.httpClients) > 0 {
		for _, client := range collector.httpClients {
			fetched, err := collector.fetchFromHttp(client)
			if err != nil {
				log.Printf("HTTP[%s]获取数据失败: %v", client.config.Name, err)
				continue
			}
			rawData = append(rawData, fetched...)
		}
	}

	if len(rawData) == 0 {
		return
	}

	tr.processAndPublish(collector, rawData)
}

func (tr *TaskRunner) processAndPublish(collector *Collector, rawData []map[string]interface{}) {
	values := make(map[string]interface{})
	metadata := make(map[string]map[string]interface{})

	for _, item := range rawData {
		origKey, _ := item["topic"].(string)
		val := item["value"]
		quality, _ := item["quality"].(int)

		newKey := tr.transformer.Transform(origKey)

		if len(tr.task.Tags) > 0 {
			for _, tag := range tr.task.Tags {
				if tag.OpcTag == origKey {
					newKey = tag.DbName
					break
				}
			}
		}

		values[newKey] = val
		metadata[newKey] = map[string]interface{}{
			"quality":   quality,
			"timestamp": time.Now().UnixMilli(),
		}
	}

	msg := map[string]interface{}{
		"timestamp": time.Now().Format(time.RFC3339),
		"values":    values,
		"metadata":  metadata,
	}

	if collector.mqttClient != nil && collector.mqttClient.IsConnected() {
		if err := collector.mqttClient.Publish(msg); err != nil {
			log.Printf("MQTT发送失败: %v", err)
		}
	}

	if collector.rtdbClient != nil && collector.rtdbClient.IsConnected() {
		if err := collector.rtdbClient.Send(msg); err != nil {
			log.Printf("RTDB发送失败: %v", err)
		}
	}
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

func (c *Collector) fetchFromHttp(client *HttpClient) ([]map[string]interface{}, error) {
	if client.config == nil || !client.config.Enabled {
		return nil, fmt.Errorf("HTTP未启用")
	}

	httpClient := &http.Client{
		Timeout: time.Duration(client.config.Timeout) * time.Millisecond,
	}

	resp, err := httpClient.Get(client.config.Url)
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
