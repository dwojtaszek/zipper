---
description: Created with Workflow Studio
allowed-tools: Task,AskUserQuestion
---
```mermaid
flowchart TD
    start_node_default([Start])
    end_node_default([End])
    agent_1767300766904[agent-1767300766904]
    question_1767300806424{AskUserQuestion:<br/>New Question}

    start_node_default --> agent_1767300766904
    agent_1767300766904 --> question_1767300806424
    question_1767300806424 -->|Option 2| end_node_default
    question_1767300806424 -->|Option 1| end_node_default
```

## Workflow Execution Guide

Follow the Mermaid flowchart above to execute the workflow. Each node type has specific execution methods as described below.

### Execution Methods by Node Type

- **Rectangle nodes**: Execute Sub-Agents using the Task tool
- **Diamond nodes (AskUserQuestion:...)**: Use the AskUserQuestion tool to prompt the user and branch based on their response
- **Diamond nodes (Branch/Switch:...)**: Automatically branch based on the results of previous processing (see details section)
- **Rectangle nodes (Prompt nodes)**: Execute the prompts described in the details section below

### AskUserQuestion Node Details

#### question_1767300806424(New Question)

**Selection mode:** Single Select (branches based on the selected option)

**Options:**
- **Option 1**: First option
- **Option 2**: Second option
