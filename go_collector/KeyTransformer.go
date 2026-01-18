package main

import (
	"regexp"
	"strings"
)

// KeyTransformer 键名转换器
type KeyTransformer struct {
	rules []TransformRule
}

// TransformRule 转换规则
type TransformRule struct {
	RuleType    string `json:"rule_type"`
	Pattern     string `json:"pattern"`
	Replacement string `json:"replacement"`
	Index       int    `json:"index"`
	Enabled     bool   `json:"enabled"`
	Description string `json:"description"`
}

// NewKeyTransformer 创建键名转换器
func NewKeyTransformer() *KeyTransformer {
	return &KeyTransformer{
		rules: []TransformRule{},
	}
}

// AddRule 添加转换规则
func (kt *KeyTransformer) AddRule(rule TransformRule) {
	kt.rules = append(kt.rules, rule)
}

// Transform 转换键名
func (kt *KeyTransformer) Transform(originalKey string) string {
	if originalKey == "" {
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

// applyRule 应用单个规则
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
		if rule.Pattern != "" {
			re := regexp.MustCompile(rule.Pattern)
			return re.ReplaceAllString(key, rule.Replacement)
		}
		return key

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

	case "Format":
		// 简单的格式化支持
		return key

	default:
		return key
	}
}

// TransformDictionary 批量转换字典
func (kt *KeyTransformer) TransformDictionary(data map[string]interface{}) map[string]string {
	result := make(map[string]string)

	for key, value := range data {
		transformedKey := kt.Transform(key)
		if strValue, ok := value.(string); ok {
			result[transformedKey] = strValue
		} else {
			result[transformedKey] = ""
		}
	}

	return result
}

// ExportRules 导出规则
func (kt *KeyTransformer) ExportRules() []TransformRule {
	return append([]TransformRule{}, kt.rules...)
}

// ImportRules 导入规则
func (kt *KeyTransformer) ImportRules(rules []TransformRule) {
	kt.rules = append([]TransformRule{}, rules...)
}

// ClearRules 清空规则
func (kt *KeyTransformer) ClearRules() {
	kt.rules = []TransformRule{}
}
