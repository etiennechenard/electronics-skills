# The wall clock (ZCode only)

Claude Code spawns are foreground and bounded by the agent's maxTurns — skip this file there.

On ZCode each shard is spawned with `run_in_background: true`. Deadline: **30 minutes from the spawn**, per shard — it stops runaway shards, never paces healthy ones. Collect in shard order, blocking with TaskOutput (one wait caps at 10 min; re-block to the deadline; a shard reached with its deadline already past is judged on arrival). Still running at its deadline → **TaskStop + exactly one SendMessage wrap-up** ("do not investigate; emit the `REVIEW:` line from what you have"); its output is the report; still nothing usable → the empty-report re-run rule. The deadline is never extended. The final report's plan line names the wall clock.
