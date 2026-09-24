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
	"net/url"
	"sort"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	mqtt "github.com/eclipse/paho.mqtt.golang"
	"github.com/robertkrimen/otto"
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
	mu          sync.RWMutex
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
	ruleErr     string // 上次规则文件加载错误；同一错误只告警一次，加载成功复位

	// 批次数据质量统计：期望集 = C# GET /api/tags（enabled||active，key=item_id||node_id||name）；
	// 10s 窗口并集——SSE 快照(全量)与变化推送(增量) payload 同构无法区分，
	// 按单批报缺失会对增量批大量误报，窗口并集只反映"近 10s 是否读到过"
	taskLabel         string
	statMu            sync.Mutex
	expected          map[string]struct{}
	expectedErr       string
	lastExpectedFetch time.Time
	windowSeen map[string]struct{}
	windowStart time.Time
	statReadyAt time.Time // 期望集就绪预热截止：此前窗口只报读到数、不判定缺失
}

func NewCollector(config *AppConfig) *Collector {
	return &Collector{
		config: config,
	}
}

// snapshot 在读锁下取运行态引用副本；任务 goroutine 全程只用副本，
// 避免与热加载 Reload/Start/Stop 的字段写构成 data race。
func (c *Collector) snapshot() (httpClients []*HttpClient, mqtt *MqttClient, rtdb *RtdbClient) {
	c.mu.RLock()
	defer c.mu.RUnlock()
	return c.httpClients, c.mqttClient, c.rtdbClient
}

func (c *Collector) Start() error {
	c.mu.Lock()
	defer c.mu.Unlock()

	ctx, cancel := context.WithCancel(context.Background())
	c.cancelFunc = cancel

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

	for i, task := range c.config.Tasks {
		if task.Enabled {
			runner := &TaskRunner{
				task:        task,
				config:      c.config,
				transformer: NewKeyTransformer(),
				taskLabel:   fmt.Sprintf("task%d", i+1),
			}
			transformFile := "transform.json"
			if task.HttpSource != "" {
				transformFile = "transform_" + task.HttpSource + ".json"
			}
			if err := runner.transformer.LoadFromFile(transformFile); err != nil && !os.IsNotExist(err) {
				log.Printf("加载规则文件 %s 失败: %v（按无规则运行）", transformFile, err)
			}
			go runner.run(ctx, c)
		}
	}

	c.running = true
	return nil
}

func (c *Collector) Stop() {
	c.mu.Lock()
	defer c.mu.Unlock()

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

func (c *Collector) Reload(newConfig *AppConfig) error {
	c.Stop()
	c.mu.Lock()
	oldConfig := c.config
	c.config = newConfig
	c.mu.Unlock()
	if err := c.Start(); err != nil {
		c.mu.Lock()
		c.config = oldConfig
		c.mu.Unlock()
		if rbErr := c.Start(); rbErr != nil {
			log.Printf("回滚旧配置后重启仍失败: %v", rbErr)
		}
		return fmt.Errorf("热加载失败（运行时已回滚旧配置）: %v", err)
	}
	log.Println("✅ 配置已热加载")
	return nil
}

func (tr *TaskRunner) run(ctx context.Context, collector *Collector) {
	// 数据源 URL 含 /api/stream 时走 SSE 订阅推送，否则保持原有定时轮询
	if tr.task.HttpSource != "" {
		httpClients, _, _ := collector.snapshot()
		for _, client := range httpClients {
			if client.config.Name == tr.task.HttpSource && strings.Contains(client.config.Url, "/api/stream") {
				tr.maybeRefreshExpected(client)
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

// sseHTTPClient SSE 专用客户端：不设总超时（长连接需持续读），仅拨号阶段 10 秒上限，
// 防止黑洞地址让订阅任务永久挂死；配合带 ctx 的请求，Stop/热加载可立即断开。
var sseHTTPClient = &http.Client{
	Transport: func() http.RoundTripper {
		tr := http.DefaultTransport.(*http.Transport).Clone()
		tr.DialContext = (&net.Dialer{Timeout: 10 * time.Second}).DialContext
		return tr
	}(),
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
		req, err := http.NewRequestWithContext(ctx, "GET", streamURL, nil)
		if err != nil {
			log.Printf("SSE 请求创建失败: %v", err)
			time.Sleep(backoff)
			continue
		}
		applyApiToken(req, client.config.Token)
		resp, err := sseHTTPClient.Do(req)
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
		tr.maybeRefreshExpected(client)

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
					// 长连接期间唯一周期点：不挂这里则期望集只在连接建立时刷一次，
					// C# 增删标签后统计口径冻结到断线重连/重启为止
					tr.maybeRefreshExpected(client)
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

// refreshExpected 拉取 C# 代理 GET /api/tags 刷新期望集合；
// key 口径 item_id||node_id||name 与 C# 前端 tagKey 一致，仅 enabled||active 计入
//（与 C# ApplyTags 订阅口径相同），保证"期望=代理页面可见标签"同一事实来源
func (tr *TaskRunner) refreshExpected(client *HttpClient) error {
	u, err := url.Parse(client.config.Url)
	if err != nil {
		return fmt.Errorf("解析数据源URL失败: %v", err)
	}
	u.Path = "/api/tags"
	u.RawQuery = ""
	u.Fragment = ""

	httpClient := &http.Client{Timeout: 5 * time.Second}
	req, err := http.NewRequest("GET", u.String(), nil)
	if err != nil {
		return fmt.Errorf("期望集请求创建失败: %v", err)
	}
	applyApiToken(req, client.config.Token)
	resp, err := httpClient.Do(req)
	if err != nil {
		return fmt.Errorf("期望集请求失败: %v", err)
	}
	defer resp.Body.Close()
	if resp.StatusCode != 200 {
		return fmt.Errorf("期望集请求状态码 %d", resp.StatusCode)
	}
	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return fmt.Errorf("期望集读取响应失败: %v", err)
	}
	var apiResp struct {
		Success bool   `json:"success"`
		Message string `json:"message"`
		Data    []struct {
			NodeId  string `json:"node_id"`
			ItemId  string `json:"item_id"`
			Name    string `json:"name"`
			Enabled bool   `json:"enabled"`
			Active  bool   `json:"active"`
		} `json:"data"`
	}
	if err := json.Unmarshal(body, &apiResp); err != nil {
		return fmt.Errorf("期望集解析JSON失败: %v", err)
	}
	if !apiResp.Success {
		return fmt.Errorf("期望集API返回错误: %s", apiResp.Message)
	}

	next := make(map[string]struct{}, len(apiResp.Data))
	for _, t := range apiResp.Data {
		if !t.Enabled && !t.Active {
			continue
		}
		key := t.ItemId
		if key == "" {
			key = t.NodeId
		}
		if key == "" {
			key = t.Name
		}
		if key == "" {
			continue
		}
		next[key] = struct{}{}
	}
	tr.statMu.Lock()
	// 集合新增 key 时给 20 秒预热：C# 保存后新订阅项要等 AddItems+初值+首个快照
	// 才会进流，此时段判定缺失必然是误报（"增删标签后先看到一串未读到"）
	for k := range next {
		if _, ok := tr.expected[k]; !ok {
			tr.statReadyAt = time.Now().Add(20 * time.Second)
			break
		}
	}
	tr.expected = next
	tr.statMu.Unlock()
	return nil
}

// maybeRefreshExpected 懒刷新：成功 30 秒一次，失败 15 秒后重试；
// 同一错误只 Warn 一次（复用 ruleErr 的去重模式，防黑洞地址刷屏）。
// 30 秒是"增删标签后无需重启即可感知"与请求频率的平衡：/api/tags 是本地轻量接口
func (tr *TaskRunner) maybeRefreshExpected(client *HttpClient) {
	tr.statMu.Lock()
	if time.Since(tr.lastExpectedFetch) < 30*time.Second {
		tr.statMu.Unlock()
		return
	}
	tr.statMu.Unlock()

	if err := tr.refreshExpected(client); err != nil {
		tr.statMu.Lock()
		sameErr := err.Error() == tr.expectedErr
		tr.expectedErr = err.Error()
		// lastExpectedFetch 回拨 15 秒 = 失败后 15 秒重试（30s 节流 - 15s 回拨）
		tr.lastExpectedFetch = time.Now().Add(-15 * time.Second)
		tr.statMu.Unlock()
		if !sameErr {
			log.Printf("批次质量期望集拉取失败（15秒后重试）: %v", err)
		}
		return
	}

	tr.statMu.Lock()
	tr.expectedErr = ""
	tr.lastExpectedFetch = time.Now()
	n := len(tr.expected)
	label := tr.taskLabel
	tr.statMu.Unlock()
	log.Printf("批次质量期望集已刷新 [%s]: %d 个标签（C# 代理需 ≥ 本批次版本以对齐 key 口径）", label, n)
}

// noteBatch 把本批 key 并入 10s 统计窗口，窗口到期输出一条汇总并重置。
// 挂在 processAndPublish 入口：SSE 与轮询两条采集路径的共同汇聚点
func (tr *TaskRunner) noteBatch(rawData []map[string]interface{}) {
	tr.statMu.Lock()
	defer tr.statMu.Unlock()

	now := time.Now()
	if tr.windowStart.IsZero() {
		tr.windowStart = now
		tr.windowSeen = make(map[string]struct{})
	}
	for _, item := range rawData {
		if k, ok := item["topic"].(string); ok && k != "" {
			tr.windowSeen[k] = struct{}{}
		}
	}
	if now.Sub(tr.windowStart) < 10*time.Second {
		return
	}

	seen := tr.windowSeen
	expected := tr.expected
	label := tr.taskLabel
	tr.windowSeen = make(map[string]struct{})
	tr.windowStart = now

	if len(expected) == 0 {
		log.Printf("📊 批次质量 [%s] 近10s: 读到 %d 个不同key（期望集未获取）", label, len(seen))
		return
	}
	// 预热期内只报读到数不判定缺失：期望集刚刷新（含启动首次）时，
	// 新增 key 尚未进流（订阅建立/初值/快照各需时间），缺失名单全是误报
	if now.Before(tr.statReadyAt) {
		log.Printf("📊 批次质量 [%s] 预热中（新期望集 20s 内不判定缺失）: 读到 %d/%d", label, len(seen), len(expected))
		return
	}
	var missing []string
	hit := 0
	for k := range expected {
		if _, ok := seen[k]; ok {
			hit++
		} else {
			missing = append(missing, k)
		}
	}
	sort.Strings(missing)
	if len(missing) == 0 {
		log.Printf("📊 批次质量 [%s] 近10s: 读到 %d/%d, 无缺失", label, hit, len(expected))
		return
	}
	shown := missing
	extra := ""
	if len(shown) > 30 {
		shown = shown[:30]
		extra = fmt.Sprintf(" ...共%d个", len(missing))
	}
	log.Printf("📊 批次质量 [%s] 近10s: 读到 %d/%d, 未读到 %d: [%s]%s",
		label, hit, len(expected), len(missing), strings.Join(shown, ", "), extra)
}

func (tr *TaskRunner) collectData(collector *Collector) {
	transformFile := "transform.json"
	if tr.task.HttpSource != "" {
		transformFile = "transform_" + tr.task.HttpSource + ".json"
	}
	if err := tr.transformer.LoadFromFile(transformFile); err != nil {
		// ENOENT = 规则文件可选（未配置即无规则），不算错误；其余错误同一条只报一次防刷屏
		if !os.IsNotExist(err) && err.Error() != tr.ruleErr {
			tr.ruleErr = err.Error()
			log.Printf("加载规则文件 %s 失败: %v（按无规则运行）", transformFile, err)
		}
	} else {
		tr.ruleErr = ""
	}

	var rawData []map[string]interface{}
	httpClients, _, _ := collector.snapshot()

	// 期望集懒刷新（成功 30 秒/失败 15 秒周期），与数据拉取解耦：
	// 拉取失败不影响采集，仅统计日志分母缺失
	if tr.task.HttpSource != "" {
		for _, client := range httpClients {
			if client.config.Name == tr.task.HttpSource {
				tr.maybeRefreshExpected(client)
				break
			}
		}
	} else if len(httpClients) > 0 {
		tr.maybeRefreshExpected(httpClients[0])
	}

	if tr.task.HttpSource != "" {
		for _, client := range httpClients {
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
	} else if len(httpClients) > 0 {
		for _, client := range httpClients {
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
	tr.noteBatch(rawData)

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

	_, mqttC, rtdbC := collector.snapshot()
	if mqttC != nil && mqttC.IsConnected() {
		if err := mqttC.Publish(msg, tr.task.HttpSource); err != nil {
			log.Printf("MQTT发送失败: %v", err)
		}
	}

	if rtdbC != nil && rtdbC.IsConnected() {
		if err := rtdbC.Send(msg, tr.task.HttpSource); err != nil {
			log.Printf("RTDB发送失败: %v", err)
		}
	}
}

type RtdbClient struct {
	config    *RtdbConfig
	connected bool
	// conns/addrs 双写目标：Host 支持逗号分隔多个地址，同 format 同批写入；
	// 部分地址不可达时保留可用连接继续发送（冗余双写），全部不可达才视为未连接
	conns []net.Conn
	addrs []string

	shortKeySkipped atomic.Bool // 前缀解析跳过告警只打一次，防止逐 tick 刷屏

	// 成功路径不逐条/逐批打日志（高频刷屏），改为 atomic 累计 + 60s 窗口汇总；
	// Send 可被多个 TaskRunner 并发调用，计数与窗口均需原子操作防 data race
	sentTotal       atomic.Int64 // 累计成功发送行数
	lastSummaryNano atomic.Int64 // 上次汇总日志的 UnixNano，按窗口节流
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

	var targets []string
	for _, h := range strings.Split(c.config.Host, ",") {
		h = strings.TrimSpace(h)
		if h != "" {
			targets = append(targets, fmt.Sprintf("%s:%d", h, c.config.Port))
		}
	}
	if len(targets) == 0 {
		return fmt.Errorf("RTDB地址或端口未配置")
	}

	c.conns = nil
	c.addrs = nil
	var failed []string
	for _, addr := range targets {
		conn, err := net.DialTimeout("tcp", addr, 5*time.Second)
		if err != nil {
			failed = append(failed, fmt.Sprintf("%s: %v", addr, err))
			log.Printf("⚠️ RTDB %s 连接失败: %v（继续尝试其余地址）", addr, err)
			continue
		}
		c.conns = append(c.conns, conn)
		c.addrs = append(c.addrs, addr)
		log.Printf("✅ RTDB已连接到 %s", addr)
	}

	if len(c.conns) == 0 {
		return fmt.Errorf("连接RTDB失败: %s", strings.Join(failed, "; "))
	}
	c.connected = true
	c.lastSummaryNano.Store(time.Now().UnixNano())
	return nil
}

// Disconnect 幂等：未连接/重复调用直接返回，防止 defer 与 Stop 双关时误报日志。
func (c *RtdbClient) Disconnect() {
	if len(c.conns) == 0 {
		return
	}
	for _, conn := range c.conns {
		conn.Close()
	}
	c.conns = nil
	c.addrs = nil
	c.connected = false
	log.Printf("📴 RTDB已断开（累计发送 %d 条）", c.sentTotal.Load())
}

func (c *RtdbClient) IsConnected() bool {
	return c.connected && len(c.conns) > 0
}

func (c *RtdbClient) Send(message map[string]interface{}, source string) error {
	if !c.IsConnected() {
		return fmt.Errorf("RTDB未连接")
	}

	values, ok := message["values"].(map[string]interface{})
	if !ok {
		return fmt.Errorf("无效的消息格式")
	}

	metadata, _ := message["metadata"].(map[string]map[string]interface{})
	failCounts := make([]int, len(c.conns))
	sentAny := false
	var batchSent int64
	var lastErr error

	// 整批渲染进单个缓冲：KairosDB telnet 行协议天然支持一次写多行，
	// 669 标签原先每批 669 次 Write 系统调用，批量化后每地址仅 1 次
	var buf strings.Builder
	lineCount := 0
	for key, value := range values {
		line, ok := c.formatLine(key, value, metadata[key])
		if !ok {
			if c.shortKeySkipped.CompareAndSwap(false, true) {
				log.Printf("RTDB: 键 %q 不足 5 字节，无法解析 device/component 前缀，已跳过该点（仅提示一次）", key)
			}
			continue
		}
		buf.WriteString(line)
		buf.WriteByte('\n')
		lineCount++
	}
	if lineCount == 0 {
		return nil
	}

	if c.config.Debug {
		log.Printf("🔍 RTDB debug [%s] 本批 %d 行:\n%s", source, lineCount, buf.String())
	}

	// 双写语义：整批写单地址，单地址失败不影响其余地址；sentAny=任一地址成功即本批有效
	data := []byte(buf.String())
	for i, conn := range c.conns {
		if _, err := conn.Write(data); err != nil {
			failCounts[i]++
			lastErr = err
			continue
		}
		sentAny = true
		batchSent += int64(lineCount)
	}

	for i, n := range failCounts {
		if n > 0 && i < len(c.addrs) {
			log.Printf("⚠️ RTDB[%s] 本批 %d 行写入失败", c.addrs[i], lineCount)
		}
	}
	if !sentAny {
		return fmt.Errorf("发送数据失败: 全部 %d 个地址均不可写（最后错误: %v）", len(c.conns), lastErr)
	}

	c.sentTotal.Add(batchSent)
	// 每 60s 至多打一条累计汇总：CAS 保证并发 Send 下同一窗口只有一个 goroutine 输出
	now := time.Now().UnixNano()
	last := c.lastSummaryNano.Load()
	if now-last >= int64(time.Minute) && c.lastSummaryNano.CompareAndSwap(last, now) {
		log.Printf("📤 RTDB累计已发送 %d 条", c.sentTotal.Load())
	}
	return nil
}

// formatLine 渲染单点输出行；返回 ok=false 表示该点应被跳过（不发送）。
func (c *RtdbClient) formatLine(key string, value interface{}, meta map[string]interface{}) (string, bool) {
	format := c.config.Format
	if format == "" {
		format = "{key},{value},{quality},{timestamp}"
	}

	// {device}/{component} 按字节解析 key 前缀（约定 ASCII：4 字节工厂编码 + 1 字节分组）；
	// 不足 5 字节无法解析，由调用方跳过，防止切片越界 panic
	needPrefix := strings.Contains(format, "{device}") || strings.Contains(format, "{component}")
	if needPrefix && len(key) < 5 {
		return "", false
	}

	quality := 192
	// RTDB 时间戳按秒（epoch 秒，与 jiaohua 参照实现一致）；metadata 内是毫秒，取到后 /1000。
	// MQTT 侧仍用毫秒，仅此处转换单位。
	timestamp := time.Now().Unix()
	if meta != nil {
		if q, ok := meta["quality"].(int); ok {
			quality = q
		}
		if t, ok := meta["timestamp"].(int64); ok {
			timestamp = t / 1000
		}
	}

	result := format
	result = strings.ReplaceAll(result, "{key}", key)
	result = strings.ReplaceAll(result, "{value}", fmt.Sprintf("%v", value))
	result = strings.ReplaceAll(result, "{quality}", fmt.Sprintf("%d", quality))
	result = strings.ReplaceAll(result, "{timestamp}", fmt.Sprintf("%d", timestamp))
	if needPrefix {
		result = strings.ReplaceAll(result, "{device}", key[:4])
		result = strings.ReplaceAll(result, "{component}", key[4:5])
	}

	return result, true
}

type MqttClient struct {
	config    *MqttConfig
	client    mqtt.Client
	vm        *otto.Otto
	vmMu      sync.Mutex
	connected bool
}

func NewMqttClient(config *MqttConfig) *MqttClient {
	c := &MqttClient{
		config: config,
	}
	// 预创建 JS 引擎（仅在配置了 js_transform 时），避免每条消息重复创建
	if config != nil && config.JsTransform != "" {
		c.vm = otto.New()
	}
	return c
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

// renderPayloads 依据 format / js_transform / split 配置，把一批数据渲染成若干条待发布报文。
func (c *MqttClient) renderPayloads(message map[string]interface{}) ([]string, error) {
	format := c.config.Format

	// full（或空）：整包 JSON，保持原有行为不变
	if format == "" || format == "full" {
		b, err := json.Marshal(message)
		if err != nil {
			return nil, fmt.Errorf("JSON序列化失败: %v", err)
		}
		return []string{string(b)}, nil
	}

	// flat：仅 values 映射
	if format == "flat" {
		if values, ok := message["values"].(map[string]interface{}); ok {
			b, err := json.Marshal(values)
			if err != nil {
				return nil, fmt.Errorf("JSON序列化失败: %v", err)
			}
			return []string{string(b)}, nil
		}
		b, err := json.Marshal(message)
		if err != nil {
			return nil, fmt.Errorf("JSON序列化失败: %v", err)
		}
		return []string{string(b)}, nil
	}

	// 自定义模板 / js_transform：逐点渲染
	values, _ := message["values"].(map[string]interface{})
	metadata, _ := message["metadata"].(map[string]interface{})

	// 按 key 排序，保证输出顺序确定
	keys := make([]string, 0, len(values))
	for k := range values {
		keys = append(keys, k)
	}
	sort.Strings(keys)

	payloads := make([]string, 0, len(keys))
	for _, key := range keys {
		quality := 192
		timestamp := time.Now().UnixMilli()
		if meta, ok := metadata[key].(map[string]interface{}); ok {
			if q, ok := meta["quality"].(int); ok {
				quality = q
			}
			if t, ok := meta["timestamp"].(int64); ok {
				timestamp = t
			}
		}

		var s string
		var err error
		if c.config.JsTransform != "" {
			s, err = c.applyJsTransform(key, values[key], quality, timestamp)
		} else {
			s = renderTemplate(format, key, values[key], quality, timestamp)
		}
		if err != nil {
			return nil, err
		}
		payloads = append(payloads, s)
	}

	// 扇出：split=true 时每点一条报文；否则合并为一包（默认）
	if c.config.Split {
		return payloads, nil
	}
	return []string{strings.Join(payloads, "\n")}, nil
}

// renderTemplate 按占位符替换渲染单行（与 RTDB 的 formatLine 保持同一套占位符）
func renderTemplate(format, key string, value interface{}, quality int, timestamp int64) string {
	result := format
	result = strings.ReplaceAll(result, "{key}", key)
	result = strings.ReplaceAll(result, "{value}", fmt.Sprintf("%v", value))
	result = strings.ReplaceAll(result, "{quality}", fmt.Sprintf("%d", quality))
	result = strings.ReplaceAll(result, "{timestamp}", fmt.Sprintf("%d", timestamp))
	return result
}

// applyJsTransform 用 otto 执行用户脚本，变量 point={key,value,quality,timestamp}；
// 返回字符串直接作为电文，或对象经 JSON 序列化。
func (c *MqttClient) applyJsTransform(key string, value interface{}, quality int, timestamp int64) (string, error) {
	if c.vm == nil {
		return "", fmt.Errorf("js_transform 需要引入 github.com/robertkrimen/otto 依赖（当前构建未包含）")
	}
	// otto 非线程安全：多 task goroutine 并发 Publish 时 Set/Run 必须互斥
	c.vmMu.Lock()
	defer c.vmMu.Unlock()
	input := map[string]interface{}{
		"key":       key,
		"value":     value,
		"quality":   quality,
		"timestamp": timestamp,
	}
	if err := c.vm.Set("point", input); err != nil {
		return "", fmt.Errorf("JS变量注入失败: %v", err)
	}
	v, err := c.vm.Run(c.config.JsTransform)
	if err != nil {
		return "", fmt.Errorf("JS执行失败: %v", err)
	}
	exported, err := v.Export()
	if err != nil {
		return "", fmt.Errorf("JS结果导出失败: %v", err)
	}
	switch out := exported.(type) {
	case string:
		return out, nil
	default:
		b, err := json.Marshal(out)
		if err != nil {
			return "", fmt.Errorf("JS结果序列化失败: %v", err)
		}
		return string(b), nil
	}
}

func (c *MqttClient) Publish(message map[string]interface{}, source string) error {
	if !c.IsConnected() {
		return fmt.Errorf("MQTT未连接")
	}

	payloads, err := c.renderPayloads(message)
	if err != nil {
		return err
	}

	qos := byte(c.config.Qos)
	for _, payload := range payloads {
		token := c.client.Publish(c.config.Topic, qos, c.config.Retain, payload)
		if token.Wait() && token.Error() != nil {
			log.Printf("MQTT发布失败 [数据源:%s]: %v", source, token.Error())
			return token.Error()
		}
	}

	log.Printf("📤 MQTT发布成功 [数据源:%s] topic=%s 条数=%d", source, c.config.Topic, len(payloads))
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

// applyApiToken 目标端启用 api_token 鉴权时附加 X-Api-Token 请求头；token 为空则不加，保持未启用鉴权时的原行为。
func applyApiToken(req *http.Request, token string) {
	if token != "" {
		req.Header.Set("X-Api-Token", token)
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

	var body io.Reader
	if method != "GET" {
		body = strings.NewReader(string(jsonData))
	}
	req, err := http.NewRequest(method, c.config.Url, body)
	if err != nil {
		log.Printf("HTTP请求创建失败: %v", err)
		return
	}
	applyApiToken(req, c.config.Token)
	if method != "GET" {
		req.Header.Set("Content-Type", "application/json")
	}
	resp, err := client.Do(req)

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

	req, err := http.NewRequest("GET", client.config.Url, nil)
	if err != nil {
		return nil, fmt.Errorf("HTTP请求创建失败: %v", err)
	}
	applyApiToken(req, client.config.Token)
	resp, err := httpClient.Do(req)
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
