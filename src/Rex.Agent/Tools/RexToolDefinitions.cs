using System.Text.Json;
using Mediahost.Llm.Models;

namespace Rex.Agent.Tools;

public static class RexToolDefinitions
{
    public static List<ToolDefinition> GetTools() =>
    [
        // ── Filesystem ────────────────────────────────────────────────────────

        new ToolDefinition(
            "read_file",
            "Read a file from /workspace or /project",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Absolute path to the file" }
              },
              "required": ["path"]
            }
            """)),

        new ToolDefinition(
            "write_file",
            "Write or overwrite a file (creates parent directories if needed)",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path":    { "type": "string", "description": "Absolute path to write" },
                "content": { "type": "string", "description": "Complete file content" }
              },
              "required": ["path", "content"]
            }
            """)),

        new ToolDefinition(
            "list_directory",
            "List files and directories at a path",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path":      { "type": "string", "description": "Directory path to list" },
                "recursive": { "type": "boolean", "description": "Whether to list recursively (default false)" }
              },
              "required": ["path"]
            }
            """)),

        new ToolDefinition(
            "delete_file",
            "Delete a file",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Absolute path of the file to delete" }
              },
              "required": ["path"]
            }
            """)),

        new ToolDefinition(
            "search_code",
            "Search for text across files in a directory (grep)",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "directory": { "type": "string", "description": "Directory to search in" },
                "pattern":   { "type": "string", "description": "Text or regex pattern to search" },
                "glob":      { "type": "string", "description": "Optional file glob e.g. '*.cs' or '*.yml'" }
              },
              "required": ["directory", "pattern"]
            }
            """)),

        // ── Git ───────────────────────────────────────────────────────────────

        new ToolDefinition(
            "git_status",
            "Show working tree status of a git repository",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "repo_path": { "type": "string", "description": "Path to the git repository" }
              },
              "required": ["repo_path"]
            }
            """)),

        new ToolDefinition(
            "git_clone",
            "Clone a git repository into /workspace/{name}",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "url":        { "type": "string", "description": "Repository URL to clone" },
                "local_name": { "type": "string", "description": "Name of local directory under /workspace" }
              },
              "required": ["url", "local_name"]
            }
            """)),

        new ToolDefinition(
            "git_pull",
            "Pull latest changes from origin",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "repo_path": { "type": "string", "description": "Path to the git repository" }
              },
              "required": ["repo_path"]
            }
            """)),

        new ToolDefinition(
            "git_diff",
            "Show staged and unstaged changes in a repository",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "repo_path": { "type": "string", "description": "Path to the git repository" }
              },
              "required": ["repo_path"]
            }
            """)),

        new ToolDefinition(
            "git_log",
            "Show recent commit history",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "repo_path": { "type": "string", "description": "Path to the git repository" },
                "limit":     { "type": "number",  "description": "Number of commits to show (default 10)" }
              },
              "required": ["repo_path"]
            }
            """)),

        new ToolDefinition(
            "git_add_commit",
            "Stage files and create a commit",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "repo_path": { "type": "string", "description": "Path to the git repository" },
                "message":   { "type": "string", "description": "Commit message" },
                "files":     { "type": "array", "items": { "type": "string" }, "description": "Specific files to stage. If empty all changes are staged." }
              },
              "required": ["repo_path", "message"]
            }
            """)),

        new ToolDefinition(
            "git_push",
            "Push commits to the remote repository",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "repo_path": { "type": "string", "description": "Path to the git repository" },
                "branch":    { "type": "string", "description": "Branch to push (default: current branch)" }
              },
              "required": ["repo_path"]
            }
            """)),

        new ToolDefinition(
            "git_create_branch",
            "Create and checkout a new branch",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "repo_path":   { "type": "string", "description": "Path to the git repository" },
                "branch_name": { "type": "string", "description": "Name of the new branch" }
              },
              "required": ["repo_path", "branch_name"]
            }
            """)),

        // ── GitHub API ────────────────────────────────────────────────────────

        new ToolDefinition(
            "gh_list_repos",
            "List all GitHub repositories for the authenticated user",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {}
            }
            """)),

        new ToolDefinition(
            "gh_get_repo",
            "Get details about a GitHub repository",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "owner": { "type": "string", "description": "Repository owner" },
                "repo":  { "type": "string", "description": "Repository name" }
              },
              "required": ["owner", "repo"]
            }
            """)),

        new ToolDefinition(
            "gh_create_repo",
            "Create a new GitHub repository",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "name":        { "type": "string",  "description": "Repository name" },
                "description": { "type": "string",  "description": "Repository description" },
                "private":     { "type": "boolean", "description": "Whether the repo is private" }
              },
              "required": ["name"]
            }
            """)),

        new ToolDefinition(
            "gh_list_branches",
            "List branches in a GitHub repository",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "owner": { "type": "string", "description": "Repository owner" },
                "repo":  { "type": "string", "description": "Repository name" }
              },
              "required": ["owner", "repo"]
            }
            """)),

        new ToolDefinition(
            "gh_create_pr",
            "Open a pull request on GitHub",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "owner": { "type": "string", "description": "Repository owner" },
                "repo":  { "type": "string", "description": "Repository name" },
                "title": { "type": "string", "description": "PR title" },
                "body":  { "type": "string", "description": "PR description" },
                "head":  { "type": "string", "description": "Source branch" },
                "base":  { "type": "string", "description": "Target branch (default: main)" }
              },
              "required": ["owner", "repo", "title", "head"]
            }
            """)),

        new ToolDefinition(
            "gh_list_issues",
            "List open issues for a GitHub repository",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "owner": { "type": "string", "description": "Repository owner" },
                "repo":  { "type": "string", "description": "Repository name" }
              },
              "required": ["owner", "repo"]
            }
            """)),

        new ToolDefinition(
            "gh_update_file",
            "Create or update a file directly via the GitHub API",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "owner":   { "type": "string", "description": "Repository owner" },
                "repo":    { "type": "string", "description": "Repository name" },
                "path":    { "type": "string", "description": "File path in the repo" },
                "content": { "type": "string", "description": "File content" },
                "message": { "type": "string", "description": "Commit message" },
                "branch":  { "type": "string", "description": "Branch (default: main)" }
              },
              "required": ["owner", "repo", "path", "content", "message"]
            }
            """)),

        // ── Developer Agent ───────────────────────────────────────────────────

        new ToolDefinition(
            "plan_task",
            "Use a temporary developer agent to produce a structured implementation plan for a task",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "task": { "type": "string", "description": "Description of the task to plan" },
                "context_files": {
                  "type": "object",
                  "description": "Map of filename -> content to give the developer agent as context",
                  "additionalProperties": { "type": "string" }
                }
              },
              "required": ["task"]
            }
            """)),

        new ToolDefinition(
            "develop_file",
            "Use a temporary developer agent to write the complete content of a single file",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "task":        { "type": "string", "description": "What the file should do" },
                "target_file": { "type": "string", "description": "Filename/path the content is for" },
                "context_files": {
                  "type": "object",
                  "description": "Map of filename -> content to give the developer agent as context",
                  "additionalProperties": { "type": "string" }
                }
              },
              "required": ["task", "target_file"]
            }
            """)),

        new ToolDefinition(
            "review_changes",
            "Use a temporary developer agent to review a git diff and check correctness",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "diff":             { "type": "string", "description": "The git diff to review" },
                "task_description": { "type": "string", "description": "The original task the diff should implement" }
              },
              "required": ["diff", "task_description"]
            }
            """)),

        // ── Container Operations ──────────────────────────────────────────────

        new ToolDefinition(
            "container_list",
            "List all containers and their current status",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {}
            }
            """)),

        new ToolDefinition(
            "container_logs",
            "Get recent log lines from a container",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "container_name": { "type": "string", "description": "Container name or ID" },
                "tail":           { "type": "number",  "description": "Number of log lines to return (default 100)" }
              },
              "required": ["container_name"]
            }
            """)),

        new ToolDefinition(
            "container_build",
            "Build a container image via Podman",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "dockerfile":    { "type": "string", "description": "Path to the Dockerfile" },
                "context_path":  { "type": "string", "description": "Build context directory" },
                "tag":           { "type": "string", "description": "Image tag e.g. myapp:latest" }
              },
              "required": ["dockerfile", "context_path", "tag"]
            }
            """)),

        new ToolDefinition(
            "container_restart",
            "Restart a running container",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "container_name": { "type": "string", "description": "Container name or ID" }
              },
              "required": ["container_name"]
            }
            """)),

        new ToolDefinition(
            "container_inspect",
            "Get full configuration and state of a container",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "container_name": { "type": "string", "description": "Container name or ID" }
              },
              "required": ["container_name"]
            }
            """)),

        // ── CI/CD ─────────────────────────────────────────────────────────────

        new ToolDefinition(
            "create_workflow",
            "Write a GitHub Actions workflow YAML file into a repository",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "repo_path":     { "type": "string", "description": "Path to the local git repository" },
                "workflow_name": { "type": "string", "description": "Workflow filename e.g. ci.yml" },
                "content":       { "type": "string", "description": "Complete YAML content of the workflow" }
              },
              "required": ["repo_path", "workflow_name", "content"]
            }
            """)),

        new ToolDefinition(
            "list_workflows",
            "List existing GitHub Actions workflow files in a repository",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "repo_path": { "type": "string", "description": "Path to the git repository" }
              },
              "required": ["repo_path"]
            }
            """)),

        new ToolDefinition(
            "read_workflow",
            "Read an existing GitHub Actions workflow file",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "repo_path":     { "type": "string", "description": "Path to the git repository" },
                "workflow_name": { "type": "string", "description": "Workflow filename e.g. ci.yml" }
              },
              "required": ["repo_path", "workflow_name"]
            }
            """)),

        // ── Memory ────────────────────────────────────────────────────────────

        new ToolDefinition(
            "remember_fact",
            "Store a fact in Rex's permanent memory (repo URLs, GitHub usernames, architecture decisions)",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "key":   { "type": "string", "description": "Short identifier e.g. 'github_username' or 'dockerhub_org'" },
                "value": { "type": "string", "description": "The value to remember" }
              },
              "required": ["key", "value"]
            }
            """)),

        new ToolDefinition(
            "forget_fact",
            "Remove a fact from Rex's permanent memory",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "key": { "type": "string", "description": "The memory key to delete" }
              },
              "required": ["key"]
            }
            """)),

        // ── Agent Scaffolding ─────────────────────────────────────────────────

        new ToolDefinition(
            "intake_agent",
            "Start a new agent scaffolding session and return the 6-question intake form",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "description": { "type": "string", "description": "Brief description of the new agent's purpose" }
              },
              "required": ["description"]
            }
            """)),

        new ToolDefinition(
            "save_intake_answers",
            "Save completed intake answers for a scaffolding session",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "session_id":   { "type": "string", "description": "Scaffolding session UUID" },
                "answers_json": { "type": "string", "description": "JSON string or plain text with answers to all 6 intake questions" }
              },
              "required": ["session_id", "answers_json"]
            }
            """)),

        new ToolDefinition(
            "present_scaffolding_plan",
            "Build and return the full scaffolding plan for review before approval",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "session_id": { "type": "string", "description": "Scaffolding session UUID" }
              },
              "required": ["session_id"]
            }
            """)),

        new ToolDefinition(
            "approve_scaffolding",
            "Approve the scaffolding plan and execute the full agent scaffold",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "session_id": { "type": "string", "description": "Scaffolding session UUID to approve and execute" }
              },
              "required": ["session_id"]
            }
            """)),

        new ToolDefinition(
            "list_scaffolded_agents",
            "List recent agent scaffolding history",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "limit": { "type": "number", "description": "Number of recent entries to return (default 10)" }
              }
            }
            """)),

        new ToolDefinition(
            "list_port_registry",
            "List all port assignments for all agents",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {}
            }
            """)),

        // ── Agent Lifecycle ───────────────────────────────────────────────────

        new ToolDefinition(
            "get_agent_info",
            "Get full details for an agent including scaffolding history and recent updates",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "agent_name": { "type": "string", "description": "Name of the agent" }
              },
              "required": ["agent_name"]
            }
            """)),

        new ToolDefinition(
            "update_agent_metadata",
            "Update a metadata field on an agent (description, routing_keywords, department, system_prompt_override, display_name, notes)",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "agent_name": { "type": "string", "description": "Agent to update" },
                "field":      { "type": "string", "description": "Field to update: description | routing_keywords | department | system_prompt_override | display_name | notes" },
                "value":      { "type": "string", "description": "New value for the field" }
              },
              "required": ["agent_name", "field", "value"]
            }
            """)),

        new ToolDefinition(
            "plan_agent_code_update",
            "Read the agent's source files and produce an implementation plan for a code change",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "agent_name":         { "type": "string", "description": "Agent to update" },
                "change_description": { "type": "string", "description": "Description of the code change required" }
              },
              "required": ["agent_name", "change_description"]
            }
            """)),

        new ToolDefinition(
            "execute_agent_code_update",
            "Execute an approved code change on an agent: develop files, build, restart, commit, push",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "agent_name":      { "type": "string", "description": "Agent to update" },
                "plan_summary":    { "type": "string", "description": "Short summary of the change (used as commit message)" },
                "files_to_modify": {
                  "type": "array",
                  "items": { "type": "string" },
                  "description": "Relative paths (from agent directory) of files to modify"
                }
              },
              "required": ["agent_name", "plan_summary", "files_to_modify"]
            }
            """)),

        new ToolDefinition(
            "soft_retire_agent",
            "Stop and deactivate an agent without removing source code. Requires CONFIRM gate for hand-built agents.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "agent_name": { "type": "string", "description": "Agent to soft-retire" },
                "confirm":    { "type": "boolean", "description": "Set to true to confirm retirement of a hand-built agent" }
              },
              "required": ["agent_name"]
            }
            """)),

        new ToolDefinition(
            "hard_retire_agent",
            "Permanently retire an agent: stop container, archive source, remove compose block, optionally drop schema. Always requires CONFIRM gate.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "agent_name":  { "type": "string",  "description": "Agent to hard-retire" },
                "drop_schema": { "type": "boolean", "description": "Whether to DROP the agent's database schema" },
                "confirm":     { "type": "boolean", "description": "Must be true to proceed. First call returns a confirmation prompt." }
              },
              "required": ["agent_name"]
            }
            """)),

        new ToolDefinition(
            "reactivate_agent",
            "Reactivate a soft-retired agent: restart container, poll health, update status",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "agent_name": { "type": "string", "description": "Agent to reactivate" }
              },
              "required": ["agent_name"]
            }
            """)),

        new ToolDefinition(
            "list_agents",
            "List all agents with their status, port, department, and scaffolding info",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "include_retired": { "type": "boolean", "description": "Include retired agents (default false)" }
              }
            }
            """))
    ];
}
