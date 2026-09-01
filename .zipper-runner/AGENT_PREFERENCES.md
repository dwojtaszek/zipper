# Agent × Model Preferences

Set priority: `0` = don't use, `1` = best, `2` = good, `3` = fallback.

| Agent | Model | Priority |
|-------|-------|----------|
| `opencode` | `deepseek/deepseek-chat` | 0|
| `opencode` | `deepseek/deepseek-reasoner` | 0|
| `opencode` | `deepseek/deepseek-v4-flash` | 0|
| `opencode` | `deepseek/deepseek-v4-pro` | 0|
| `opencode` | `opencode/big-pickle` | 0|
| `opencode` | `opencode/claude-sonnet-4` | 0|
| `opencode` | `opencode/claude-sonnet-4-5` | 0|
| `opencode` | `opencode/claude-sonnet-4-6` | 0|
| `opencode` | `opencode/claude-opus-4-5` | 0|
| `opencode` | `opencode/claude-opus-4-6` | 0|
| `opencode` | `opencode/claude-opus-4-7` | 0|
| `opencode` | `opencode/claude-opus-4-8` | 0|
| `opencode` | `opencode/claude-haiku-4-5` | 0|
| `opencode` | `opencode/deepseek-v4-flash` | 0|
| `opencode` | `opencode/deepseek-v4-flash-free` | 0|
| `opencode` | `default` | 2|
| `opencode` | `opencode/big-pickle` | 3|
| `opencode` | `opencode/deepseek-v4-pro` | 0|
| `opencode` | `opencode/gemini-3-flash` | 0|
| `opencode` | `opencode/gemini-3.1-pro` | 0|
| `opencode` | `opencode/gemini-3.5-flash` | 0|
| `opencode` | `opencode/glm-5` | 0|
| `opencode` | `opencode/glm-5.1` | 0|
| `opencode` | `opencode/glm-5.2` | 0|
| `opencode` | `opencode/gpt-5` | 0|
| `opencode` | `opencode/gpt-5-codex` | 0|
| `opencode` | `opencode/gpt-5.1-codex` | 0|
| `opencode` | `opencode/gpt-5.1-codex-max` | 0|
| `opencode` | `opencode/gpt-5.2-codex` | 0|
| `opencode` | `opencode/gpt-5.3-codex` | 0|
| `opencode` | `opencode/gpt-5.4` | 0|
| `opencode` | `opencode/gpt-5.4-pro` | 0|
| `opencode` | `opencode/gpt-5.5` | 0|
| `opencode` | `opencode/gpt-5.5-pro` | 0|
| `opencode` | `opencode/grok-build-0.1` | 0|
| `opencode` | `opencode/kimi-k2.5` | 0|
| `opencode` | `opencode/kimi-k2.6` | 0|
| `opencode` | `opencode/mimo-v2.5-free` | 0|
| `opencode` | `opencode/minimax-m2.5` | 0|
| `opencode` | `opencode/minimax-m2.7` | 0|
| `opencode` | `opencode/nemotron-3-ultra-free` | 0|
| `opencode` | `opencode/north-mini-code-free` | 0|
| `opencode` | `opencode/qwen3.5-plus` | 0|
| `opencode` | `opencode/qwen3.6-plus` | 0|
| `claude` | `claude-sonnet-4-20250514` (Sonnet 4) | 0|
| `claude` | `claude-sonnet-4-20250514-thinking` (Sonnet 4 thinking) | 0|
| `claude` | `claude-opus-4-20250514` (Opus 4) | 0|
| `claude` | `claude-opus-4-20250514-thinking` (Opus 4 thinking) | 0|
| `claude` | `default` | 1|
| `claude` | `claude-sonnet-4-6-20250622` (Sonnet 4.6) | 0|
| `agy` | `default` | 1|
| `agy` | `gemini-3.7-flash-high` | 1|
| `agy` | `gemini-3.7-flash-medium` | 2|
| `agy` | `gemini-3.7-flash-low` | 3|
| `agy` | `gemini-3.1-pro-high` | 1|
| `agy` | `gemini-3.1-pro-low` | 0|
| `agy` | `gemini-3.5-flash-high` | 0|
| `agy` | `gemini-3.5-flash-medium` | 0|
| `agy` | `gemini-3.5-flash-low` | 0|
| `agy` | `claude-sonnet-4-6` | 3|
| `agy` | `claude-opus-4-6-thinking` | 0|
| `hermes` | `default` | 1|
| `hermes` | `stepfun/step-3.7-flash:free` | 1|
| `hermes` | `deepseek-ai/deepseek-v4-flash` | 2|
| `droid` | `factory-ai-default` (model managed internally, config shows `glm-5.2`) | 3|
| `codex` | `openai/gpt-5.6-terra` | 3|
| `codex` | `openai/gpt-5.5` | 0|
| `codex` | `openai/gpt-5.4` | 0|

## How it works

The runner probes every agent plugin (install + token health), then matches available
(agent, model) pairs against this table. The pair with the lowest non-zero priority
(1 = best) that is healthy gets selected for the run. If no pair is healthy, the
runner exits.

If `ACTIVE_AGENT` env var is set, it acts as a priority hint — the runner still probes
all agents, but that agent's models get first consideration.
