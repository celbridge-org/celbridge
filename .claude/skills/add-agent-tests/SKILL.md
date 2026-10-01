---
name: add-agent-tests
description: Add or update Celbridge agent test cases, the scripted cases that drive the real application, in the same change as the code. Use whenever implementing a feature or fixing a defect whose behavior is visible only in the running application, such as input routing, focus, a dialog or a console's environment, without waiting to be asked, and when the user asks to add, sketch or update agent tests. Not for the .NET, JavaScript or Python unit suites.
---

# Add agent tests

Behavior visible only in the running application gets agent test cases in the same change. The agent tests,
and how a case is written and validated, live in the private `celbridge-tests` repository, checked out beside
this one. This skill only finds them.

1. Check that `../celbridge-tests` exists beside this checkout. If it does not, stop and tell the user the
   agent tests need that repository cloned there.
2. Read `../celbridge-tests/docs/writing_tests.md` and follow it. It says where a case goes, how to choose its
   kind, how to write its script, and how to check it.
3. Before the run that validates a new case, read `../celbridge-tests/docs/protocol.md` and
   `../celbridge-tests/docs/running_with_claude.md`, and ask the user before starting it, since a run takes
   over the machine.

Never commit, pull, fetch, switch branches or stash in either repository.
