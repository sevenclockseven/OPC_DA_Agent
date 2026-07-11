package main

import (
	"encoding/json"
	"os"
	"regexp"
	"strings"
)

type KeyTransformer struct {
	rules  []TransformRule
	enabled bool
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
	Enabled        bool           `json:"enabled"`
	DefaultPrefix  string         `json:"default_prefix"`
	DefaultSuffix  string         `json:"default_suffix"`
	Rules          []TransformRule `json:"rules"`
}

func NewKeyTransformer() *KeyTransformer {
	return &KeyTransformer{
		rules:   []TransformRule{},
		enabled: true,
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

	kt.enabled = config.Enabled
	kt.rules = config.Rules
	return nil
}

func (kt *KeyTransformer) SetEnabled(enabled bool) {
	kt.enabled = enabled
}

func (kt *KeyTransformer) IsEnabled() bool {
	return kt.enabled
}

func (kt *KeyTransformer) AddRule(rule TransformRule) {
	kt.rules = append(kt.rules, rule)
}

func (kt *KeyTransformer) Transform(originalKey string) string {
	if originalKey == "" {
		return originalKey
	}

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

	default:
		return key
	}
}

func (kt *KeyTransformer) ExportRules() []TransformRule {
	return append([]TransformRule{}, kt.rules...)
}

func (kt *KeyTransformer) ImportRules(rules []TransformRule) {
	kt.rules = append([]TransformRule{}, rules...)
}

func (kt *KeyTransformer) ClearRules() {
	kt.rules = []TransformRule{}
}

func (kt *KeyTransformer) GetStatus() map[string]interface{} {
	return map[string]interface{}{
		"enabled":   kt.enabled,
		"rule_count": len(kt.rules),
	}
}

func (kt *KeyTransformer) TestTransform(key string) string {
	return kt.Transform(key)
}
