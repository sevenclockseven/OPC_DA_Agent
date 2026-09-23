package main

import (
	"encoding/json"
	"log"
	"os"
	"regexp"
	"strings"
	"sync"
)

type KeyTransformer struct {
	mu      sync.Mutex
	rules   []TransformRule
	enabled bool
	reCache map[string]*regexp.Regexp
	reBad   map[string]bool
}

type TransformRule struct {
	RuleType    string `json:"rule_type"`
	Pattern     string `json:"pattern"`
	Replacement string `json:"replacement"`
	Index       int    `json:"index"`
	Enabled     bool   `json:"enabled"`
	Description string `json:"description"`
}

type TransformConfig struct {
	Enabled       bool            `json:"enabled"`
	DefaultPrefix string          `json:"default_prefix"`
	DefaultSuffix string          `json:"default_suffix"`
	Rules         []TransformRule `json:"rules"`
}

func NewKeyTransformer() *KeyTransformer {
	return &KeyTransformer{
		rules:   []TransformRule{},
		enabled: true,
		reCache: make(map[string]*regexp.Regexp),
		reBad:   make(map[string]bool),
	}
}

func (kt *KeyTransformer) LoadFromFile(path string) error {
	data, err := os.ReadFile(path)
	if err != nil {
		return err
	}

	var config TransformConfig
	if err := json.Unmarshal(data, &config); err != nil {
		return err
	}

	kt.mu.Lock()
	defer kt.mu.Unlock()
	kt.enabled = config.Enabled
	kt.rules = config.Rules
	return nil
}

func (kt *KeyTransformer) SetEnabled(enabled bool) {
	kt.mu.Lock()
	defer kt.mu.Unlock()
	kt.enabled = enabled
}

func (kt *KeyTransformer) IsEnabled() bool {
	kt.mu.Lock()
	defer kt.mu.Unlock()
	return kt.enabled
}

func (kt *KeyTransformer) AddRule(rule TransformRule) {
	kt.mu.Lock()
	defer kt.mu.Unlock()
	kt.rules = append(kt.rules, rule)
}

func (kt *KeyTransformer) Transform(originalKey string) string {
	if originalKey == "" {
		return originalKey
	}

	kt.mu.Lock()
	defer kt.mu.Unlock()

	if !kt.enabled {
		return originalKey
	}

	result := originalKey

	for _, rule := range kt.rules {
		if !rule.Enabled {
			continue
		}
		result = kt.applyRule(result, rule)
	}

	return result
}

// applyRule 必须在持有 kt.mu 的前提下调用。
func (kt *KeyTransformer) applyRule(key string, rule TransformRule) string {
	switch rule.RuleType {
	case "RemovePrefix":
		if strings.HasPrefix(key, rule.Pattern) {
			return key[len(rule.Pattern):]
		}
		return key

	case "RemoveSuffix":
		if strings.HasSuffix(key, rule.Pattern) {
			return key[:len(key)-len(rule.Pattern)]
		}
		return key

	case "AddPrefix":
		return rule.Replacement + key

	case "AddSuffix":
		return key + rule.Replacement

	case "Replace":
		return strings.ReplaceAll(key, rule.Pattern, rule.Replacement)

	case "RegexReplace":
		if rule.Pattern == "" {
			return key
		}
		re := kt.compileLocked(rule.Pattern)
		if re == nil {
			return key
		}
		return re.ReplaceAllString(key, rule.Replacement)

	case "ToLower":
		return strings.ToLower(key)

	case "ToUpper":
		return strings.ToUpper(key)

	case "Trim":
		return strings.TrimSpace(key)

	case "SplitAndSelect":
		if rule.Pattern != "" {
			parts := strings.Split(key, rule.Pattern)
			if rule.Index >= 0 && rule.Index < len(parts) {
				return parts[rule.Index]
			}
		}
		return key

	default:
		return key
	}
}

// compileLocked 缓存正则编译结果；无效 pattern 返回 nil 并只告警一次，
// 跳过该规则而不是用 MustCompile 让坏配置把采集器 panic 打崩。
func (kt *KeyTransformer) compileLocked(pattern string) *regexp.Regexp {
	if re, ok := kt.reCache[pattern]; ok {
		return re
	}
	re, err := regexp.Compile(pattern)
	if err != nil {
		if !kt.reBad[pattern] {
			kt.reBad[pattern] = true
			log.Printf("键名映射正则无效，已跳过规则 %q: %v", pattern, err)
		}
		kt.reCache[pattern] = nil
		return nil
	}
	kt.reCache[pattern] = re
	return re
}

func (kt *KeyTransformer) ExportRules() []TransformRule {
	kt.mu.Lock()
	defer kt.mu.Unlock()
	return append([]TransformRule{}, kt.rules...)
}

func (kt *KeyTransformer) ImportRules(rules []TransformRule) {
	kt.mu.Lock()
	defer kt.mu.Unlock()
	kt.rules = append([]TransformRule{}, rules...)
}

func (kt *KeyTransformer) ClearRules() {
	kt.mu.Lock()
	defer kt.mu.Unlock()
	kt.rules = []TransformRule{}
}

func (kt *KeyTransformer) GetStatus() map[string]interface{} {
	kt.mu.Lock()
	defer kt.mu.Unlock()
	return map[string]interface{}{
		"enabled":    kt.enabled,
		"rule_count": len(kt.rules),
	}
}

func (kt *KeyTransformer) TestTransform(key string) string {
	return kt.Transform(key)
}
