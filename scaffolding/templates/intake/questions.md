I'll scaffold this new agent for you. To build the right thing, I need a few details:

1. **What does this agent do?** (one clear sentence — this becomes its description in Jarvis)

2. **Which department?**
   IT Operations / Development / Projects / Finance / Personal / Research

3. **What data does it need access to?** (select all that apply)
   a) SSH access to managed servers (read-only or read-write?)
   b) Database access (which database? read-only or can it write?)
   c) External HTTP APIs (which ones?)
   d) None — conversational and LLM-only

4. **Does it need scheduled background jobs?**
   If yes: what should run, and how often?

5. **Should it remember conversations?**
   Stateful (remembers context between sessions, like Andrew) or
   Stateless (each request is independent, like Rocky)?

6. **Name 8–12 routing keywords** — words or phrases Jarvis should use to route
   messages to this agent (e.g. "database", "slow query", "mysql").

Answer all six and I'll put together a complete plan.
