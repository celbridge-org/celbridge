---
name: add-agent-tests
description: Add tests to the Celbridge agent tests by writing or processing a proposal, which turns specified cases into plan rows and scripts that drive the real application. Use when the user asks to add agent tests, write a test proposal, or process a proposal ("process the downloads proposal"). Not for the .NET, JavaScript or Python unit suites.
---

# Add agent tests

The agent tests, their proposals and the process that adds tests to them live in the private
`celbridge-tests` repository, checked out beside this one. This skill only finds them.

1. Check that `../celbridge-tests` exists beside this checkout. If it does not, stop and tell the user the
   agent tests need that repository cloned there.
2. Read `../celbridge-tests/proposals.md` and `../celbridge-tests/agents/claude.md`, and follow them. They say
   how to write a proposal, how to process one into a plan and its scripts, and what to run and reply.

Never commit, pull, fetch, switch branches or stash in either repository.
