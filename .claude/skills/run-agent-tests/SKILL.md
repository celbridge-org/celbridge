---
name: run-agent-tests
description: Run the Celbridge agent tests, the scripted cases that drive the real application, against this checkout. Use when the user asks to run the agent tests, optionally naming a test file or area ("run the console agent tests"), cases, a level, or a Release build. Not for the .NET, JavaScript or Python unit suites.
---

# Run the agent tests

The agent tests, their driver and their run instructions live in the private `celbridge-tests` repository,
checked out beside this one. This skill only finds them.

1. Check that `../celbridge-tests` exists beside this checkout. If it does not, stop and tell the user the
   agent tests need that repository cloned there.
2. Read `../celbridge-tests/docs/protocol.md` and `../celbridge-tests/docs/running_with_claude.md`, and follow
   them. They say how to check the machine, start the run the user asked for, triage it and reply.

Never commit, pull, fetch, switch branches or stash in either repository.
